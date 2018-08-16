#define PERMIT_STACKALLOC_OPTIMIZATIONS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using Canvas3D.LowLevel;
using Canvas3D.LowLevel.Direct3D;
using Canvas3D.LowLevel.Helpers;
using SharpDX;
using SharpDX.Direct3D11;
using Color = SharpDX.Color;
using IDeviceContext = Canvas3D.LowLevel.IDeviceContext;
using RectangleF = SharpDX.RectangleF;

namespace Canvas3D {
   public interface IScene {
      void Clear();
      void SetCamera(Vector3 cameraEye, Matrix projView);
      int AddMaterialResources(MaterialResourcesDescription desc);
      void AddRenderable(IMesh mesh, Matrix worldCm, MaterialDescription material, Color color = default(Color));
      void AddRenderable(IMesh mesh, Matrix worldCm, MaterialProperties materialProperties, int materialResourcesIndex, Color color);
      void AddRenderable(IMesh mesh, RenderJobDescription info);
      void AddRenderJobBatch(RenderJobBatch batch);
      void AddSpotlight(Vector3 position, Vector3 lookat, Vector3 up, float theta, Color color, float near, float far, float daRatioConstant, float daRatioLinear, float daRatioQuadratic, float edgeSpotlightAttenuationPercent = 1.0f / 256.0f);
      ISceneSnapshot ExportSnapshot();
      void ExportSnapshot(ISceneSnapshot snapshot);
   }

   public interface IRenderContext {
      void RenderScene(ISceneSnapshot scene);
   }

   public enum DiffuseTextureSamplingMode {
      FlatUV = 0,
      FlatUVGrayscale = 10,
      FlatUVGrayscaleDerivative = 11,
      FlatUVNoAlpha = 12,
      FlatUVUnpackMaterialW = 13,
      CubeObjectRelative = 20,
      CubeNormal = 21
   }

   public class Scene : IScene {
      private static readonly ConcurrentQueue<SceneSnapshot> snapshotPool = new ConcurrentQueue<SceneSnapshot>();
      private AddOnlyOrderedHashSet<IShaderResourceView> textures = new AddOnlyOrderedHashSet<IShaderResourceView>();
      private ExposedArrayList<InternalMaterialResourcesDescription> materials = new ExposedArrayList<InternalMaterialResourcesDescription>();
      private Dictionary<IMesh, RenderJobBatch> defaultRenderJobBatchesByMesh = new Dictionary<IMesh, RenderJobBatch>();
      private List<RenderJobBatch> renderJobBatches = new List<RenderJobBatch>();
      private List<SpotlightInfo> spotlightInfos = new List<SpotlightInfo>();

      private Vector3 _cameraEye;
      private Matrix _projView, _projViewInv;

      private float _time;

      public void Clear() {
         textures.Clear();
         materials.Clear();
         renderJobBatches.Clear();
         // Note: It's assumed safe to hold references to texture SRVs long-term; 
         // the graphics device will box an SRV, and disposing invalidates box contents.
         foreach (var kvp in defaultRenderJobBatchesByMesh) {
            kvp.Value.Jobs.Clear();
            renderJobBatches.Add(kvp.Value);
         }
         spotlightInfos.Clear();
      }

      public void SetCamera(Vector3 cameraEye, Matrix projView) {
         _cameraEye = cameraEye;
         _projView = projView;
         _projViewInv = Matrix.Invert(projView);
      }

      private int GetOrAddTextureIndex(IShaderResourceView textureSrv) {
         if (textureSrv == null) {
            return -1;
         }
         textures.TryAdd(textureSrv, out int baseTextureIndex);
         return baseTextureIndex;
      }

      public int AddMaterialResources(MaterialResourcesDescription desc) {
         materials.Add(desc.ToInternal(GetOrAddTextureIndex(desc.BaseTexture)));
         return materials.Count - 1;
      }

      public void AddRenderable(IMesh mesh, Matrix worldCm, MaterialDescription material, Color color = default(Color)) {
         AddRenderable(mesh, worldCm, material.Properties, AddMaterialResources(material.Resources), color == default(Color) ? Color.White : color);
      }

      public void AddRenderable(IMesh mesh, Matrix worldCm, MaterialProperties materialProperties, int materialResourcesIndex, Color color) {
         AddRenderable(mesh, new RenderJobDescription {
            WorldTransform = worldCm,
            MaterialProperties = materialProperties,
            MaterialResourcesIndex = materialResourcesIndex,
            Color = color
         });
      }

      public void AddRenderable(IMesh mesh, RenderJobDescription info) {
         if (!defaultRenderJobBatchesByMesh.TryGetValue(mesh, out var batch)) {
            batch = defaultRenderJobBatchesByMesh[mesh] = RenderJobBatch.Create(mesh);
            renderJobBatches.Add(batch);
         }
         batch.Jobs.Add(info);
      }

      public void AddRenderJobBatch(RenderJobBatch batch) {
         renderJobBatches.Add(batch);
      }

