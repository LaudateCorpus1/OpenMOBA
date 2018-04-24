﻿using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Canvas3D.LowLevel;
using SharpDX;
using SharpDX.DirectInput;
using Color = SharpDX.Color;
using Point = System.Drawing.Point;

namespace Canvas3D {
   internal static class Program {
      private static Vector3 cameraTarget = new Vector3(0, 0.5f, 0);
      private static Vector3 cameraOffset = new Vector3(3, 2.5f, 5) - cameraTarget;
      private static Vector3 cameraUp = new Vector3(0, 1, 0);
      private static Matrix view = MatrixCM.ViewLookAtRH(cameraTarget + cameraOffset, cameraTarget, cameraUp);
      private static Matrix projView;

      private const int NUM_LAYERS = 100;
      private const int CUBES_PER_LAYER = 10;
      private static readonly Matrix[] cubeDefaultTransforms = (
         from layer in Enumerable.Range(0, NUM_LAYERS)
         from i in Enumerable.Range(0, CUBES_PER_LAYER)
         select
         MatrixCM.RotationY(
            2 * (float)Math.PI * i / CUBES_PER_LAYER + (layer + 1) * i + (layer + 1)
         ) *
         MatrixCM.Translation(
            0.8f + 0.02f * layer,
            0.5f + 0.3f * (float)Math.Sin((8 + 7 * layer / (float)NUM_LAYERS) * 2 * Math.PI * (i + 1) / CUBES_PER_LAYER),
            0) *
         MatrixCM.Scaling(0.5f / (float)Math.Sqrt(NUM_LAYERS)) *
         MatrixCM.RotationY(i)).ToArray();   

