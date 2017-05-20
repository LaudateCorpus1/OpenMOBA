﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using OpenMOBA.DevTool.Debugging.Canvas3D;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Windows;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = SharpDX.Color;

namespace Shade {
   public interface ITechnique {
      int Passes { get; set; }
      void BeginPass(IRenderContext renderContext, int pass);
   }

   public interface ITechniqueCollection {
      ITechnique DefaultPositionedColored { get; }
   }
   public class DefaultTechniqueCollectionImpl : ITechniqueCollection {
      public ITechnique DefaultPositionedColored { get; private set; }

      public static DefaultTechniqueCollectionImpl Create(IAssetManager assetManager) {
         var defaultPixelShader = assetManager.LoadPixelShaderFromFile("shaders/default", "PSMain");
         var defaultVertexShader = assetManager.LoadVertexShaderFromFile("shaders/default", "VSMain");
         return new DefaultTechniqueCollectionImpl {
            DefaultPositionedColored = new Technique {
               PixelShader = defaultPixelShader,
               VertexShader = defaultVertexShader
            }
         };
      }

      private class Technique : ITechnique {
         public IPixelShader PixelShader { get; set; }
         public IVertexShader VertexShader { get; set; }

         public int Passes { get; set; }

         public void BeginPass(IRenderContext renderContext, int pass) {
            renderContext.SetPixelShader(PixelShader);
            renderContext.SetVertexShader(VertexShader);
         }
      }
   }