      public void AddSpotlight(Vector3 position, Vector3 lookat, Vector3 up, float theta, Color color, float near, float far, float daRatioConstant, float daRatioLinear, float daRatioQuadratic, float edgeSpotlightAttenuationPercent = 1.0f / 256.0f) {
         var projCm = MatrixCM.PerspectiveFovRH(theta, 1.0f, near, far);

         var viewCm = MatrixCM.ViewLookAtRH(position, lookat, up);

         // solve distance attenuation constants: 1/256 = atten = 1 / (x * darc + far * x * darl + far * far * x * darq)
         // 256 = x * darc + far * x * darl + far * far * x * darq
         // 256 = x * (darc + far * darl + far * far * darq)
         var x = 256 / (daRatioConstant + far * daRatioLinear + far * far * daRatioQuadratic);
         //Console.WriteLine(x + " " + daRatioConstant + " " + daRatioLinear + " " + daRatioQuadratic);
         var direction = lookat - position;
         direction.Normalize();
         //Console.WriteLine("@8: " + (daRatioConstant * x + daRatioLinear * x * 8 + daRatioQuadratic * x * 8 * 8));
         //Console.WriteLine("@far: " + (daRatioConstant * x + daRatioLinear * x * far + daRatioQuadratic * x * far * far));

         // solve spotlight attenuation constant.
         // edge% = atten_spotlight = dot(spotlightDirectionUnit, objectDirectionUnit) ^ power
         // edge% = cos(theta) ^ power
         // Math.Log(edge%, cos(theta)) = power
         var power = Math.Log(edgeSpotlightAttenuationPercent, Math.Cos(theta));
         //Console.WriteLine("Power: " + power);
         //Console.WriteLine("@far: " + Math.Pow(Math.Cos(theta), power));

         var projViewCm = projCm * viewCm;

         AddSpotlight(new SpotlightInfo {
            Origin = position,
            Direction = direction,

            Color = color,
            DistanceAttenuationConstant = x * daRatioConstant,
            DistanceAttenuationLinear = x * daRatioLinear,
            DistanceAttenuationQuadratic = x * daRatioQuadratic,
            SpotlightAttenuationPower = (float)power,

            ProjViewCM = projViewCm,
         });
      }

      private void AddSpotlight(SpotlightInfo info) {
         spotlightInfos.Add(info);
      }

      public void SetTime(float t) {
         _time = t;
      }

      public ISceneSnapshot ExportSnapshot() {
         if (!snapshotPool.TryDequeue(out var snapshot)) {
            snapshot = new SceneSnapshot();
         }

         Trace.Assert(snapshot.HandleCount == 0);
         snapshot.HandleCount++;

         ExportSnapshot(snapshot);
         return snapshot;
      }

      public void ExportSnapshot(ISceneSnapshot s) {
         var snapshot = (SceneSnapshot)s;
         snapshot.Textures.Clear();
         foreach (var tex in textures) snapshot.Textures.TryAdd(tex);

         snapshot.Materials.Clear();
         foreach (var mat in materials) snapshot.Materials.Add(mat);

         snapshot.RenderJobBatches.Clear();
         foreach (var batch in renderJobBatches) snapshot.RenderJobBatches.Add(batch);

         snapshot.SpotlightInfos.Clear();
         foreach (var spotlightInfo in spotlightInfos) snapshot.SpotlightInfos.Add(spotlightInfo);

         snapshot.CameraEye = _cameraEye;
         snapshot.ProjView = _projView;
         snapshot.ProjViewInv = _projViewInv;

         foreach (var batch in snapshot.RenderJobBatches)
            if (batch.MaterialResourcesIndexOverride != -1)
               foreach (var job in batch.Jobs)
                  if (job.MaterialResourcesIndex >= snapshot.Materials.Count)
                     throw new InvalidOperationException();

         snapshot.Time = _time;
      }

      internal static void ReturnSnapshot(SceneSnapshot sceneSnapshot) {
         snapshotPool.Enqueue(sceneSnapshot);
      }
   }

   internal class RenderContext : IRenderContext {
      private const int kShadowMapWidthHeight = 256 * 4;
      private const int kBatchNoMaterialIndexOverride = -1;
      private const int kBaseTextureSlotId = 48;
      private const int kTextureBindLimit = 80; // Slots [48, 127)
      private const int kMaterialBufferCount = 256; // Slots [48, 127)

      private readonly IGraphicsFacade _graphicsFacade;
      private readonly IGraphicsDevice _graphicsDevice;
      private readonly ITechniqueCollection _techniques;
      private readonly IPresetsStore _presets;

      private readonly IBuffer<SceneConstantBufferData> _sceneBuffer;
      private readonly IBuffer<BatchConstantBufferData> _batchBuffer;
      private readonly IBuffer<TextureDescriptorConstantBufferData> _textureDescriptorBuffer;
      private readonly List<IBuffer<RenderJobDescription>> _instancingBuffers;
      private readonly IRenderTargetView[] _gBufferRtvs;
      private readonly IShaderResourceView _gBufferSrv;
      private readonly IShaderResourceView[] _gBufferSrvs;
      private readonly IDepthStencilView _gBufferDsv;
      private readonly IShaderResourceView _gBufferDepthSrv;

      private readonly IBuffer<SpotlightDescription> _spotlightDescriptionsBuffer;
      private readonly IShaderResourceView _spotlightDescriptionsBufferSrv;
      private readonly IBuffer<InternalMaterialResourcesDescription> _materialResourcesBuffer;
      private readonly IShaderResourceView _materialResourcesBufferSrv;

      private readonly IDepthStencilView[] _lightDepthStencilViews;
      private readonly ITexture2D _lightDepthTexture;
      private readonly IShaderResourceView _lightShaderResourceView;
      private readonly IShaderResourceView[] _lightShaderResourceViews;


      // hack
      private readonly WaterWip water;

