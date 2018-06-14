using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ClipperLib;
using OpenMOBA.DataStructures;
using OpenMOBA.Foundation.Terrain.CompilationResults.Local;
using OpenMOBA.Foundation.Terrain.Declarations;
using OpenMOBA.Geometry;

#if use_fixed
using cDouble = FixMath.NET.Fix64;
#else
using cDouble = System.Double;
#endif

namespace OpenMOBA.Foundation.Terrain.CompilationResults.Overlay {
   public class TerrainOverlayNetwork {
      private readonly Dictionary<SectorNodeDescription, LocalGeometryView> activeLocalGeometryViewBySectorNodeDescription;
      private readonly Dictionary<SectorNodeDescription, TerrainOverlayNetworkNode[]> activeTerrainNodesBySectorNodeDescription;
      private readonly cDouble agentRadius;

      private readonly IReadOnlyList<SectorEdgeDescription> edgeDescriptions;
      private readonly ILookup<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsByDestination;
      private readonly MultiValueDictionary<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsByEndpoints;
      private readonly ILookup<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsBySource;

      private readonly Dictionary<(SectorEdgeDescription, LocalGeometryView, LocalGeometryView), List<EdgeJob>> edgeJobCache = new Dictionary<(SectorEdgeDescription, LocalGeometryView, LocalGeometryView), List<EdgeJob>>();
      private readonly Dictionary<LocalGeometryView, List<PolyNode>> landPolyNodesByDefaultLocalGeometryView;
      private readonly Dictionary<(SectorNodeDescription, PolyNode), TerrainOverlayNetworkNode> terrainNodesBySectorNodeDescriptionAndPolyNode;

      public TerrainOverlayNetwork(
         cDouble agentRadius,
         Dictionary<SectorNodeDescription, LocalGeometryView> activeLocalGeometryViewBySectorNodeDescription,
         Dictionary<SectorNodeDescription, TerrainOverlayNetworkNode[]> activeTerrainNodesBySectorNodeDescription,
         Dictionary<LocalGeometryView, List<PolyNode>> landPolyNodesByDefaultLocalGeometryView,
         Dictionary<(SectorNodeDescription, PolyNode), TerrainOverlayNetworkNode> terrainNodesBySectorNodeDescriptionAndPolyNode,
         IReadOnlyList<SectorEdgeDescription> edgeDescriptions,
         ILookup<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsBySource,
         ILookup<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsByDestination,
         MultiValueDictionary<SectorNodeDescription, SectorEdgeDescription> edgeDescriptionsByEndpoints
      ) {
         this.agentRadius = agentRadius;
         this.activeLocalGeometryViewBySectorNodeDescription = activeLocalGeometryViewBySectorNodeDescription;
         this.activeTerrainNodesBySectorNodeDescription = activeTerrainNodesBySectorNodeDescription;
         this.landPolyNodesByDefaultLocalGeometryView = landPolyNodesByDefaultLocalGeometryView;
         this.terrainNodesBySectorNodeDescriptionAndPolyNode = terrainNodesBySectorNodeDescriptionAndPolyNode;
         this.edgeDescriptions = edgeDescriptions;
         this.edgeDescriptionsBySource = edgeDescriptionsBySource;
         this.edgeDescriptionsByDestination = edgeDescriptionsByDestination;
         this.edgeDescriptionsByEndpoints = edgeDescriptionsByEndpoints;
      }

      public IReadOnlyCollection<TerrainOverlayNetworkNode> TerrainNodes => terrainNodesBySectorNodeDescriptionAndPolyNode.Values;
      public BvhTreeAABB<TerrainOverlayNetworkNode> NodeBvh { get; private set; }

      public void Initialize() {
         foreach (var node in TerrainNodes) {
            node.Network = this;
         }

         NodeBvh = BvhTreeAABB<TerrainOverlayNetworkNode>.Build(TerrainNodes.Select(n => n.SectorNodeDescription.WorldBounds.PairValue(n)));
         foreach (var edge in edgeDescriptions) UpdateEdge(edge, false);
      }