   public static class CanvasProgram {
      public static void EntryPoint(string[] args) {
         var renderForm = new RenderForm { ClientSize = new Size(1280, 720) };
         var graphicsDevice = Direct3DGraphicsDevice.Create(renderForm);
         var techniqueCollection = (ITechniqueCollection)DefaultTechniqueCollectionImpl.Create(graphicsDevice.AssetManager);

         var cubeVertices = new[] {
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Red), // Front
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, -1.0f), Color.Red),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, -1.0f), Color.Red),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Red),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, -1.0f), Color.Red),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, -1.0f), Color.Red),

            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 1.0f), Color.Lime), // BACK
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Lime),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 1.0f), Color.Lime),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 1.0f), Color.Lime),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 1.0f), Color.Lime),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Lime),

            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, -1.0f), Color.Blue), // Top
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 1.0f), Color.Blue),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Blue),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, -1.0f), Color.Blue),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Blue),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, -1.0f), Color.Blue),

            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Yellow), // Bottom
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 1.0f), Color.Yellow),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 1.0f), Color.Yellow),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Yellow),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, -1.0f), Color.Yellow),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 1.0f), Color.Yellow),

            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Magenta), // Left
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 1.0f), Color.Magenta),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 1.0f), Color.Magenta),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, -1.0f), Color.Magenta),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 1.0f), Color.Magenta),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, -1.0f), Color.Magenta),

            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, -1.0f), Color.Cyan), // Right
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Cyan),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 1.0f), Color.Cyan),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, -1.0f), Color.Cyan),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, -1.0f), Color.Cyan),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 1.0f), Color.Cyan),
         };

         // Switch cube from LHS to RHS coordinate system. Flip clockness of verts, 
         for (var i = 0; i < cubeVertices.Length; i += 3) {
            // // Flip vert clockness
            // var temp = cubeVertices[i + 1];
            // cubeVertices[i + 1] = cubeVertices[i + 2];
            // cubeVertices[i + 2] = temp;

            // Flip Zs, which effectively flips clockness. Important because we have
            // colored left/right which will be flipped if we just flip clockness as above.
            cubeVertices[i].Position.Z *= -1.0f;
            cubeVertices[i + 1].Position.Z *= -1.0f;
            cubeVertices[i + 2].Position.Z *= -1.0f;
            
            // cubeVertices[i].Color = Color.White;
            // cubeVertices[i + 1].Color = Color.White;
            // cubeVertices[i + 2].Color = Color.White;
         }

         var cubeBuffer = Buffer.Create(graphicsDevice.InternalD3DDevice, BindFlags.VertexBuffer, cubeVertices);

         var planeXYBuffer = Buffer.Create(graphicsDevice.InternalD3DDevice, BindFlags.VertexBuffer, new[] {
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 0.0f), Color.White), // Faces Z-
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 0.0f), Color.White),

            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, 1.0f, 0.0f), Color.White), // Faces Z+
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(-1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, -1.0f, 0.0f), Color.White),
            new Direct3DVertexPositionColor(0.5f * new Vector3(1.0f, 1.0f, 0.0f), Color.White),
         });
         var contantBuffer = new Buffer(graphicsDevice.InternalD3DDevice, Utilities.SizeOf<Matrix>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0);

         renderForm.Show();
         var start = DateTime.Now;
         using (var renderLoop = new RenderLoop(renderForm)) {
            while (renderLoop.NextFrame() || true) {
               graphicsDevice.DoEvents();

               var context = graphicsDevice.RenderContext;
               context.ClearTargetAndDepthBuffers(Color.Black);

               techniqueCollection.DefaultPositionedColored.BeginPass(context, 0);
               //               graphicsDevice.InternalD3DDevice.

               var time = (float)(DateTime.Now - start).TotalSeconds;
               var position = new Vector3(0, 2, 5);

               var lookat = 0 * new Vector3(1, 1, 1) / 2;
               var up = new Vector3(0, 1, 0);
               // var view = Matrix.LookAtRH(position, lookat, up);
               var view = Matrix.LookAtRH(Vector3.Transform(position, Matrix3x3.RotationY(time)), lookat, up);
               view.Transpose();
               var proj = Matrix.PerspectiveFovRH((float)Math.PI / 4.0f, 1280.0f / 720.0f, 0.1f, 100.0f);
               proj.Transpose();

               graphicsDevice.InternalD3DDevice.ImmediateContext.VertexShader.SetConstantBuffer(0, contantBuffer);
               graphicsDevice.InternalD3DDevice.ImmediateContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

               var cubeWorld = Matrix.Identity;
               var cubeProjViewWorld = proj * view; // * cubeWorld;
               graphicsDevice.InternalD3DDevice.ImmediateContext.InputAssembler.SetVertexBuffers(
                  0, new VertexBufferBinding(cubeBuffer, Direct3DVertexPositionColor.Size, 0));
               graphicsDevice.InternalD3DDevice.ImmediateContext.UpdateSubresource(ref cubeProjViewWorld, contantBuffer, 0);
               graphicsDevice.InternalD3DDevice.ImmediateContext.Draw(36, 0);

               var origin = new Vector3(0, 0, 0);
               var x = new Vector4(origin, 1.0f);
               var pvw = cubeProjViewWorld;
//               pvw.Transpose();
               var transformed = Vector4.Transform(x, pvw);
               transformed /= transformed.W;
//               Console.WriteLine(transformed);
//               var y = * cubeProjViewWorld;
//               cubeProjViewWorld

//               var planeWorld = Matrix.Scaling(2) * Matrix.Translation(0, 0, -1);
//               var planeWorldViewProj = proj * view * planeWorld;
//               graphicsDevice.InternalD3DDevice.ImmediateContext.UpdateSubresource(ref planeWorldViewProj, contantBuffer, 0);
//               graphicsDevice.InternalD3DDevice.ImmediateContext.InputAssembler.SetVertexBuffers(
//                  0, new VertexBufferBinding(planeXYBuffer, Direct3DVertexPositionColor.Size, 0));
//               graphicsDevice.InternalD3DDevice.ImmediateContext.Draw(12, 0);

               context.Present();
            }
         }
      }
   }