      internal RenderContext(IGraphicsFacade graphicsFacade) {
         _graphicsFacade = graphicsFacade;
         _graphicsDevice = graphicsFacade.Device;
         _techniques = graphicsFacade.Techniques;
         _presets = graphicsFacade.Presets;

         _sceneBuffer = _graphicsDevice.CreateConstantBuffer<SceneConstantBufferData>(1);
         _batchBuffer = _graphicsDevice.CreateConstantBuffer<BatchConstantBufferData>(1);
         _textureDescriptorBuffer = _graphicsDevice.CreateConstantBuffer<TextureDescriptorConstantBufferData>(kBaseTextureSlotId + kTextureBindLimit);
         _instancingBuffers = new List<IBuffer<RenderJobDescription>>();

         const int kMaxPreallocatedInstanceBufferPower = 20;
         for (var i = 0; i <= kMaxPreallocatedInstanceBufferPower; i++) {
            _instancingBuffers.Add(_graphicsDevice.CreateVertexBuffer<RenderJobDescription>(1 << i));
         }

         (_gBufferRtvs, _gBufferSrv, _gBufferSrvs) = _graphicsDevice.CreateScreenSizeRenderTarget(2);
         (_gBufferDsv, _gBufferDepthSrv) = _graphicsDevice.CreateScreenSizeDepthTarget();
         (_spotlightDescriptionsBuffer, _spotlightDescriptionsBufferSrv) = _graphicsDevice.CreateStructuredBufferAndView<SpotlightDescription>(256);
         (_materialResourcesBuffer, _materialResourcesBufferSrv) = _graphicsDevice.CreateStructuredBufferAndView<InternalMaterialResourcesDescription>(kMaterialBufferCount);
         (_lightDepthTexture, _lightDepthStencilViews, _lightShaderResourceView, _lightShaderResourceViews) = _graphicsDevice.CreateDepthTextureAndViews(10, new Size(kShadowMapWidthHeight, kShadowMapWidthHeight));

         Trace.Assert(Utilities.SizeOf<SpotlightInfo>() == SpotlightInfo.Size);
         Trace.Assert(Utilities.SizeOf<AtlasLocation>() == AtlasLocation.SIZE);
         Trace.Assert(Utilities.SizeOf<SpotlightDescription>() == SpotlightDescription.Size);
         Trace.Assert(Utilities.SizeOf<RenderJobDescription>() == RenderJobDescription.Size);
         Trace.Assert(Utilities.SizeOf<InternalMaterialResourcesDescription>() == InternalMaterialResourcesDescription.Size);

         water = new WaterWip(graphicsFacade);
         water.Initialize();
      }

      public unsafe void RenderScene(ISceneSnapshot scene) {
         scene.AddReference();
         _graphicsDevice.ImmediateContext.GetBackBufferViews(out var backBufferDepthStencilView, out var backBufferRenderTargetView);
         var renderContext = _graphicsDevice.ImmediateContext; // : _graphicsDevice.CreateDeferredRenderContext();
         renderContext.SetRasterizerConfiguration(RasterizerConfiguration.FillFrontBack);
         renderContext.SetDepthConfiguration(DepthConfiguration.Enabled);

         renderContext.SetConstantBuffer(0, _sceneBuffer, RenderStage.VertexDomainPixel);
         renderContext.SetConstantBuffer(1, _batchBuffer, RenderStage.VertexDomainPixel);
         renderContext.SetConstantBuffer(2, _textureDescriptorBuffer, RenderStage.VertexDomainPixel);

         if (true) {
            RenderScene_Forward(renderContext, (SceneSnapshot)scene, backBufferDepthStencilView, backBufferRenderTargetView);
         } else {

         }

         renderContext.SetConstantBuffer(0, null, RenderStage.VertexDomainPixel);
         renderContext.SetConstantBuffer(1, null, RenderStage.VertexDomainPixel);
         renderContext.SetConstantBuffer(2, null, RenderStage.VertexDomainPixel);

         _graphicsDevice.ImmediateContext.SetRenderTargets(backBufferDepthStencilView, backBufferRenderTargetView);
         _graphicsDevice.ImmediateContext.Present();
         scene.ReleaseReference();
      }