      private void UpdateEdge(SectorEdgeDescription edgeDescription, bool forceRender) {
         var sourceLgv = activeLocalGeometryViewBySectorNodeDescription[edgeDescription.Source];
         var destinationLgv = activeLocalGeometryViewBySectorNodeDescription[edgeDescription.Destination];

         // Compute Edge Job
         var key = (edgeDescription, sourceLgv, destinationLgv);
         List<EdgeJob> edgeJobs;
         if (!edgeJobCache.TryGetValue(key, out edgeJobs)) {
            var crossoverPointSpacing = CDoubleMath.Max(CDoubleMath.c5, agentRadius * CDoubleMath.c0_1);
            edgeJobs = edgeJobCache[key] = edgeDescription.EmitCrossoverJobs(crossoverPointSpacing, sourceLgv, destinationLgv);
         }

         // Update Source/Destination PolyNodes' Waypoint Sets
         foreach (var edgeJob in edgeJobs) {
            var sourceNode = terrainNodesBySectorNodeDescriptionAndPolyNode[(edgeDescription.Source, edgeJob.SourcePolyNode)];
            var destinationNode = terrainNodesBySectorNodeDescriptionAndPolyNode[(edgeDescription.Destination, edgeJob.DestinationPolyNode)];
            var (sourceCrossoverPoints, destinationCrossoverPoints) = ComputeEdgeCrossoverPoints(edgeJob.SourceSegment, edgeJob.DestinationSegment);

            var sourceCrossoverIndices = sourceNode.CrossoverPointManager.AddMany(edgeJob.SourceSegment, sourceCrossoverPoints);
            var destinationCrossoverIndices = destinationNode.CrossoverPointManager.AddMany(edgeJob.DestinationSegment, destinationCrossoverPoints);

            var edges = new TerrainOverlayNetworkEdge[sourceCrossoverIndices.Length];
            for (var i = 0; i < edges.Length; i++) edges[i] = new TerrainOverlayNetworkEdge(sourceCrossoverIndices[i], destinationCrossoverIndices[i], 0);
            //            var edges = sourceCrossoverIndices.Zip(destinationCrossoverIndices, (sci, dci) => new TerrainOverlayNetworkEdge(sci, dci, 0))
            //                                              .ToArray();
            var edgeGroup = new TerrainOverlayNetworkEdgeGroup(sourceNode, destinationNode, edgeJob, edges);
            sourceNode.OutboundEdgeGroups.Add(destinationNode, edgeGroup);
            destinationNode.InboundEdgeGroups.Add(sourceNode, edgeGroup);
         }

         // Flag Source/Destination PolyNodes as dirty.
         //         JSSGCPNWM j = null;
         //         foreach (var (sourcePolyNode, destinationPolyNode, sourcePoint, destinationPoint) in edgeJobs.CrossoverJobs) {
         //            var sourceTerrainNode = terrainNodesBySectorNodeDescriptionAndPolyNode[(edgeDescription.Source, sourcePolyNode)];
         //            var destinationTerrainNode = terrainNodesBySectorNodeDescriptionAndPolyNode[(edgeDescription.Destination, destinationPolyNode)];
         //         }
      }

      private (IntVector2[], IntVector2[]) ComputeEdgeCrossoverPoints(DoubleLineSegment2 sourceSegment, DoubleLineSegment2 destinationSegment) {
         var sourceSegmentVector = sourceSegment.First.To(sourceSegment.Second);
         var sourceSegmentLength = sourceSegmentVector.Norm2D();

         var destinationSegmentVector = destinationSegment.First.To(destinationSegment.Second);
         var destinationSegmentLength = destinationSegmentVector.Norm2D();

         var longestSegmentLength = CDoubleMath.Max(sourceSegmentLength, destinationSegmentLength);
         var crossoverPointSpacing = (cDouble)1000;
         var points = (int)CDoubleMath.Ceiling(longestSegmentLength / crossoverPointSpacing) + 1;

         var sourceCrossoverPoints = new IntVector2[points];
         var destinationCrossoverPoints = new IntVector2[points];
         for (var i = 0; i < points; i++) {
            var t = (cDouble)i / (cDouble)(points - 1);
            sourceCrossoverPoints[i] = (sourceSegment.First + t * sourceSegmentVector).LossyToIntVector2();
            destinationCrossoverPoints[i] = (destinationSegment.First + t * destinationSegmentVector).LossyToIntVector2();
         }

         return (sourceCrossoverPoints, destinationCrossoverPoints);
      }

