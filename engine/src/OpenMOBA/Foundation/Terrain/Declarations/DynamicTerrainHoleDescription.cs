using System;
using System.Collections.Generic;
using System.Numerics;
using OpenMOBA.DataStructures;
using OpenMOBA.Geometry;

namespace OpenMOBA.Foundation.Terrain.Declarations {
   /// <summary>
   /// Considered internal to TerrainService
   /// </summary>
   public class DynamicTerrainHoleDescription {
      private readonly TerrainService terrainService;

      internal DynamicTerrainHoleDescription(TerrainService terrainService, IHoleStaticMetadata staticMetadata) {
         this.terrainService = terrainService;
         this.StaticMetadata = staticMetadata;

         RecomputeWorldAABB();
      }

      // Internals touched by terrain service
      internal int Version;

      public IHoleStaticMetadata StaticMetadata;
      internal HoleInstanceMetadata InstanceMetadata = new HoleInstanceMetadata();

      // Publics accessible by game logic
      public Matrix4x4 WorldTransform
      {
         get => InstanceMetadata.WorldTransform;
         set
         {
            if (InstanceMetadata.WorldTransform != value) {
               InstanceMetadata.WorldTransform = value;
               var inverted = Matrix4x4.Invert(WorldTransform, out InstanceMetadata.WorldTransformInv);
               if (!inverted) {
                  throw new InvalidOperationException("Unable to invert transformation matrix!?");
               }

               RecomputeWorldAABB();
               Version++;
            }
         }
      }

      private void RecomputeWorldAABB() {
         InstanceMetadata.WorldAABB = StaticMetadata.ComputeWorldAABB(InstanceMetadata.WorldTransform);
      }

      public Matrix4x4 WorldTransformInv => InstanceMetadata.WorldTransformInv;

      public bool EnableDebugHighlight { get; set; }

      public AxisAlignedBoundingBox3 WorldBounds => InstanceMetadata.WorldAABB;

      public void EnhanceLocalGeometryJob(SectorNodeDescription sectorNodeDescription, ref LocalGeometryJob localGeometryRenderJob) {
         if (!WorldBounds.Intersects(sectorNodeDescription.WorldBounds)) {
            return;
         }

         IReadOnlyList<Polygon2> projectedHoleIncludedContours, projectedHoleExcludedContours;
         if (!StaticMetadata.TryProjectOnto(InstanceMetadata, sectorNodeDescription, out projectedHoleIncludedContours, out projectedHoleExcludedContours)) {
            return;
         }

         localGeometryRenderJob.DynamicHoles[(this, Version)] = (projectedHoleIncludedContours, projectedHoleExcludedContours);
      }
   }
}