      private void RenderScene_Forward(IDeviceContext context, SceneSnapshot scene, IDepthStencilView backBufferDepthStencilView, IRenderTargetView backBufferRenderTargetView) {
         RenderShadowMaps(context, scene);

         // Restore backbuffer rendertarget + scene constant buffer + srvs.
         context.SetRenderTargets(backBufferDepthStencilView, backBufferRenderTargetView);
         context.SetViewportRect(new RectangleF(0, 0, backBufferRenderTargetView.Resolution.Width, backBufferRenderTargetView.Resolution.Height));
         UpdateSceneConstantBuffer(context, new Vector4(scene.CameraEye, 1.0f), scene.ProjView, scene.ProjViewInv, scene.ProjView, scene.ProjViewInv, true, true, scene.SpotlightInfos.Count, scene.Time);

         // Clear render/depth, bind srvs after setrendertargets
         context.ClearRenderTarget(Color.Gray);
         context.ClearDepthBuffer(1.0f);
         BindCommonShaderResourceViews(context);

         // Atmosphere
         if (false) {
            _techniques.ForwardSkyFromAtmosphere.BeginPass(context, 0);
            context.SetDepthConfiguration(DepthConfiguration.Disabled);
            var (orthoProj, world) = ComputeSceneQuadProjWorld(backBufferRenderTargetView.Resolution, 0, 0, backBufferRenderTargetView.Resolution.Width, backBufferRenderTargetView.Resolution.Height);
            UpdateSceneConstantBuffer(context, new Vector4(scene.CameraEye, 1), orthoProj, MatrixCM.Invert(orthoProj), scene.ProjView, MatrixCM.Invert(scene.ProjView), false, false, scene.SpotlightInfos.Count, scene.Time);
            UpdateBatchConstantBuffer(context, Matrix.Identity, DiffuseTextureSamplingMode.FlatUV, 0);
            DrawScreenQuad(context, world, null);
            context.SetDepthConfiguration(DepthConfiguration.Enabled);
            {
               var projViewInv = MatrixCM.Invert(scene.ProjView);
               projViewInv.Transpose();
               var a = Vector4.Transform(new Vector4(0, 0, 1, 1), projViewInv);
               a /= a.W;
               var av = (Vector3)a;
               av.Normalize();
               var b = Vector4.Transform(new Vector4(1, 0, 1, 1), projViewInv);
               b /= b.W;
               var bv = (Vector3)b;
               bv.Normalize();
               var c = Vector4.Transform(new Vector4(0, 1, 1, 1), projViewInv);
               c /= c.W;
               var cv = (Vector3)c;
               cv.Normalize();
               Console.WriteLine(av + " " + bv + " " + cv);
            }
         }

         // Water
         if (false) {
            var pv = scene.ProjView;
            pv.Transpose();
            Console.WriteLine("!!!!!" + Vector3.Transform(new Vector3(-5.00f, -2.18557e-07f, -5.00f), pv));

            _techniques.ForwardWater.BeginPass(context, 0);
            UpdateSceneConstantBuffer(context, new Vector4(scene.CameraEye, 1), scene.ProjView, scene.ProjViewInv, scene.ProjView, scene.ProjViewInv, false, false, scene.SpotlightInfos.Count, scene.Time);
            UpdateBatchConstantBuffer(context, Matrix.Identity, DiffuseTextureSamplingMode.FlatUV, 0);
            var instancingBuffer = PickInstancingBuffer(8192);
            context.SetVertexBuffer(1, instancingBuffer);
            int n = 40;
            using (var updater = context.TakeUpdater(instancingBuffer)) {
               for (var y = -n; y <= n; y++) {
                  for (var x = -n; x <= n; x++) {
                     updater.Write(new RenderJobDescription {
                        WorldTransform = MatrixCM.Scaling(10) * MatrixCM.Translation(x, 0, y) * MatrixCM.RotationX(-(float)Math.PI / 2.0f) * MatrixCM.Translation(-0.5f, -0.5f, 0),
                        MaterialProperties = { Metallic = 0.0f, Roughness = 1.0f },
                        MaterialResourcesIndex = -1,
                        Color = Color.White,
                     });
                  }
               }
            }
            context.SetShaderResource(30, _graphicsFacade.Presets.SolidTextures[Color4.White], RenderStage.Pixel);
            var mrbu = context.TakeUpdater(_materialResourcesBuffer);
            mrbu.Write(new InternalMaterialResourcesDescription() {
               BaseColor = Color4.White
            }.Resolve(30));
            mrbu.UpdateCloseAndDispose();
            water.Render(context, (2 * n + 1) * (2 * n + 1));
         }

         // Forward render pass
         for (var pass = 0; pass < _techniques.Forward.Passes; pass++) {
            _techniques.Forward.BeginPass(context, pass);
            foreach (var batch in scene.RenderJobBatches) {
               RenderBatch(context, scene, batch);
            }
         }
      }

      private void RenderBatch(IDeviceContext context, SceneSnapshot scene, RenderJobBatch batch) {
         context.SetRasterizerConfiguration(batch.Wireframe ? RasterizerConfiguration.WireFrontBack : RasterizerConfiguration.FillFrontBack);

         if (batch.MaterialResourcesIndexOverride != -1) {
            RenderBatch_MaterialPerBatch(context, scene, batch);
         } else {
            RenderBatch_MaterialPerInstance(context, scene, batch);
         }
      }

      private void RenderBatch_MaterialPerBatch(IDeviceContext context, SceneSnapshot scene, RenderJobBatch batch) {
         ref var material = ref scene.Materials.store[batch.MaterialResourcesIndexOverride];

         // Bind textures
         context.SetShaderResource(30, scene.Textures[material.BaseTextureIndex], RenderStage.Pixel);

         // Write to material resource index 0
         var mrbu = context.TakeUpdater(_materialResourcesBuffer);
         mrbu.Write(material.Resolve(30));
         mrbu.UpdateCloseAndDispose();
         context.Update(_materialResourcesBuffer, material.Resolve(30));

         // Prepare draw
         UpdateBatchConstantBuffer(context, batch.BatchTransform, DiffuseTextureSamplingMode.CubeNormal, 0);

         // Pick instancing buffer, update (jobs fully correct, job resource indices will be ignored).
         var instancingBuffer = PickInstancingBuffer(batch.Jobs.Count);
         context.Update(instancingBuffer, batch.Jobs.store, 0, batch.Jobs.Count);

         // Set instance buffer, draw.
         context.SetVertexBuffer(1, instancingBuffer);
         batch.Mesh.Draw(context, batch.Jobs.Count);
         context.SetVertexBuffer(1, null);
      }