      private void Dijkstra(TerrainOverlayNetworkNode node) {
         //         node.CrossoverPointManager
      }
   }

   /// <summary>
   ///    Handles finding optimal path between crossoverpoints in a given terrainNode
   ///    Waypoints refer to waypoints in the polynode visibility graph.
   ///    CrossoverPoints are a tacked on concept by the terrain engine.
   /// </summary>
   public class PolyNodeCrossoverPointManager {
      //-------------------------------------------------------------------------------------------
      // For debugging computational complexity
      //-------------------------------------------------------------------------------------------
      public static int AddManyInvocationCount;
      public static int AddManyConvexHullsComputed = 0;
      public static int CrossoverPointsAdded;
      public static int FindOptimalLinksToCrossoversInvocationCount;
      public static int FindOptimalLinksToCrossovers_CandidateWaypointVisibilityCheck;
      public static int FindOptimalLinksToCrossovers_CostToWaypointCount;
      public static int ProcessCpiInvocationCount;
      public static int ProcessCpiInvocation_CandidateBarrierIntersectCount;
      public static int ProcessCpiInvocation_DirectCount;
      public static int ProcessCpiInvocation_IndirectCount;
      private readonly int[] allWaypointIndices;

      //-------------------------------------------------------------------------------------------
      // Data about Crossover Points
      //-------------------------------------------------------------------------------------------
      private readonly AddOnlyOrderedHashSet<IntVector2> crossoverPoints = new AddOnlyOrderedHashSet<IntVector2>();

      private readonly Dictionary<DoubleLineSegment2, int[]> indicesBySegment = new Dictionary<DoubleLineSegment2, int[]>();
      private readonly PolyNode landPolyNode;

      /// <summary>
      ///    [SourceCrossoverIndex][DestCrossoverIndex] = (first hop, total path cost),
      ///    probably followed by a lookup of [DestCrossoverIndex][first hop] in optimalLinkToWaypointsByCrossoverPointIndex
      /// </summary>
      private readonly List<ExposedArrayList<PathLink>> optimalLinkToOtherCrossoversByCrossoverPointIndex = new List<ExposedArrayList<PathLink>>();

      /// <summary>
      ///    [CrossoverIndex] => visible waypoint links of (visible waypoint index, cost from crossover point to waypoint)[]
      /// </summary>
      /// <summary>
      ///    [CrossoverIndex][WaypointIndex] => links of (next hop, total path cost)
      ///    Note: final hop will be a waypoint to itself of nonzero cost. This should hop to the crossover point.
      /// </summary>
      private readonly List<PathLink[]> optimalLinkToWaypointsByCrossoverPointIndex = new List<PathLink[]>();

      private readonly Dictionary<IntVector2, DoubleLineSegment2> segmentByCrossoverPoint = new Dictionary<IntVector2, DoubleLineSegment2>();
      private readonly PolyNodeVisibilityGraph visibilityGraph;

      //-------------------------------------------------------------------------------------------
      // Data from the PolyNode
      //-------------------------------------------------------------------------------------------
      private readonly IntVector2[] waypoints;

      /// <summary>
      ///    [DestinationWaypointIndex][SourceWaypointIndex] => (next hop, total path cost)
      /// </summary>
      private readonly PathLink[][] waypointToWaypointLut;

      public PolyNodeCrossoverPointManager(PolyNode landPolyNode) {
         this.landPolyNode = landPolyNode;
         waypoints = landPolyNode.FindAggregateContourCrossoverWaypoints();
         allWaypointIndices = waypoints.Map((_, i) => i);
         visibilityGraph = landPolyNode.ComputeVisibilityGraph();
         waypointToWaypointLut = visibilityGraph.BuildWaypointToWaypointLut();
      }