      public static void Main(string[] args) {
         var graphicsLoop = GraphicsLoop.CreateWithNewWindow(1280, 720, InitFlags.DisableVerticalSync | InitFlags.EnableDebugStats);
         graphicsLoop.Form.Resize += (s, e) => UpdateProjViewMatrix(graphicsLoop.Form.ClientSize);
         
         graphicsLoop.Form.MouseWheel += (s, e) => {
            var dir = cameraOffset;
            dir.Normalize();
            cameraOffset += dir * (-e.Delta / 1000.0f);
            Console.WriteLine(e.Delta + " " + dir + " " + cameraOffset);
            view = MatrixCM.ViewLookAtRH(cameraTarget + cameraOffset, cameraTarget, new Vector3(0, 1, 0));
            UpdateProjViewMatrix(graphicsLoop.Form.ClientSize);
         };

         UpdateProjViewMatrix(graphicsLoop.Form.ClientSize);
         
         var floatingCubesBatch = RenderJobBatch.Create(graphicsLoop.Presets.GetPresetMesh(MeshPreset.UnitCube));
         floatingCubesBatch.Wireframe = true;
         foreach (var transform in cubeDefaultTransforms) {
            floatingCubesBatch.Jobs.Add(new RenderJobDescription {
               WorldTransform = transform,
               MaterialProperties = { Metallic = 0.0f, Roughness = 0.0f },
               MaterialResourcesIndex = -1,
               Color = Color.White
            });
         }

         var scene = new Scene();
         for (var frame = 0; graphicsLoop.IsRunning(out var renderer, out var input); frame++) {
            var t = (float)graphicsLoop.Statistics.FrameTime.TotalSeconds;

            var right = Vector3.Cross(-cameraOffset, cameraUp);
            right.Normalize();

            var forward = -cameraOffset;
            forward.Normalize();

            var up = Vector3.Cross(right, forward);

            if (input.IsMouseDown(MouseButtons.Left)) {
               var rotation = Matrix.RotationY(-input.DeltaX * 0.005f) * Matrix.RotationAxis(right, -input.DeltaY * 0.005f);
               cameraOffset = (Vector3)Vector3.Transform(cameraOffset, rotation);
            }

            if (input.IsKeyDown(Keys.Left)) {
               cameraTarget -= right * 0.005f;
            }
            if (input.IsKeyDown(Keys.Right)) {
               cameraTarget += right * 0.005f;
            }
            if (input.IsKeyDown(Keys.Up)) {
               cameraTarget += forward * 0.005f;
            }
            if (input.IsKeyDown(Keys.Down)) {
               cameraTarget -= forward * 0.005f;
            }
            if (input.IsKeyDown(Keys.Space)) {
               cameraTarget += cameraUp * 0.005f * (input.IsKeyDown(Keys.ShiftKey) ? -1 : 1);
            }

            view = MatrixCM.ViewLookAtRH(cameraTarget + cameraOffset, cameraTarget, new Vector3(0, 1, 0));
            UpdateProjViewMatrix(graphicsLoop.Form.ClientSize);

            scene.Clear();
            scene.SetCamera(cameraTarget + cameraOffset, projView);

            if (input.IsKeyDown(Keys.Left)) {
               Console.WriteLine("L");
            }

            // Draw floor
            scene.AddRenderable(
               graphicsLoop.Presets.UnitCube,
               MatrixCM.Scaling(4f, 0.1f, 4f) * MatrixCM.Translation(0, -0.5f, 0) * MatrixCM.RotationX((float)Math.PI),
               new MaterialDescription { Properties = { Metallic = 0.0f, Roughness = 0.04f } },
               Color.White);

            // Draw center cube / sphere
            scene.AddRenderable(
               false ? graphicsLoop.Presets.UnitCube : graphicsLoop.Presets.UnitSphere,
               MatrixCM.Translation(0, 0.5f, 0),
               new MaterialDescription { Properties = { Metallic = 0.0f, Roughness = 0.04f } },
               Color.White);

            // Draw floating cubes circling around center cube
            floatingCubesBatch.BatchTransform = MatrixCM.RotationY(t * (float)Math.PI / 10.0f);
            floatingCubesBatch.MaterialResourcesIndexOverride = scene.AddMaterialResources(new MaterialResourcesDescription {
               BaseTexture = graphicsLoop.Presets.SolidCubeTextures[Color.Red, Color.Cyan, Color.Lime, Color.Magenta, Color.Blue, Color.Yellow]
            });
            if (false) {
               floatingCubesBatch.MaterialResourcesIndexOverride = -1;
               for (var i = 0; i < floatingCubesBatch.Jobs.Count; i++) {
                  floatingCubesBatch.Jobs.store[i].MaterialResourcesIndex = scene.AddMaterialResources(new MaterialResourcesDescription {
                     BaseTexture = graphicsLoop.Presets.SolidCubeTextures[new Color4(i / (float)(floatingCubesBatch.Jobs.Count - 1), 0, 0, 1)]
                  });
               }
            }
            scene.AddRenderJobBatch(floatingCubesBatch);

            // Add spotlights
            scene.AddSpotlight(
               new Vector3(5, 4, 3), new Vector3(0, 0, 0), Vector3.Up, (float)Math.PI / 8.0f,
               Color.White, 100.0f,
               0.0f, 6.0f, 3.0f,
               0.5f / 256.0f);
            scene.AddSpotlight(new Vector3(5, 4, -5), new Vector3(0, 0, 0), Vector3.Up, (float)Math.PI / 8.0f, Color.White, 0.1f, 100.0f, 3.0f, 6.0f, 1.0f);

            // Draw the scene
            var snapshot = scene.ExportSnapshot();
            renderer.RenderScene(snapshot);
            snapshot.ReleaseReference();
         }
      }

      private static bool zfirst = true;
      private static void UpdateProjViewMatrix(Size clientSize) {
         var verticalFov = (float)Math.PI / 4;
         var aspect = clientSize.Width / (float)clientSize.Height;
         var proj = MatrixCM.PerspectiveFovRH(verticalFov, aspect, 1.0f, 100.0f);
         projView = proj * view;

         if (zfirst) {
            zfirst = false;
            var lookat = new Vector3(0, 0.5f, 0);
            //var pos = new Vector4(lookat + 0.1f * (cameraEye - lookat), 1);//new Vector4(1, 2, 3, 1);
            var pos = new Vector4(0.5f, 1.0f, 0.5f, 1);
            var projViewRm = projView;
            projViewRm.Transpose();
            var homogeneous = Vector4.Transform(pos, projViewRm);
            Console.WriteLine("pos: " + pos);
            Console.WriteLine("posh: " + homogeneous);
            homogeneous /= homogeneous.W;
            Console.WriteLine("poshn: " + homogeneous);
            var projViewRmInv = projViewRm;
            projViewRmInv.Invert();
            Console.WriteLine();
            Console.WriteLine(projViewRmInv * projViewRm);
            Console.WriteLine();
            Console.WriteLine(projViewRm * projViewRmInv);
            Console.WriteLine();
            var x = Vector4.Transform(homogeneous, projViewRmInv);
            Console.WriteLine(x);
            x /= x.W;
            Console.WriteLine(x);
         }
      }
   }
}