      private unsafe void RenderBatch_MaterialPerInstance(IDeviceContext context, SceneSnapshot scene, RenderJobBatch batch) {
         // sort jobs by MRI
#if PERMIT_STACKALLOC_OPTIMIZATIONS
         var mris = stackalloc int[batch.Jobs.store.Length];
         var jobIndexer = stackalloc int[batch.Jobs.store.Length];
#else
         var mris = new int[batch.Jobs.store.Length];
         var jobIndexer = new int[batch.Jobs.store.Length];
#endif
         for (var i = 0; i < batch.Jobs.store.Length; i++) {
            mris[i] = batch.Jobs.store[i].MaterialResourcesIndex;
            jobIndexer[i] = i;
         }

#if PERMIT_STACKALLOC_OPTIMIZATIONS
         UnmanagedCollections.IndirectSort(mris, jobIndexer, 0, batch.Jobs.store.Length);
#else
         fixed (int* pmris = mris)
            fixed(int* pjobIndexer = jobIndexer)
               UnmanagedCollections.IndirectSort(pmris, pjobIndexer, 0, batch.Jobs.store.Length);
#endif
         //         UnmanagedCollections.IndirectSort(mris, jobIndexer, 0, batch.Jobs.store.Length);

#if PERMIT_STACKALLOC_OPTIMIZATIONS
         var boundTextureSlotByTextureIndex = stackalloc int[scene.Textures.Count]; // todo: stackalloc
#else
         var boundTextureSlotByTextureIndex = new int[scene.Textures.Count]; // todo: stackalloc
#endif
         for (var i = 0; i < scene.Textures.Count; i++) {
            boundTextureSlotByTextureIndex[i] = -1;
         }

#if PERMIT_STACKALLOC_OPTIMIZATIONS
         var mriBound = stackalloc int[scene.Materials.Count];
#else
         var mriBound = new int[scene.Materials.Count]; // todo: stackalloc
#endif
         for (var i = 0; i < scene.Materials.Count; i++) {
            mriBound[i] = -1;
         }

         int boundTextures = 0;
         int boundMaterialResourceDescriptions = 0;

         var tdbUpdater = context.TakeUpdater(_textureDescriptorBuffer);
         var mrbUpdater = context.TakeUpdater(_materialResourcesBuffer);

         var instancingBuffer = PickInstancingBuffer(batch.Jobs.Count);
         var instancingBufferUpdater = context.TakeUpdater(instancingBuffer);

         int instancesToRender = 0;
         var whiteDefaultTextureBound = -1;

         for (var i = 0; i < batch.Jobs.Count; i++) {
            var materialResourcesIndex = batch.Jobs[i].MaterialResourcesIndex;
            if (mriBound[materialResourcesIndex] == -1) {
               if (boundMaterialResourceDescriptions + 1 == kMaterialBufferCount || boundTextures > kTextureBindLimit - 4) {
                  instancingBufferUpdater.UpdateAndClose();
                  tdbUpdater.UpdateAndClose();
                  mrbUpdater.UpdateAndClose();

                  // shouldnt be necessary
                  context.SetShaderResource(10, _lightShaderResourceView, RenderStage.Pixel);
                  context.SetShaderResource(11, _spotlightDescriptionsBufferSrv, RenderStage.Pixel);
                  context.SetShaderResource(12, _materialResourcesBufferSrv, RenderStage.Pixel);

                  // Prepare draw
                  UpdateBatchConstantBuffer(context, batch.BatchTransform, DiffuseTextureSamplingMode.CubeNormal, -1);

                  // Set instance buffer, draw.
                  context.SetVertexBuffer(1, instancingBuffer);
                  batch.Mesh.Draw(context, instancesToRender);
                  context.SetVertexBuffer(1, null);

                  // Reset state
                  for (var j = 0; j < scene.Textures.Count; j++) {
                     boundTextureSlotByTextureIndex[j] = -1;
                  }

                  for (var j = 0; j < scene.Materials.Count; j++) {
                     mriBound[j] = -1;
                  }

                  boundTextures = 0;
                  boundMaterialResourceDescriptions = 0;
                  instancesToRender = 0;

                  whiteDefaultTextureBound = -1;

                  instancingBufferUpdater.Reopen();
                  tdbUpdater.Reopen();
                  mrbUpdater.Reopen();
               }

               ref var material = ref scene.Materials.store[materialResourcesIndex];

               // Ensure base texture bound
               var isTextureBound = material.BaseTextureIndex == -1
                  ? whiteDefaultTextureBound != -1
                  : boundTextureSlotByTextureIndex[material.BaseTextureIndex] != -1;
               if (!isTextureBound) {
                  var textureSlot = boundTextures + kBaseTextureSlotId;
                  if (material.BaseTextureIndex == -1) {
                     var textureSrv = _presets.SolidCubeTextures[Color4.White];
                     context.SetShaderResource(textureSlot, textureSrv, RenderStage.Pixel);
                     whiteDefaultTextureBound = textureSlot;
                  } else {
                     var textureSrv = scene.Textures[material.BaseTextureIndex];
                     context.SetShaderResource(textureSlot, textureSrv, RenderStage.Pixel);
                     boundTextureSlotByTextureIndex[material.BaseTextureIndex] = textureSlot;

                     if (material.BaseTextureIndex >= scene.Textures.Count) {
                        throw new IndexOutOfRangeException();
                     }
                  }
                  boundTextures++;

                  tdbUpdater.Write(new TextureDescriptorConstantBufferData { isCubeMap = 1 });
               }

               mrbUpdater.Write(material.Resolve(
                  material.BaseTextureIndex == -1
                     ? whiteDefaultTextureBound
                     : boundTextureSlotByTextureIndex[material.BaseTextureIndex]
               ));

               if (materialResourcesIndex >= scene.Materials.Count) {
                  throw new IndexOutOfRangeException();
               }
               mriBound[materialResourcesIndex] = boundMaterialResourceDescriptions;
               boundMaterialResourceDescriptions++;
            }
            instancingBufferUpdater.Write(batch.Jobs[i].Resolve(mriBound[materialResourcesIndex]));
            instancesToRender++;
         }
         instancingBufferUpdater.UpdateCloseAndDispose();
         tdbUpdater.UpdateCloseAndDispose();
         mrbUpdater.UpdateCloseAndDispose();

         // Prepare draw
         UpdateBatchConstantBuffer(context, batch.BatchTransform, DiffuseTextureSamplingMode.CubeNormal, -1);

         // shouldnt be necessary
         context.SetShaderResource(10, _lightShaderResourceView, RenderStage.Pixel);
         context.SetShaderResource(11, _spotlightDescriptionsBufferSrv, RenderStage.Pixel);
         context.SetShaderResource(12, _materialResourcesBufferSrv, RenderStage.Pixel);

         // Set instance buffer, draw.
         context.SetVertexBuffer(1, instancingBuffer);
         batch.Mesh.Draw(context, instancesToRender);
         context.SetVertexBuffer(1, null);
      }