      public IReadOnlyList<IntVector2> Waypoints => waypoints;
      public IReadOnlyList<IntVector2> CrossoverPoints => crossoverPoints;
      public IReadOnlyList<IReadOnlyList<PathLink>> OptimalLinkToOtherCrossoversByCrossoverPointIndex => optimalLinkToOtherCrossoversByCrossoverPointIndex;
      public IReadOnlyList<PathLink[]> OptimalLinkToWaypointsByCrossoverPointIndex => optimalLinkToWaypointsByCrossoverPointIndex;
      public PathLink[][] WaypointToWaypointLut => waypointToWaypointLut;

      public static void DumpPerformanceCounters() {
         Console.WriteLine(
            $"== Perf Counters ==" + Environment.NewLine +
            $"+ AddMany: " + Environment.NewLine +
            $"  + Invokes: {AddManyInvocationCount}" + Environment.NewLine +
            $"  + ConvexHullsComputed: {AddManyConvexHullsComputed}" + Environment.NewLine +
            $"  + CrossoverPointsAdded: {CrossoverPointsAdded}" + Environment.NewLine +
            $"  + FindOptimalLinksToCrossovers:" + Environment.NewLine +
            $"    + Invokes: {FindOptimalLinksToCrossoversInvocationCount}" + Environment.NewLine +
            $"    + CandidateWaypointVisibilityChecks: {FindOptimalLinksToCrossovers_CandidateWaypointVisibilityCheck}" + Environment.NewLine +
            $"    + ProcessCPI:" + Environment.NewLine +
            $"      + Invokes: {ProcessCpiInvocationCount}" + Environment.NewLine +
            $"      + CandidateBarrierIntersectCounts: {ProcessCpiInvocation_CandidateBarrierIntersectCount}" + Environment.NewLine +
            $"      + Directs: {ProcessCpiInvocation_DirectCount}" + Environment.NewLine +
            $"      + Indirects: {ProcessCpiInvocation_IndirectCount}");
      }

      // Todo: Can we support DV2s?

      public int[] AddMany(DoubleLineSegment2 edgeSegment, IntVector2[] points) {
         Interlocked.Increment(ref AddManyInvocationCount);
         //         return points.Map(p => 0);
         var segmentSeeingWaypoints = landPolyNode.ComputeSegmentSeeingWaypoints(edgeSegment);

         // It's safe to assume <some> point in points will be new, so preprocess which segments are betwen us and other edge segments
         var barriers = landPolyNode.FindContourAndChildHoleBarriers();
         Dictionary<DoubleLineSegment2, IntLineSegment2[]> barriersBySegment = indicesBySegment.Map((s, _) => {
            Interlocked.Increment(ref AddManyConvexHullsComputed);
            var hull = GeometryOperations.ConvexHull4(s.First, s.Second, edgeSegment.First, edgeSegment.Second);
            return barriers.Where(b => {
               var barrierDv2 = new DoubleLineSegment2(b.First.ToDoubleVector2(), b.Second.ToDoubleVector2());
               return GeometryOperations.SegmentIntersectsConvexPolygonInterior(barrierDv2, hull);
            }).ToArray();
         });

         return indicesBySegment[edgeSegment] = points.Map(p => {
            if (TryAdd(p, out var cpi)) {
               Interlocked.Increment(ref CrossoverPointsAdded);
               segmentByCrossoverPoint[p] = edgeSegment;
            }

            return cpi;
         });

         bool TryAdd(IntVector2 crossoverPoint, out int crossoverPointIndex) {
            if (!crossoverPoints.TryAdd(crossoverPoint, out crossoverPointIndex)) return false;

            var (visibleWaypointLinks, visibleWaypointLinksLength, optimalLinkToWaypoints, optimalLinkToCrossovers) = FindOptimalLinksToCrossovers(crossoverPoint, segmentSeeingWaypoints, barriersBySegment);
            // visibleWaypointLinksByCrossoverPointIndex.Add(visibleWaypointLinks);
            optimalLinkToWaypointsByCrossoverPointIndex.Add(optimalLinkToWaypoints);
            optimalLinkToOtherCrossoversByCrossoverPointIndex.Add(optimalLinkToCrossovers);
            Trace.Assert(optimalLinkToOtherCrossoversByCrossoverPointIndex.Count == optimalLinkToCrossovers.Count);

            for (var otherCpi = 0; otherCpi < crossoverPoints.Count - 1; otherCpi++) {
               var linkToOther = optimalLinkToCrossovers[otherCpi];
               var linkFromOther = linkToOther.PriorIndex < 0
                  ? new PathLink { PriorIndex = linkToOther.PriorIndex, TotalCost = linkToOther.TotalCost }
                  : new PathLink {
                     PriorIndex = optimalLinkToWaypointsByCrossoverPointIndex[otherCpi][linkToOther.PriorIndex].PriorIndex,
                     TotalCost = linkToOther.TotalCost
                  };
               optimalLinkToOtherCrossoversByCrossoverPointIndex[otherCpi].Add(linkFromOther);
            }

            return true;
         }
      }

