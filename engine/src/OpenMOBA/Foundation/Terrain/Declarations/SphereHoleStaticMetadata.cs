using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using OpenMOBA.DataStructures;
using OpenMOBA.Geometry;

#if use_fixed
using cDouble = FixMath.NET.Fix64;
#else
using cDouble = System.Double;
#endif

namespace OpenMOBA.Foundation.Terrain.Declarations {
   public class SphereHoleStaticMetadata : IHoleStaticMetadata {
      public cDouble Radius;

      public bool TryProjectOnto(HoleInstanceMetadata instanceMetadata, SectorNodeDescription sectorNodeDescription, out IReadOnlyList<Polygon2> projectedHoleIncludedContours, out IReadOnlyList<Polygon2> projectedHoleExcludedContours) {
         Trace.Assert(Matrix4x4.Decompose(instanceMetadata.WorldTransform, out var scale, out var rotation, out var translation));
         Trace.Assert(Math.Abs(scale.X - scale.Y) < 1E-9 && Math.Abs(scale.X - scale.Z) < 1E-9);
         // TODO: Divergence
         var effectiveRadiusWorld = (cDouble)scale.X * Radius;
         var normalSectorWorld = Vector3.TransformNormal(new Vector3(0, 0, 1), sectorNodeDescription.WorldTransform).ToOpenMobaVector();
         var normalSectorWorldUnit = normalSectorWorld / normalSectorWorld.Norm2D();
         var sectorOriginWorld = Vector3.Transform(new Vector3(0, 0, 0), sectorNodeDescription.WorldTransform).ToOpenMobaVector();
         var distanceWorld = CDoubleMath.Abs((sectorOriginWorld - translation.ToOpenMobaVector()).Dot(normalSectorWorldUnit));
         var c2MinusA2 = effectiveRadiusWorld * effectiveRadiusWorld - distanceWorld * distanceWorld;
         if (c2MinusA2 <= CDoubleMath.c0) {
            projectedHoleIncludedContours = null;
            projectedHoleExcludedContours = null;
            return false;
         }

         var effectiveRadiusWorldOnSectorPlane = CDoubleMath.Sqrt(c2MinusA2);

         // TODO: Divergence
         var effectiveRadiusSector = Vector3.TransformNormal(
            new Vector3(0, 0, (float)effectiveRadiusWorldOnSectorPlane),
            sectorNodeDescription.InstanceMetadata.WorldTransformInv
         ).Length();

         //         if (effectiveRadiusSector < 200) {
         //            projectedHoleIncludedContours = null;
         //            projectedHoleExcludedContours = null;
         //            return false;
         //         } else {
         //            projectedHoleIncludedContours = new List<Polygon2> {
         //               Polygon2.CreateCircle(0, 0, 20)
         //            };
         //            projectedHoleExcludedContours = new List<Polygon2>();
         //            return true;
         //         }

         var centerSector = Vector3.Transform(
            translation,
            sectorNodeDescription.InstanceMetadata.WorldTransformInv
         );

         projectedHoleIncludedContours = new List<Polygon2> {
            Polygon2.CreateCircle((int)centerSector.X, (int)centerSector.Y, (int)effectiveRadiusSector)
         };
         projectedHoleExcludedContours = new List<Polygon2>();
         return true;
      }

      public AxisAlignedBoundingBox3 ComputeWorldAABB(Matrix4x4 worldTransform) {
         // todo: can we do this more efficiently?
         var rad = (float)Radius;
         var nrad = -rad;
         var corners = new[] {
            new Vector3(-rad, -rad, -rad),
            new Vector3(-rad, -rad,  rad),
            new Vector3(-rad,  rad, -rad),
            new Vector3(-rad,  rad,  rad),
            new Vector3( rad, -rad, -rad),
            new Vector3( rad, -rad,  rad),
            new Vector3( rad,  rad, -rad),
            new Vector3( rad,  rad,  rad)
         };
         // TODO: Divergence
         return AxisAlignedBoundingBox3.BoundingPoints(corners.Map(p => Vector3.Transform(p, worldTransform).ToOpenMobaVector()));
      }

      public bool ContainsPoint(HoleInstanceMetadata instanceMetadata, DoubleVector3 pointWorld, cDouble agentRadius) {
         // TODO: Divergence
         var pointLocal = Vector3.Transform(pointWorld.ToDotNetVector(), instanceMetadata.WorldTransformInv);
         var mag = (cDouble)pointLocal.Length();
         return mag <= Radius + agentRadius;
      }
   }
}