      private unsafe void RenderShadowMaps(IDeviceContext context, SceneSnapshot scene) {
         // Allocate shadow map atlas
         var spotlightDescriptions = stackalloc SpotlightDescription[scene.SpotlightInfos.Count];
         for (var i = 0; i < scene.SpotlightInfos.Count; i++) {
            spotlightDescriptions[i].AtlasLocation = new AtlasLocation {
               Position = new Vector3(0, 0, i),
               Size = new Vector2(1, 1)
            };
            spotlightDescriptions[i].SpotlightInfo = scene.SpotlightInfos[i];
         }

         // Batch shadow map descriptor to gpu (used in lighting passes after this function call)
         context.Update(_spotlightDescriptionsBuffer, (IntPtr)spotlightDescriptions, scene.SpotlightInfos.Count);

         // Clear shadow map buffers.
#if PERMIT_STACKALLOC_OPTIMIZATIONS
         var lightDepthStencilViewCleared = stackalloc bool[_lightDepthStencilViews.Length];
#else
         var lightDepthStencilViewCleared = new bool[_lightDepthStencilViews.Length];
#endif
         for (var i = 0; i < scene.SpotlightInfos.Count; i++) {
            var ldsvIndex = (int)spotlightDescriptions[i].AtlasLocation.Position.Z;
            if (ldsvIndex >= _lightDepthStencilViews.Length) throw new IndexOutOfRangeException();
            if (lightDepthStencilViewCleared[ldsvIndex]) continue;
            lightDepthStencilViewCleared[ldsvIndex] = true;
            context.SetRenderTargets(_lightDepthStencilViews[ldsvIndex], null);
            context.ClearDepthBuffer(1.0f);
         }

         // shadow passes
         for (var spotlightIndex = 0; spotlightIndex < scene.SpotlightInfos.Count; spotlightIndex++) {
            var spotlightDescription = &spotlightDescriptions[spotlightIndex];
            context.SetRenderTargets(_lightDepthStencilViews[(int)spotlightDescription->AtlasLocation.Position.Z], null);
            context.SetViewportRect((Vector2)spotlightDescription->AtlasLocation.Position, kShadowMapWidthHeight * spotlightDescription->AtlasLocation.Size);

            UpdateSceneConstantBuffer(context, new Vector4(spotlightDescription->SpotlightInfo.Origin, 1.0f), spotlightDescription->SpotlightInfo.ProjViewCM, scene.ProjView, scene.ProjViewInv, Matrix.Zero, false, false, 0, scene.Time);
            for (var pass = 0; pass < _techniques.ForwardDepthOnly.Passes; pass++) {
               _techniques.ForwardDepthOnly.BeginPass(context, pass);

               foreach (var batch in scene.RenderJobBatches) {
                  UpdateBatchConstantBuffer(context, batch.BatchTransform, 0, batch.MaterialResourcesIndexOverride);

                  var instancingBuffer = PickInstancingBuffer(batch.Jobs.Count);
                  context.Update(instancingBuffer, batch.Jobs.store, 0, batch.Jobs.Count);
                  context.SetVertexBuffer(1, instancingBuffer);
                  batch.Mesh.Draw(context, batch.Jobs.Count);
                  context.SetVertexBuffer(1, null);
               }
            }
         }
      }

      private void BindCommonShaderResourceViews(IDeviceContext context) {
         context.SetShaderResource(10, _lightShaderResourceView, RenderStage.Pixel);
         context.SetShaderResource(11, _spotlightDescriptionsBufferSrv, RenderStage.Pixel);
         context.SetShaderResource(12, _materialResourcesBufferSrv, RenderStage.Pixel);
      }