      private (PathLink[] visibleWaypointLinks, int visibleWaypointLinksLength, PathLink[] optimalLinkToWaypoints, ExposedArrayList<PathLink> optimalLinkToCrossovers)
         foltcEmptyResult = (new PathLink[0], 0, null, new ExposedArrayList<PathLink>());

      public (PathLink[] visibleWaypointLinks, int visibleWaypointLinksLength, PathLink[] optimalLinkToWaypoints, ExposedArrayList<PathLink> optimalLinkToCrossovers) 
            FindOptimalLinksToCrossovers(
               IntVector2 p, 
               int[] candidateWaypoints = null, 
               IReadOnlyDictionary<DoubleLineSegment2, IntLineSegment2[]> candidateBarriersByDestinationSegment = null
            ) {
         Interlocked.Increment(ref FindOptimalLinksToCrossoversInvocationCount);
         if (crossoverPoints.Count == 0 || (candidateWaypoints != null && candidateWaypoints.Length == 0)) {
            return foltcEmptyResult;
         }

         //var links = new List<PathLink>(128);
         //links.Resize(crossoverPoints.Count);
         //return (new PathLink[0], 0, new PathLink[waypoints.Length], links);

         int visibleWaypointLinksLength;
         PathLink[] optimalLinkToWaypoints;
         var visibleWaypointLinks = FindVisibleWaypointLinks(p, candidateWaypoints, out visibleWaypointLinksLength, out optimalLinkToWaypoints);

         // Cost from p to other crossoverPoints...
         var optimalLinkToCrossovers = new ExposedArrayList<PathLink>(Math.Max(128, crossoverPoints.Count));
         optimalLinkToCrossovers.size = crossoverPoints.Count;

         void ProcessCpi(int cpi, IntLineSegment2[] candidateBarriers) {
            // for bench
            optimalLinkToCrossovers[cpi] = new PathLink {
               PriorIndex = PathLink.ErrorInvalidIndex,
               TotalCost = cDouble.MaxValue
            };
            return;

            Interlocked.Increment(ref ProcessCpiInvocationCount);
            bool isDirectPath;
            if (p == crossoverPoints[cpi]) {
               isDirectPath = true; // degenerate segment
            } else if (candidateBarriers == null) {
               //               Console.WriteLine($"Try intersect {cpi}: {p} to {crossoverPoints[cpi]}");
               var isDirectPath1 = !landPolyNode.FindContourAndChildHoleBarriersBvh().Intersects(new IntLineSegment2(p, crossoverPoints[cpi]));
               //               var isDirectPath2 = landPolyNode.SegmentInLandPolygonNonrecursive(p, crossoverPoints[cpi]);
               //               Console.WriteLine($" => res {isDirectPath1} vs {isDirectPath2}");
               isDirectPath = isDirectPath1;
            } else {
               // below is equivalent to (and shaved off 22% execution time relative to):
               // isDirectPath = candidateBarriers.None(new ILS2(p, crossoverPoints[cpi]).Intersects)
               isDirectPath = true;
               //var seg = new IntLineSegment2(p, crossoverPoints[cpi]);
               for (var bi = 0; bi < candidateBarriers.Length && isDirectPath; bi++) {
                  Interlocked.Increment(ref ProcessCpiInvocation_CandidateBarrierIntersectCount);

                  if (IntLineSegment2.Intersects(
                     p.X, p.Y, crossoverPoints[cpi].X, crossoverPoints[cpi].Y, 
                     candidateBarriers[bi].X1, candidateBarriers[bi].Y1, candidateBarriers[bi].X2, candidateBarriers[bi].Y2))
                     isDirectPath = false;
                  //if (seg.Intersects(ref candidateBarriers[bi])) isDirectPath = false;
               }
            }

            if (isDirectPath) {
               Interlocked.Increment(ref ProcessCpiInvocation_DirectCount);
               var totalCost = p.To(crossoverPoints[cpi]).Norm2F();
#if !use_fixed
               Trace.Assert(!double.IsNaN(totalCost));
#endif
               optimalLinkToCrossovers[cpi] = new PathLink {
                  PriorIndex = PathLink.DirectPathIndex,
                  TotalCost = totalCost
               };
            } else {
               Interlocked.Increment(ref ProcessCpiInvocation_IndirectCount);
               var otherOptimalLinkByWaypointIndex = optimalLinkToWaypointsByCrossoverPointIndex[cpi];

               //--
               // Below is equivalent to (and shaved off 14% execution time relative to):
               // visibleWaypointLinks.MinBy(cpwl => cpwl.TotalCost + otherOptimalLinkByWaypointIndex[cpwl.PriorIndex].TotalCost);
               var optimalLinkToOtherCrossoverPointIndex = -1;
               var optimalLinkToOtherCrossoverPointCost = cDouble.MaxValue;
               for (var vwli = 0; vwli < visibleWaypointLinksLength; vwli++) {
                  ref var vwl = ref visibleWaypointLinks[vwli];
                  var cost = vwl.TotalCost + otherOptimalLinkByWaypointIndex[vwl.PriorIndex].TotalCost;
                  if (cost < optimalLinkToOtherCrossoverPointCost) {
                     optimalLinkToOtherCrossoverPointIndex = vwli;
                     optimalLinkToOtherCrossoverPointCost = cost;
                  }
               }

               if (optimalLinkToOtherCrossoverPointIndex == -1) {
                  // Todo: This shouldn't happen!
                  optimalLinkToCrossovers[cpi] = new PathLink {
                     PriorIndex = PathLink.ErrorInvalidIndex,
                     TotalCost = cDouble.MaxValue
                  };
               } else {
                  ref var optimalLinkToOtherCrossoverPoint = ref visibleWaypointLinks[optimalLinkToOtherCrossoverPointIndex];

                  //--
                  var optimalLinkFromOtherCrossoverPoint = otherOptimalLinkByWaypointIndex[optimalLinkToOtherCrossoverPoint.PriorIndex];
                  var totalCost = optimalLinkToOtherCrossoverPoint.TotalCost + optimalLinkFromOtherCrossoverPoint.TotalCost;
#if !use_fixed
                  Trace.Assert(!double.IsNaN(totalCost));
#endif
                  optimalLinkToCrossovers[cpi] = new PathLink {
                     PriorIndex = optimalLinkToOtherCrossoverPoint.PriorIndex,
                     TotalCost = totalCost
                  };
               }
            }
         }

         if (candidateBarriersByDestinationSegment == null) {
            // Console.WriteLine("Warning: candidateBarriersByDestinationSegment null?");
            for (var cpi = 0; cpi < crossoverPoints.Count; cpi++) ProcessCpi(cpi, null);
         } else {
            var isCpiVisited = new bool[crossoverPoints.Count];
            foreach (var (segment, barriers) in candidateBarriersByDestinationSegment)
            foreach (var cpi in indicesBySegment[segment]) {
               if (isCpiVisited[cpi]) continue;
               ProcessCpi(cpi, barriers);
               isCpiVisited[cpi] = true;
            }

            for (var cpi = 0; cpi < isCpiVisited.Length; cpi++) {
               if (isCpiVisited[cpi]) continue;
               ProcessCpi(cpi, new IntLineSegment2[0]);
            }
         }

         return (visibleWaypointLinks, visibleWaypointLinksLength, optimalLinkToWaypoints, optimalLinkToCrossovers);
      }