//   public class CanvasProgramImpl : CanvasProgram {
//      private const double kScale = 30;
//      private List<DelaunayTriangle> triangulation;
//
//      public override void Setup() {
//         base.Setup();
//
//         var land = new List<List<TriangulationPoint>> {
//            GeometryFactory.Rectangle(1, 1, 8, 8),
//            GeometryFactory.Rectangle(9, 4, 2, 2),
//            GeometryFactory.Rectangle(11, 1, 8, 8),
//            GeometryFactory.Rectangle(14, 9, 2, 2),
//            GeometryFactory.Rectangle(11, 11, 8, 8)
//         };
//         var holes = new List<List<TriangulationPoint>> {
//            GeometryFactory.Rectangle(3, 3, 4, 4),
//            GeometryFactory.Rectangle(12.6, 2.6, 1.6, 1.6),
//            GeometryFactory.Rectangle(15.8, 2.6, 1.6, 1.6),
//            GeometryFactory.Rectangle(12.6, 5.8, 1.6, 1.6),
//            GeometryFactory.Rectangle(15.8, 5.8, 1.6, 1.6),
//            GeometryFactory.Rectangle(11, 11, 1.6, 1.6),
//            GeometryFactory.Rectangle(17.4, 11, 1.6, 1.6),
//            GeometryFactory.Rectangle(17.4, 17.4, 1.6, 1.6),
//            GeometryFactory.Rectangle(11, 17.4, 1.6, 1.6),
//            GeometryFactory.Rectangle(13, 14, 4, 2),
//            GeometryFactory.Rectangle(14, 13, 2, 4)
//         };
//         var blockers = new List<List<TriangulationPoint>> {
////            GeometryFactory.Rectangle(9, 4, 6, 1)
//         };
//
//         triangulation = new Triangulator().TriangulateNavigationMesh(land, holes, blockers, 0);
//      }
//
//      public override void Render(GameTime gameTime) {
//         base.Render(gameTime);
//
//         Canvas.Clear(Color.FromBgra(0xFF000000 + 0x010101 * 10));
//         
//         foreach (var triangle in triangulation) {
//            Canvas.BeginPath();
//            Canvas.SetLineStyle(1, Color.Cyan);
//            Canvas.MoveTo(triangle.Points[0].X * kScale, triangle.Points[0].Y * kScale);
//            Canvas.LineTo(triangle.Points[1].X * kScale, triangle.Points[1].Y * kScale);
//            Canvas.LineTo(triangle.Points[2].X * kScale, triangle.Points[2].Y * kScale);
//            Canvas.LineTo(triangle.Points[0].X * kScale, triangle.Points[0].Y * kScale);
//            Canvas.FillPath(Color.White);
//            Canvas.Stroke();
//
//            Canvas.BeginPath();
//            Canvas.SetLineStyle(1, Color.Red);
//            foreach (var neighbor in triangle.Neighbors.Where(n => n != null)) {
//               if (neighbor.IsInterior) {
//                  Canvas.MoveTo(triangle.Centroid().X * kScale, triangle.Centroid().Y * kScale);
//                  Canvas.LineTo(neighbor.Centroid().X * kScale, neighbor.Centroid().Y * kScale);
//               }
//            }
//            Canvas.Stroke();
//         }
//      }
//   }

//   public class DynamicElement : Canvas {
//      public DynamicElement(int width, int height) : base(width, height) {
//
//      }
//   }

//   public class TriangulationElement : DynamicElement {
//      private readonly List<DelaunayTriangle> triangles;
//
//      public TriangulationElement(List<DelaunayTriangle> triangles) {
//         Step += HandleStep;
//      }
//
//      private void HandleStep() {
//         foreach (var triangle in triangles) {
//            var points = triangle.Points;
//            BeginPath();
//            SetLineStyle(1, 0x00FF00);
//            MoveTo(points[0].X, points[0].Y);
//            LineTo(points[1].X, points[1].Y);
//            LineTo(points[2].X, points[2].Y);
//            LineTo(points[0].X, points[0].Y);
//            Stroke();
//         }
//      }
//   }

//   public class GeometryLayer2D {
//
//   }
}