      private (Matrix, Matrix) ComputeSceneQuadProjWorld(Size renderTargetSize, float x, float y, float w, float h, float z = -1.0f) {
         var orthoProj = MatrixCM.OrthoOffCenterRH(0.0f, renderTargetSize.Width, renderTargetSize.Height, 0.0f, 0.1f, 100.0f); // top-left origin
         var quadWorld = MatrixCM.Translation(x, y, 0) * MatrixCM.Scaling(w, h, 1) * MatrixCM.Translation(0.5f, 0.5f, z);
         return (orthoProj, quadWorld);
      }

      private void DrawScreenQuad(IDeviceContext deviceContext, Matrix world, IShaderResourceView textureSrv0, IShaderResourceView textureSrv1 = null, IShaderResourceView textureSrv2 = null) {
         var instancingBuffer = PickInstancingBuffer(1);
         deviceContext.Update(instancingBuffer, new RenderJobDescription { WorldTransform = world });

         deviceContext.SetShaderResource(0, textureSrv0, RenderStage.Pixel);
         deviceContext.SetShaderResource(1, textureSrv1, RenderStage.Pixel);
         deviceContext.SetShaderResource(2, textureSrv2, RenderStage.Pixel);

         deviceContext.SetVertexBuffer(1, instancingBuffer);
         _presets.UnitPlaneXY.Draw(deviceContext, 1);
         deviceContext.SetVertexBuffer(1, null);
      }

      private void UpdateSceneConstantBuffer(IDeviceContext deviceContext, Vector4 cameraEye, Matrix projViewCamera, Matrix projViewCameraInv, Matrix projViewMain, Matrix projViewMainInv, bool pbrEnabled, bool shadowTestEnabled, int numSpotlights, float time) {
         deviceContext.Update(_sceneBuffer, new SceneConstantBufferData {
            cameraEye = cameraEye,
            projViewCamera = projViewCamera,
            projViewCameraInv = projViewCameraInv,
            projViewMain = projViewMain,
            projViewMainInv = projViewMainInv,
            pbrEnabled = pbrEnabled ? 1 : 0,
            shadowTestEnabled = shadowTestEnabled ? 1 : 0,
            numSpotlights = numSpotlights,
            time = time
         });
      }

      private void UpdateBatchConstantBuffer(IDeviceContext deviceContext, Matrix batchTransform, DiffuseTextureSamplingMode diffuseSamplingMode, int batchMaterialIndexOverride) {
         deviceContext.Update(_batchBuffer, new BatchConstantBufferData {
            batchTransform = batchTransform,
            diffuseSamplingMode = (int)diffuseSamplingMode,
            batchMaterialIndexOverride = batchMaterialIndexOverride,
            padding0 = 0,
            padding1 = 0,
         });
      }

      private IBuffer<RenderJobDescription> PickInstancingBuffer(int sz) {
         for (var i = 0; i <= _instancingBuffers.Count; i++) {
            var capacity = 1 << i;
            if (sz <= capacity) {
               return _instancingBuffers[i];
            }
         }
         throw new ArgumentOutOfRangeException();
      }

      [StructLayout(LayoutKind.Sequential, Pack = 1)]
      private struct SceneConstantBufferData {
         public Vector4 cameraEye;
         public Matrix projViewCamera;
         public Matrix projViewCameraInv;
         public Matrix projViewMain; // projview of main camera (if projViewCamera is for something else like screenspace quad)
         public Matrix projViewMainInv; // projview of main camera (if projViewCamera is for something else like screenspace quad)
         public int pbrEnabled;
         public int shadowTestEnabled;
         public int numSpotlights;
         public float time;

         public const int Size = 16 + 64 * 4 + 4 * 3 + 4;
      }

      [StructLayout(LayoutKind.Sequential, Pack = 1)]
      private struct BatchConstantBufferData {
         public Matrix batchTransform;
         public int diffuseSamplingMode;
         public int batchMaterialIndexOverride; // -1 default
         public int padding0, padding1;

         public const int Size = 64 + 4 + 12;
      }

      [StructLayout(LayoutKind.Sequential, Pack = 4)]
      private struct TextureDescriptorConstantBufferData {
         public int isCubeMap;

         public const int Size = 4;
      }

      [StructLayout(LayoutKind.Sequential, Pack = 1)]
      private struct AtlasLocation {
         public Vector3 Position;
         public Vector2 Size;

         public const int SIZE = 4 * (3 + 2);
      }

      [StructLayout(LayoutKind.Sequential, Pack = 1)]
      private struct SpotlightDescription {
         public SpotlightInfo SpotlightInfo;
         public AtlasLocation AtlasLocation;

         public const int Size = AtlasLocation.SIZE + SpotlightInfo.Size;
      }
   }

   public interface ISceneSnapshot {
      ISceneSnapshot AddReference();
      ISceneSnapshot ReleaseReference();
   }

   internal class SceneSnapshot : ISceneSnapshot {
      public AddOnlyOrderedHashSet<IShaderResourceView> Textures = new AddOnlyOrderedHashSet<IShaderResourceView>();
      public ExposedArrayList<InternalMaterialResourcesDescription> Materials = new ExposedArrayList<InternalMaterialResourcesDescription>();
      // public Dictionary<IMesh, RenderJobBatch> DefaultRenderJobBatchesByMesh = new Dictionary<IMesh, RenderJobBatch>();
      public List<RenderJobBatch> RenderJobBatches = new List<RenderJobBatch>();
      public List<SpotlightInfo> SpotlightInfos = new List<SpotlightInfo>();

      public Vector3 CameraEye;
      public Matrix ProjView;
      public Matrix ProjViewInv;
      public float Time;

      internal int HandleCount;