      public PathLink[] FindVisibleWaypointLinks(IntVector2 p, int[] candidateWaypoints, out int visibleWaypointLinksLength, out PathLink[] optimalLinkToWaypoints) {
         candidateWaypoints = candidateWaypoints ?? allWaypointIndices;

         // Find cost from p to visible waypoints - crazy inefficient (has visibility poly, atan)!
         var visibleWaypointLinks = new PathLink[candidateWaypoints.Length];
         visibleWaypointLinksLength = 0;
         for (var i = 0; i < candidateWaypoints.Length; i++) {
            Interlocked.Increment(ref FindOptimalLinksToCrossovers_CandidateWaypointVisibilityCheck);
            var wi = candidateWaypoints[i];
            var costSquared = waypoints[wi].To(p).SquaredNorm2();
            var visibilityPolygon = landPolyNode.ComputeWaypointVisibilityPolygons()[wi];
            if (visibilityPolygon.Contains(p)) {
               visibleWaypointLinks[visibleWaypointLinksLength] = new PathLink { PriorIndex = wi, TotalCost = CDoubleMath.Sqrt((cDouble)costSquared) };
               visibleWaypointLinksLength++;
            }
         }

         // Cost from p to all waypoints. Below is an unrolled map
         optimalLinkToWaypoints = new PathLink[waypoints.Length];
         for (var wi = 0; wi < waypoints.Length; wi++) {
            Interlocked.Increment(ref FindOptimalLinksToCrossovers_CostToWaypointCount);
            // unrolled from minby loop for 25% perf gain
            var optimalLinkIndex = -1;
            var optimalLinkCost = cDouble.MaxValue;
            for (var i = 0; i < visibleWaypointLinksLength; i++) {
               ref var link = ref visibleWaypointLinks[i];
               var (a, b) = (wi, link.PriorIndex);
               if (a < b) (a, b) = (b, a);
               var linkCost = link.TotalCost + waypointToWaypointLut[a][b].TotalCost;
               if (linkCost < optimalLinkCost) {
                  optimalLinkIndex = i;
                  optimalLinkCost = linkCost;
               }
            }

            if (optimalLinkIndex == -1) {
               // todo: this shouldn't happen!
               optimalLinkToWaypoints[wi] = new PathLink {
                  PriorIndex = PathLink.ErrorInvalidIndex,
                  TotalCost = cDouble.MaxValue
               };
            } else {
               ref var optimalLink = ref visibleWaypointLinks[optimalLinkIndex];
               var (c, d) = (wi, optimalLink.PriorIndex);
               if (c < d) (c, d) = (d, c);
               optimalLinkToWaypoints[wi] = new PathLink {
                  PriorIndex = optimalLink.PriorIndex,
                  TotalCost = optimalLink.TotalCost + waypointToWaypointLut[c][d].TotalCost
               };
            }
         }

         ;
         return visibleWaypointLinks;
      }


      public void FindWaypointToWaypointPath(int sourceWaypoint, int destinationWaypoint) {
         var sourcePath = new List<PathLink>();
         var destPath = new List<PathLink>();

         // must query with [a][b] where a > b
         while (sourceWaypoint != destinationWaypoint)
            if (sourceWaypoint < destinationWaypoint) {
               // grow destination, query with [destinationWaypoint][sourceWaypoint]
               var link = waypointToWaypointLut[destinationWaypoint][sourceWaypoint];
               destPath.Add(link);
               destinationWaypoint = link.PriorIndex;
            } else {
               // grow source, query with [sourceWaypoint][destinationWaypoint]
               var link = waypointToWaypointLut[sourceWaypoint][destinationWaypoint];
               sourcePath.Add(link);
               sourceWaypoint = link.PriorIndex;
            }
      }
   }
}
