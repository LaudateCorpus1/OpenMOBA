﻿using System;
using System.Collections.Generic;
using OpenMOBA.DataStructures;
using OpenMOBA.Foundation.Terrain.CompilationResults.Local;
using OpenMOBA.Foundation.Terrain.CompilationResults.Overlay;
using OpenMOBA.Geometry;

#if use_fixed
using cDouble = FixMath.NET.Fix64;
#else
using cDouble = System.Double;
#endif

namespace OpenMOBA.Foundation {
   public class PathfinderResultContext {
      public readonly (TerrainOverlayNetworkNode, IntVector2) Source;
      public readonly (TerrainOverlayNetworkNode, IntVector2)[] Destinations;
      internal readonly Dictionary<(TerrainOverlayNetworkNode, int), (TerrainOverlayNetworkNode, int, TerrainOverlayNetworkEdge, cDouble)> Predecessors;
      internal readonly ExposedArrayList<PathLink> SourceOptimalLinkToCrossovers;
      internal readonly ExposedArrayList<PathLink>[] DestinationOptimalLinkToCrossoversByDestinationIndex;

      private readonly (bool computed, MotionRoadmap roadmap)[] roadmapCache;

      public PathfinderResultContext((TerrainOverlayNetworkNode, IntVector2) source) {
         Source = source;
         Destinations = null;
         Predecessors = new Dictionary<(TerrainOverlayNetworkNode, int), (TerrainOverlayNetworkNode, int, TerrainOverlayNetworkEdge, cDouble)>();
         SourceOptimalLinkToCrossovers = null;
         DestinationOptimalLinkToCrossoversByDestinationIndex = null;
         roadmapCache = null;
      }

      public PathfinderResultContext((TerrainOverlayNetworkNode, IntVector2) source, (TerrainOverlayNetworkNode, IntVector2)[] destinations, Dictionary<(TerrainOverlayNetworkNode, int), (TerrainOverlayNetworkNode, int, TerrainOverlayNetworkEdge, cDouble)> predecessors, ExposedArrayList<PathLink> sourceOptimalLinkToCrossovers, ExposedArrayList<PathLink>[] destinationOptimalLinkToCrossoversByDestinationIndex) {
         Source = source;
         Destinations = destinations;
         Predecessors = predecessors;
         SourceOptimalLinkToCrossovers = sourceOptimalLinkToCrossovers;
         DestinationOptimalLinkToCrossoversByDestinationIndex = destinationOptimalLinkToCrossoversByDestinationIndex;

         roadmapCache = new (bool, MotionRoadmap)[destinations.Length];
      }

      public bool TryComputeRoadmap(int destinationIndex, out MotionRoadmap roadmap) {
         if (!roadmapCache[destinationIndex].computed) {
            roadmapCache[destinationIndex].computed = true;
            var success = PathfinderCalculator.TryBacktrack(
               Source.Item1, Source.Item2,
               Destinations[destinationIndex].Item1, Destinations[destinationIndex].Item2,
               Predecessors,
               -destinationIndex - 1,
               SourceOptimalLinkToCrossovers,
               DestinationOptimalLinkToCrossoversByDestinationIndex[destinationIndex],
               out roadmap);
            roadmapCache[destinationIndex] = (true, success ? roadmap : null);
         }
         roadmap = roadmapCache[destinationIndex].roadmap;
         return roadmap != null;
      }
   }
}