      public ISceneSnapshot AddReference() {
         var next = Interlocked.Increment(ref HandleCount);
         Trace.Assert(next >= 2); // can't addref something with 0 handle count;
         return this;
      }

      public ISceneSnapshot ReleaseReference() {
         var next = Interlocked.Decrement(ref HandleCount);
         Trace.Assert(next >= 0);

         if (next == 0) {
            Textures.Clear();
            Materials.Clear();
            RenderJobBatches.Clear();
            SpotlightInfos.Clear();

            Scene.ReturnSnapshot(this);
         }
         return this;
      }
   }

   [StructLayout(LayoutKind.Sequential, Pack = 1)]
   public struct SpotlightInfo {
      public Vector3 Origin;
      public Vector3 Direction;

      public Color4 Color;
      public float DistanceAttenuationConstant;
      public float DistanceAttenuationLinear;
      public float DistanceAttenuationQuadratic;
      public float SpotlightAttenuationPower;

      public Matrix ProjViewCM;

      public const int Size = 3 * 4 * 2 + 4 * 4 * 1 + 4 * 4 + 64 * 1;
   }

   [StructLayout(LayoutKind.Sequential, Pack = 4)]
   public struct MaterialProperties {
      public float Metallic;
      public float Roughness;

      public const int Size = 4 * 2;
   }

   [StructLayout(LayoutKind.Sequential, Pack = 4)]
   internal struct InternalMaterialResourcesDescription {
      public int BaseTextureIndex;
      public int padding0, padding1, padding2;
      public Color4 BaseColor;

      public const int Size = 4 * 4 + 4 * 4;

      public InternalMaterialResourcesDescription Resolve(int resolvedBaseTextureIndex) {
         return new InternalMaterialResourcesDescription {
            BaseTextureIndex = resolvedBaseTextureIndex,
            BaseColor = BaseColor
         };
      }
   }

   public struct MaterialResourcesDescription {
      public IShaderResourceView BaseTexture;
      public Color4 BaseColor;

      internal InternalMaterialResourcesDescription ToInternal(int baseTextureIndex) {
         return new InternalMaterialResourcesDescription {
            BaseTextureIndex = baseTextureIndex,
            BaseColor = Fallback<Color4>.Helper(BaseColor, Color4.White)
         };
      }

      public static class Fallback<T> where T : struct {
         private static readonly Func<T, T, T> func;

         static Fallback() {
            var method = new DynamicMethod("", typeof(T), new[] { typeof(T), typeof(T) }, true);
            var emitter = method.GetILGenerator();
            var defaultLocal = emitter.DeclareLocal(typeof(T));
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.Emit(OpCodes.Ldloca_S, defaultLocal);
            emitter.Emit(OpCodes.Initobj, typeof(T));
            emitter.Emit(OpCodes.Ldloc, defaultLocal);
            emitter.Emit(OpCodes.Call, typeof(T).GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static));

            // Jump to return fallback if arg was equal to default
            var labelReturnFallback = emitter.DefineLabel();
            emitter.Emit(OpCodes.Brtrue_S, labelReturnFallback);

            // Otherwise return val;
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.Emit(OpCodes.Ret);

            // arg was equal to default, ret fallback
            emitter.MarkLabel(labelReturnFallback);
            emitter.Emit(OpCodes.Ldarg_1);
            emitter.Emit(OpCodes.Ret);
            /**/

            func = (Func<T, T, T>)method.CreateDelegate(typeof(Func<T, T, T>));
         }

         // note: val.Equals(default(T)) ? fallback : val allocs!
         public static T Helper(T val, T fallback) => func(val, fallback);
      }

      public override bool Equals(object obj) {
         return obj is MaterialResourcesDescription o &&
                BaseTexture == o.BaseTexture &&
                BaseColor == o.BaseColor;
      }

      public override int GetHashCode() {
         var hashCode = -1531171848;
         hashCode = hashCode * -1521134295 + (BaseTexture?.GetHashCode() ?? 27);
         hashCode = hashCode * -1521134295 + BaseColor.GetHashCode();
         return hashCode;
      }
   }

   public struct MaterialDescription {
      public MaterialProperties Properties;
      public MaterialResourcesDescription Resources;
   }
   
   public struct RenderJobBatch {
      public IMesh Mesh;
      public Matrix BatchTransform;
      public ExposedArrayList<RenderJobDescription> Jobs;
      public int MaterialResourcesIndexOverride;
      public bool Wireframe;

      public static RenderJobBatch Create(IMesh mesh = null) {
         return new RenderJobBatch {
            BatchTransform = Matrix.Identity,
            Jobs = new ExposedArrayList<RenderJobDescription>(),
            Mesh = mesh,
            MaterialResourcesIndexOverride = -1
         };
      }
   }

   [StructLayout(LayoutKind.Sequential, Pack = 4)]
   public struct RenderJobDescription {
      public Matrix WorldTransform;
      public MaterialProperties MaterialProperties;
      public int MaterialResourcesIndex;
      public Color Color;

      public const int Size = 4 * 4 * 4 * 1 + MaterialProperties.Size + 4 + 4;

      public RenderJobDescription Resolve(int resolvedMaterialResourcesIndex) {
         return new RenderJobDescription {
            WorldTransform = WorldTransform,
            MaterialProperties = MaterialProperties,
            MaterialResourcesIndex = resolvedMaterialResourcesIndex,
            Color = Color
         };
      }
   }
}
