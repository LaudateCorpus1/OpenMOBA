﻿using ClipperLib;
using OpenMOBA.Foundation.Terrain;
using OpenMOBA.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenMOBA.Foundation.Terrain.Declarations;

#if use_fixed
using cDouble = FixMath.NET.Fix64;
#else
using cDouble = System.Double;
#endif

namespace OpenMOBA.Foundation {
   public static class MapLoader {
      public static void LoadMeshAsMap(this TerrainService terrainService, string objPath, DoubleVector3 meshOffset, DoubleVector3 worldOffset, int scaling = 50000) {
         Environment.CurrentDirectory = @"V:\my-repositories\miyu\derp\OpenMOBA.DevTool\bin\Debug\net461";

         var lines = File.ReadLines(objPath);
         var verts = new List<DoubleVector3>();
         var previousEdges = new Dictionary<(int, int), (SectorNodeDescription, IntLineSegment2)>();

         void Herp(SectorNodeDescription node, int a, int b, IntLineSegment2 seg) {
            if (a > b) {
               (a, b) = (b, a); // a < b
               seg = new IntLineSegment2(seg.Second, seg.First);
            }

            if (previousEdges.TryGetValue((a, b), out var prev)) {
               var (prevNode, prevSeg) = prev;
               throw new NotImplementedException("Need clockness");
               //terrainService.AddSectorEdgeDescription(PortalSectorEdgeDescription.Build(node, prevNode, seg, prevSeg));
               //terrainService.AddSectorEdgeDescription(PortalSectorEdgeDescription.Build(prevNode, node, prevSeg, seg));
            } else {
               previousEdges.Add((a, b), (node, seg));
            }
         }

         foreach (var (i, line) in lines.Select(l => l.Trim()).Enumerate()) {
            if (line.Length == 0) continue;
            if (line.StartsWith("#")) continue;
            var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0]) {
               case "v":
                  // TODO: Determinism
                  var v = meshOffset + new DoubleVector3((cDouble)double.Parse(tokens[1]), (cDouble)double.Parse(tokens[2]), (cDouble)double.Parse(tokens[3]));
                  v = new DoubleVector3(v.X, -v.Z, v.Y);
                  v = v * scaling + worldOffset;
                  // todo: flags for dragon / bunny to switch handiness + rotate
                  verts.Add(v);
                  //                  verts.Add(new DoubleVector3(v.X, v.Y, v.Z));
                  break;
               case "f":
                  //                  Console.WriteLine($"Loading face of line {i}");
                  var i1 = int.Parse(tokens[1]) - 1;
                  var i2 = int.Parse(tokens[2]) - 1;
                  var i3 = int.Parse(tokens[3]) - 1;

                  var v1 = verts[i1]; // origin
                  var v2 = verts[i2]; // a, x dim
                  var v3 = verts[i3]; // b, y dim

                  /***
                   *            ___      
                   *    /'.      |     ^
                   * b /.t '.    | h   | vert
                   *  /__'___'. _|_    |
                   *     a
                   *  |-------| w
                   *  |---| m
                   *  
                   *          ___      
                   *  \.       |     ^
                   * b \'.     | h   | vert
                   *    \_'.  _|_    |
                   *    |--| w
                   *  |-| m
                   */
                  var a = v2 - v1;
                  var b = v3 - v1;
                  var theta = CDoubleMath.Acos(a.Dot(b) / (a.Norm2D() * b.Norm2D())); // a.b =|a||b|cos(theta)

                  var w = a.Norm2D();
                  var h = b.Norm2D() * CDoubleMath.Sin(theta);
                  var m = b.Norm2D() * CDoubleMath.Cos(theta);

                  var scaleBound = (cDouble)1000; //ClipperBase.loRange
                  var localUpscale = scaleBound * (cDouble)0.9f / CDoubleMath.Max(CDoubleMath.Abs(m), CDoubleMath.Max(CDoubleMath.Abs(h), w));
                  var globalDownscale = CDoubleMath.c1 / localUpscale;
                  // Console.WriteLine(localUpscale + " " + (int)(m * localUpscale) + " " + (int)(h * localUpscale) + " " + (int)(w * localUpscale));

                  var po = new IntVector2(0, 0);
                  var pa = new IntVector2((int)(w * localUpscale), 0);
                  var pb = new IntVector2((int)(m * localUpscale), (int)(h * localUpscale));
                  var metadata = new TerrainStaticMetadata {
                     LocalBoundary = m < CDoubleMath.c0 ? new Rectangle((int)(m * localUpscale), 0, (int)((w - m) * localUpscale), (int)(h * localUpscale)) : new Rectangle(0, 0, (int)(w * localUpscale), (int)(h * localUpscale)),
                     LocalIncludedContours = new List<Polygon2> {
                        new Polygon2(new List<IntVector2> { po, pb, pa, po })
                     },
                     LocalExcludedContours = new List<Polygon2>()
                  };

                  foreach (var zzz in metadata.LocalIncludedContours) {
                     foreach (var p in zzz.Points) {
                        if (Math.Abs(p.X) >= ClipperBase.loRange || Math.Abs(p.Y) >= ClipperBase.loRange) {
                           throw new Exception("!!!!");
                        }
                     }
                  }

                  var snd = terrainService.CreateSectorNodeDescription(metadata);
                  var triangleToWorld = Matrix4x4.Identity;

                  var alen = (cDouble)a.Norm2D();
                  triangleToWorld.M11 = (float)(globalDownscale * (cDouble)a.X / alen); // TODO: Determinism
                  triangleToWorld.M12 = (float)(globalDownscale * (cDouble)a.Y / alen);
                  triangleToWorld.M13 = (float)(globalDownscale * (cDouble)a.Z / alen);
                  triangleToWorld.M14 = 0.0f;

                  var n = a.Cross(b).ToUnit();
                  var vert = n.Cross(a).ToUnit();
                  //                  var blen = (float)b.Norm2D();
                  triangleToWorld.M21 = (float)(globalDownscale * (cDouble)vert.X); // TODO: Determinism
                  triangleToWorld.M22 = (float)(globalDownscale * (cDouble)vert.Y);
                  triangleToWorld.M23 = (float)(globalDownscale * (cDouble)vert.Z);
                  triangleToWorld.M24 = 0.0f;

                  triangleToWorld.M31 = (float)(globalDownscale * (cDouble)n.X); // TODO: Determinism
                  triangleToWorld.M32 = (float)(globalDownscale * (cDouble)n.Y);
                  triangleToWorld.M33 = (float)(globalDownscale * (cDouble)n.Z);
                  triangleToWorld.M34 = 0.0f;

                  triangleToWorld.M41 = (float)v1.X;
                  triangleToWorld.M42 = (float)v1.Y;
                  triangleToWorld.M43 = (float)v1.Z;
                  triangleToWorld.M44 = 1.0f;

                  snd.WorldTransform = triangleToWorld;
                  snd.WorldToLocalScalingFactor = localUpscale;
                  terrainService.AddSectorNodeDescription(snd);

                  // var store = new SectorGraphDescriptionStore();
                  // var ts = new TerrainService(store, new TerrainSnapshotCompiler(store));
                  // ts.AddSectorNodeDescription(snd);
                  // ts.AddSectorEdgeDescription(PortalSectorEdgeDescription.Build(snd, snd, new IntLineSegment2(po, pa), new IntLineSegment2(po, pa)));
                  // ts.AddSectorEdgeDescription(PortalSectorEdgeDescription.Build(snd, snd, new IntLineSegment2(pa, pb), new IntLineSegment2(pa, pb)));
                  // ts.AddSectorEdgeDescription(PortalSectorEdgeDescription.Build(snd, snd, new IntLineSegment2(pb, po), new IntLineSegment2(pb, po)));
                  // ts.CompileSnapshot().OverlayNetworkManager.CompileTerrainOverlayNetwork(0.0);

                  Herp(snd, i1, i2, new IntLineSegment2(po, pa));
                  Herp(snd, i2, i3, new IntLineSegment2(pa, pb));
                  Herp(snd, i3, i1, new IntLineSegment2(pb, po));
                  break;
            }
         }

         var lowerbound = verts.Aggregate(new DoubleVector3(cDouble.MaxValue, cDouble.MaxValue, cDouble.MaxValue), (a, b) => new DoubleVector3(CDoubleMath.Min(a.X, b.X), CDoubleMath.Min(a.Y, b.Y), CDoubleMath.Min(a.Z, b.Z)));
         var upperbound = verts.Aggregate(new DoubleVector3(cDouble.MinValue, cDouble.MinValue, cDouble.MinValue), (a, b) => new DoubleVector3(CDoubleMath.Max(a.X, b.X), CDoubleMath.Max(a.Y, b.Y), CDoubleMath.Max(a.Z, b.Z)));
         // Console.WriteLine("Loaded map bounds: " + lowerbound + " " + upperbound + " " + (upperbound + lowerbound) / 2 + " " + (upperbound - lowerbound));
      }
   }
}
