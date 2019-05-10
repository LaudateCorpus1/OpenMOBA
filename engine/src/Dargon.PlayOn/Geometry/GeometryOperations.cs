using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Dargon.PlayOn.DataStructures;
using cInt = System.Int32;
using Clk = Dargon.PlayOn.Geometry.Clockness;

#if use_fixed
using cDouble = FixMath.NET.Fix64;
#else
using cDouble = System.Double;
#endif

namespace Dargon.PlayOn.Geometry {
   public static class GeometryOperations {
#if use_fixed
      public static readonly cDouble kEpsilon = CDoubleMath.Epsilon;

      public static bool IsReal(cDouble v) => true;
      public static bool IsReal(DoubleVector2 v) => true;
      public static bool IsReal(DoubleVector3 v) => true;
#else
      // C# double.Epsilon is denormal = terrible perf; avoid and use this instead.
      // https://www.johndcook.com/blog/2012/01/05/double-epsilon-dbl_epsilon/
      public const double kEpsilon = 10E-16;

      private const double kPointInTriangleEpsilon = 5E-6;

      public static bool IsReal(double v) => !(double.IsNaN(v) || double.IsInfinity(v));
      public static bool IsReal(DoubleVector2 v) => IsReal(v.X) && IsReal(v.Y);
      public static bool IsReal(DoubleVector3 v) => IsReal(v.X) && IsReal(v.Y) && IsReal(v.Z);
#endif

      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(IntVector2 a, IntVector2 b, IntVector2 c) => Clockness(b - a, b - c);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(IntVector2 ba, IntVector2 bc) => Clockness(ba.X, ba.Y, bc.X, bc.Y);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(cInt ax, cInt ay, cInt bx, cInt by, cInt cx, cInt cy) => Clockness(bx - ax, by - ay, bx - cx, by - cy);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(cInt bax, cInt bay, cInt bcx, cInt bcy) => (Clockness)Math.Sign(Cross(bax, bay, bcx, bcy));

      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long Cross(this IntVector2 a, IntVector2 b) => Cross(a.X, a.Y, b.X, b.Y);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long Cross(cInt ax, cInt ay, cInt bx, cInt by) => (long)ax * by - (long)ay * bx;

      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(DoubleVector2 a, DoubleVector2 b, DoubleVector2 c) => Clockness(b - a, b - c);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(DoubleVector2 ba, DoubleVector2 bc) => Clockness(ba.X, ba.Y, bc.X, bc.Y);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(cDouble ax, cDouble ay, cDouble bx, cDouble by, cDouble cx, cDouble cy) => Clockness(bx - ax, by - ay, bx - cx, by - cy);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Clockness Clockness(cDouble bax, cDouble bay, cDouble bcx, cDouble bcy) => (Clockness)CDoubleMath.Sign(Cross(bax, bay, bcx, bcy));

      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static cDouble Cross(this DoubleVector2 a, DoubleVector2 b) => Cross(a.X, a.Y, b.X, b.Y);
      [MethodImpl(MethodImplOptions.AggressiveInlining)] public static cDouble Cross(cDouble ax, cDouble ay, cDouble bx, cDouble by) => ax * by - ay * bx;

      public static Vector3 ToDotNetVector(this IntVector3 v) => new Vector3(v.X, v.Y, v.Z);
      // TODO: Determinism
      public static Vector3 ToDotNetVector(this DoubleVector3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
      public static DoubleVector3 ToOpenMobaVector(this Vector3 v) => new DoubleVector3((cDouble)v.X, (cDouble)v.Y, (cDouble)v.Z);

      // TODO: Determinism
      public static Vector2 ToDotNetVector(this IntVector2 v) => new Vector2(v.X, v.Y);
      public static Vector2 ToDotNetVector(this DoubleVector2 v) => new Vector2((float)v.X, (float)v.Y);
      public static DoubleVector2 ToOpenMobaVector(this Vector2 v) => new DoubleVector2((cDouble)v.X, (cDouble)v.Y);

      // todo: this is probablby wrong, sorry.
      public static Rectangle ToDotNetRectangle(this IntRect2 r) => new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

      public static bool IsCollinearWith(this IntLineSegment2 a, IntLineSegment2 b) {
         var a1a2 = a.First.To(a.Second);
         var b1b2 = b.First.To(b.Second);
         var a1b1 = a.First.To(b.First);
         var isParallel = Cross(a1a2, b1b2) == 0;
         var isA1A2CollinearB1 = Cross(a1a2, a1b1) == 0;
         return isParallel && isA1A2CollinearB1;
      }

      // todo: this needs love
      public static bool TryFindLineLineIntersection(IntLineSegment2 a, IntLineSegment2 b, out DoubleVector2 result) {
         var p1 = a.First;
         var p2 = a.Second;
         var p3 = b.First;
         var p4 = b.Second;

         var v21 = p1 - p2; // (x1 - x2, y1 - y2)
         var v43 = p3 - p4; // (x3 - x4, y3 - y4)

         var denominator = Cross(v21, v43);
         if (denominator == 0) {
            result = DoubleVector2.Zero;
            return false;
         }

         var p1xp2 = Cross(p1, p2); // x1y2 - y1x2
         var p3xp4 = Cross(p3, p4); // x3y4 - y3x4
         var numeratorX = p1xp2 * v43.X - v21.X * p3xp4;
         var numeratorY = p1xp2 * v43.Y - v21.Y * p3xp4;

         result = new DoubleVector2((cDouble)numeratorX / (cDouble)denominator, (cDouble)numeratorY / (cDouble)denominator);
         return true;
      }

      // todo: this needs love
      public static bool TryFindLineLineIntersection(DoubleVector2 a1, DoubleVector2 a2, DoubleVector2 b1, DoubleVector2 b2, out DoubleVector2 result) {
         var p1 = a1;
         var p2 = a2;
         var p3 = b1;
         var p4 = b2;

         var v21 = p1 - p2; // (x1 - x2, y1 - y2)
         var v43 = p3 - p4; // (x3 - x4, y3 - y4)

         var denominator = Cross(v21, v43);
         if (denominator == CDoubleMath.c0) {
            result = DoubleVector2.Zero;
            return false;
         }

         var p1xp2 = Cross(p1, p2); // x1y2 - y1x2
         var p3xp4 = Cross(p3, p4); // x3y4 - y3x4
         var numeratorX = p1xp2 * v43.X - v21.X * p3xp4;
         var numeratorY = p1xp2 * v43.Y - v21.Y * p3xp4;

         result = new DoubleVector2((cDouble)numeratorX / (cDouble)denominator, (cDouble)numeratorY / (cDouble)denominator);
         return true;
      }

      // NOTE: Assumes lines are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, lines should not have more than 1 point of intersection.
      // if lines DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindNonoverlappingLineLineIntersectionT(ref IntLineSegment2 a, ref IntLineSegment2 b, out cDouble tForA) {
         return TryFindNonoverlappingLineLineIntersectionT(a.First, a.Second, b.First, b.Second, out tForA);
      }

      public static bool TryFindNonoverlappingLineLineIntersectionT(IntVector2 a1, IntVector2 a2, IntVector2 b1, IntVector2 b2, out cDouble tForA) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var p = a1;
         var r = a1.To(a2);
         var q = b1;
         var s = b1.To(b2);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;
         tForA = t;
         return true;

         fail:
         tForA = default;
         return false;
      }

      // NOTE: Assumes lines are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, lines should not have more than 1 point of intersection.
      // if lines DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindNonoverlappingLineLineIntersectionT(ref DoubleLineSegment2 a, ref DoubleLineSegment2 b, out cDouble tForA) {
         return TryFindNonoverlappingLineLineIntersectionT(a.First, a.Second, b.First, b.Second, out tForA);
      }

      public static bool TryFindNonoverlappingLineLineIntersectionT(DoubleVector2 a1, DoubleVector2 a2, DoubleVector2 b1, DoubleVector2 b2, out cDouble tForA) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var p = a1;
         var r = a1.To(a2);
         var q = b1;
         var s = b1.To(b2);

         var rxs = Cross(r, s);
         if (rxs == CDoubleMath.c0) { // iffy?
            goto fail;
         }

         var qmp = q - p;
         var t = Cross(qmp, s) / (cDouble)rxs;
         tForA = t;
         return true;

         fail:
         tForA = default;
         return false;
      }

      public static bool Intersects(this IntLineSegment2 a, IntLineSegment2 b) {
         return TryFindSegmentSegmentIntersection(ref a, ref b, out DoubleVector2 _);
      }

      // NOTE: Assumes segments are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, segments should not have more than 1 point of intersection.
      // if segments DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindNonoverlappingSegmentSegmentIntersectionT(ref IntLineSegment2 a, ref IntLineSegment2 b, out cDouble tForA) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var p = a.First;
         var r = a.First.To(a.Second);
         var q = b.First;
         var s = b.First.To(b.Second);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;
         if (t < CDoubleMath.c0 || t > CDoubleMath.c1) {
            goto fail;
         }

         var u = (cDouble)Cross(qmp, r) / (cDouble)rxs;
         if (u < CDoubleMath.c0 || u > CDoubleMath.c1) {
            goto fail;
         }

         tForA = t;
         return true;

         fail:
         tForA = default;
         return false;
      }

      // NOTE: Assumes segments are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, segments should not have more than 1 point of intersection.
      // if segments DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindSegmentSegmentIntersection(ref IntLineSegment2 a, ref IntLineSegment2 b, out DoubleVector2 result) {
         if (TryFindNonoverlappingSegmentSegmentIntersectionT(ref a, ref b, out cDouble t)) {
            var p = a.First;
            var r = a.First.To(a.Second);

            result = new DoubleVector2((cDouble)p.X + t * (cDouble)r.X, (cDouble)p.Y + t * (cDouble)r.Y);
            return true;
         }
         result = DoubleVector2.Zero;
         return false;
      }

      // NOTE: Assumes segments are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, segments should not have more than 1 point of intersection.
      // if segments DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindNonoverlappingRaySegmentIntersectionT(ref IntVector2 p, ref IntVector2 dir, ref IntLineSegment2 segment, out cDouble tForRay) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var r = dir;
         var q = segment.First;
         var s = segment.First.To(segment.Second);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;
         if (t < CDoubleMath.c0) {
            goto fail;
         }

         var u = (cDouble)Cross(qmp, r) / (cDouble)rxs;
         if (u < CDoubleMath.c0 || u > CDoubleMath.c1) {
            goto fail;
         }

         tForRay = t;
         return true;

         fail:
         tForRay = default;
         return false;
      }

      public static bool TryFindNonoverlappingRaySegmentIntersectionT(in DoubleVector2 p, in DoubleVector2 dir, in DoubleLineSegment2 segment, out cDouble tForRay) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var r = dir;
         var q = segment.First;
         var s = segment.First.To(segment.Second);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;
         if (t < CDoubleMath.c0) {
            goto fail;
         }

         var u = (cDouble)Cross(qmp, r) / (cDouble)rxs;
         if (u < CDoubleMath.c0 || u > CDoubleMath.c1) {
            goto fail;
         }

         tForRay = t;
         return true;

         fail:
         tForRay = default;
         return false;
      }

      // NOTE: Assumes segments are valid (two distinct endpoints) NOT line-OVERLAPPING
      // that is, segments should not have more than 1 point of intersection.
      // if segments DO have more than 1 point of intersection, this returns no intersection found.
      public static bool TryFindNonoverlappingLineSegmentIntersectionT(ref IntLineSegment2 line, ref IntLineSegment2 segment, out cDouble tForRay) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var p = line.First;
         var r = line.First.To(line.Second);
         var q = segment.First;
         var s = segment.First.To(segment.Second);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;

         var u = (cDouble)Cross(qmp, r) / (cDouble)rxs;
         if (u < CDoubleMath.c0 || u > CDoubleMath.c1) {
            goto fail;
         }

         tForRay = t;
         return true;

         fail:
         tForRay = default;
         return false;
      }

      public static bool TryFindNonoverlappingLineSegmentIntersectionT(in DoubleLineSegment2 line, in DoubleLineSegment2 segment, out cDouble tForRay) {
         // via http://stackoverflow.com/questions/563198/how-do-you-detect-where-two-line-segments-intersect
         var p = line.First;
         var r = line.First.To(line.Second);
         var q = segment.First;
         var s = segment.First.To(segment.Second);

         var rxs = Cross(r, s);
         if (rxs == 0) {
            goto fail;
         }

         var qmp = q - p;
         var t = (cDouble)Cross(qmp, s) / (cDouble)rxs;

         var u = (cDouble)Cross(qmp, r) / (cDouble)rxs;
         if (u < CDoubleMath.c0 || u > CDoubleMath.c1) {
            goto fail;
         }

         tForRay = t;
         return true;

         fail:
         tForRay = default;
         return false;
      }

      public static DoubleVector2 PointAtX(this DoubleLineSegment2 seg, double x) {
         var dx = seg.X2 - seg.X1;
         var dy = seg.Y2 - seg.Y1;
         return new DoubleVector2(x, seg.Y1 + (x - seg.X1) * dy / dx);
      }

      public static DoubleVector2 PointAtY(this DoubleLineSegment2 seg, double y) {
         var dx = seg.X2 - seg.X1;
         var dy = seg.Y2 - seg.Y1;
         return new DoubleVector2(seg.X1 + (y - seg.Y1) * dx / dy, y);
      }

      public static bool TryIntersect(this Triangulation triangulation, cDouble x, cDouble y, out TriangulationIsland island, out int triangleIndex) {
         foreach (var candidateIsland in triangulation.Islands) {
            if (candidateIsland.TryIntersect(x, y, out triangleIndex)) {
               island = candidateIsland;
               return true;
            }
         }
         island = null;
         triangleIndex = -1;
         return false;
      }

      public static bool TryIntersect(this TriangulationIsland island, cDouble x, cDouble y, out int triangleIndex) {
         if (x < (cDouble)island.IntBounds.Left || y < (cDouble)island.IntBounds.Top ||
             x > (cDouble)island.IntBounds.Right || y > (cDouble)island.IntBounds.Bottom) {
            triangleIndex = -1;
            return false;
         }
#if use_fixed
         var p = new DoubleVector2(x, y);
#endif

         cDouble bestNearness = cDouble.MaxValue;
         cDouble kAcceptableNearness = CDoubleMath.c0_1;
         triangleIndex = -1;
         for (var i = 0; i < island.Triangles.Length; i++) {
#if use_fixed
            // It's faster to check AABB before doing the full PIP math in fixed-point arithmetic.
            if (!island.FixedOptimizationTriangleBounds[i].Contains(ref p)) continue;
#endif

            if (IsPointInTriangleWithNearness(x, y, ref island.Triangles[i], out var nearness)) {
               triangleIndex = i;
               return true;
            } else if (nearness < bestNearness && nearness <= kAcceptableNearness) {
               triangleIndex = i;
               bestNearness = nearness;
            }
         }
         return triangleIndex != -1;
      }

      public static bool SegmentIntersectsConvexPolygonInterior(IntLineSegment2 s, IntVector2[] p) {
         if (p.Length == 1) {
            return false;
         } else if (p.Length == 2) {
            return s.Intersects(new IntLineSegment2(p[0], p[1]));
         } else {
            return SegmentIntersectsNonDegenerateConvexPolygonInterior(s, p);
         }
      }

      // assumes p is ccw ordered
      public static bool ConvexPolygonContainsPoint(IntVector2 x, IntVector2[] p) {
#if DEBUG
         if (Clockness(p[0], p[1], p[2]) == Clk.Clockwise) throw new BadInputException("p not ccw");
         if (p.Length < 3) throw new BadInputException("len(p) < 3");
#endif

         for (var i = 0; i < p.Length; i++) {
            var a = p[i == 0 ? p.Length - 1 : i - 1];
            var b = p[i];
            if (Clockness(a, b, x) == Clk.Clockwise) return false;
         }
         return true;
      }

      // assumes p is ccw ordered, edge is counted as interior (neither case)
      public static bool SegmentIntersectsNonDegenerateConvexPolygonInterior(IntLineSegment2 s, IntVector2[] p) {
#if DEBUG
         if (Clockness(p[0], p[1], p[2]) == Clk.Clockwise) throw new BadInputException("p not ccw");
         if (p.Length < 3) throw new BadInputException("len(p) < 3");
#endif
         var (x, y) = s;
         bool xInterior = true, yInterior = true;
         IntVector2 a = p[p.Length - 1], b;
         int i = 0;
         for (; i < p.Length && (xInterior || yInterior); i++, a = b) {
            b = p[i];
            var abx = Clockness(a, b, x);
            var aby = Clockness(a, b, y);
            if (abx == Clk.Clockwise && aby == Clk.Clockwise) return false;
            xInterior &= abx != Clk.Clockwise;
            yInterior &= aby != Clk.Clockwise;
            if (abx == (Clk)(-(int)aby) || abx == Clk.Neither || aby == Clk.Neither) {
               // The below is equivalent to: 
               // // (a, b) places x, y onto opposite half-planes.
               // // Intersect if (x, y) places a, b onto opposite half-planes.
               // var xya = Clockness(x, y, a);
               // var xyb = Clockness(x, y, b);
               // if (xya != xyb || xya == Clk.Neither || xyb == Clk.Neither) return true;
               if (IntLineSegment2.Intersects(a.X, a.Y, b.X, b.Y, x.X, x.Y, y.X, y.Y)) {
                  return true;
               }
            }
         }
         for (; i < p.Length; i++, a = b) {
            b = p[i];
            if (IntLineSegment2.Intersects(a.X, a.Y, b.X, b.Y, x.X, x.Y, y.X, y.Y)) {
               return true;
            }
         }
         return xInterior && yInterior;
      }


      public static bool SegmentIntersectsConvexPolygonInterior(DoubleLineSegment2 s, DoubleVector2[] p) {
         if (p.Length == 1) {
            return false;
         } else if (p.Length == 2) {
            return s.Intersects(new DoubleLineSegment2(p[0], p[1]));
         } else {
            return SegmentIntersectsNonDegenerateConvexPolygonInterior(s, p);
         }
      }

      // assumes p is ccw ordered
      public static bool ConvexPolygonContainsPoint(DoubleVector2 x, DoubleVector2[] p) {
#if DEBUG
         if (Clockness(p[0], p[1], p[2]) == Clk.Clockwise) throw new BadInputException("p not ccw");
         if (p.Length < 3) throw new BadInputException("len(p) < 3");
#endif

         for (var i = 0; i < p.Length; i++) {
            var a = p[i == 0 ? p.Length - 1 : i - 1];
            var b = p[i];
            if (Clockness(a, b, x) == Clk.Clockwise) return false;
         }
         return true;
      }

      // assumes p is ccw ordered, edge is counted as interior (neither case)
      public static bool SegmentIntersectsNonDegenerateConvexPolygonInterior(DoubleLineSegment2 s, DoubleVector2[] p) {
#if DEBUG
         if (Clockness(p[0], p[1], p[2]) == Clk.Clockwise) throw new BadInputException("p not ccw");
         if (p.Length < 3) throw new BadInputException("len(p) < 3");
#endif
         var (x, y) = s;
         bool xInterior = true, yInterior = true;
         DoubleVector2 a = p[p.Length - 1], b;
         int i = 0;
         for (; i < p.Length && (xInterior || yInterior); i++, a = b) {
            b = p[i];
            var abx = Clockness(a, b, x);
            var aby = Clockness(a, b, y);
            if (abx == Clk.Clockwise && aby == Clk.Clockwise) return false;
            xInterior &= abx != Clk.Clockwise;
            yInterior &= aby != Clk.Clockwise;
            if (abx == (Clk)(-(int)aby) || abx == Clk.Neither || aby == Clk.Neither) {
               // The below is equivalent to: 
               // // (a, b) places x, y onto opposite half-planes.
               // // Intersect if (x, y) places a, b onto opposite half-planes.
               // var xya = Clockness(x, y, a);
               // var xyb = Clockness(x, y, b);
               // if (xya != xyb || xya == Clk.Neither || xyb == Clk.Neither) return true;
               if (DoubleLineSegment2.Intersects(a.X, a.Y, b.X, b.Y, x.X, x.Y, y.X, y.Y)) {
                  return true;
               }
            }
         }
         for (; i < p.Length; i++, a = b) {
            b = p[i];
            if (DoubleLineSegment2.Intersects(a.X, a.Y, b.X, b.Y, x.X, x.Y, y.X, y.Y)) {
               return true;
            }
         }
         return xInterior && yInterior;
      }

      public static bool IsPointInTriangle(cDouble px, cDouble py, ref Triangle3 triangle) {
         // Barycentric coordinates for PIP w/ triangle test http://blackpawn.com/texts/pointinpoly/

         var ax = triangle.Points.A.X; // bounded 2^14
         var ay = triangle.Points.A.Y;
         var bx = triangle.Points.B.X;
         var by = triangle.Points.B.Y;
         var cx = triangle.Points.C.X;
         var cy = triangle.Points.C.Y;

         var v0x = cx - ax; // bounded 2^15
         var v0y = cy - ay;
         var v1x = bx - ax;
         var v1y = by - ay;
         var v2x = px - ax;
         var v2y = py - ay;

         var dot00 = v0x * v0x + v0y * v0y; // bounded 2^31
         var dot01 = v0x * v1x + v0y * v1y;
         var dot02 = v0x * v2x + v0y * v2y;
         var dot11 = v1x * v1x + v1y * v1y;
         var dot12 = v1x * v2x + v1y * v2y;

#if !use_fixed
         var invDenom = CDoubleMath.c1 / (dot00 * dot11 - dot01 * dot01);
         var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
         var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
#else
         // in OpenMOBA, above dots can each reach INT32_MAX/MIN. 
         // This results in saturating overflow in the below divisor.
         // The workaround is to multiply numerator/denominator vs small c
         var c = (cDouble)1 / (cDouble)(1 << 16);
         var invDenom = (cDouble)1 / ((dot00 * c) * (dot11 * c) - (dot01 * c) * (dot01 * c));
         var u = ((dot11 * c) * (dot02 * c) - (dot01 * c) * (dot12 * c)) * invDenom;
         var v = ((dot00 * c) * (dot12 * c) - (dot01 * c) * (dot02 * c)) * invDenom;
#endif
         var uPlusV = u + v;

#if use_fixed
         u = cDouble.Round(u, 24);
         v = cDouble.Round(v, 24);
         uPlusV = cDouble.Round(uPlusV, 24);
         return (u >= CDoubleMath.c0) && (v >= CDoubleMath.c0) && (uPlusV <= CDoubleMath.c1);
#else
         // Todo: If epsilon determines outcome (rare), report that and continue PIP
         // comparisons (if additional ones exist), then pick the best positive.
         const double epsilon = kPointInTriangleEpsilon;
         return (u >= -epsilon) && (v >= -epsilon) && (uPlusV <= CDoubleMath.c1 + epsilon);
#endif
      }

      public static bool IsPointInTriangleWithNearness(cDouble px, cDouble py, ref Triangle3 triangle, out cDouble nearness) {
         // Barycentric coordinates for PIP w/ triangle test http://blackpawn.com/texts/pointinpoly/

         var ax = triangle.Points.A.X; // bounded 2^14
         var ay = triangle.Points.A.Y;
         var bx = triangle.Points.B.X;
         var by = triangle.Points.B.Y;
         var cx = triangle.Points.C.X;
         var cy = triangle.Points.C.Y;

         var v0x = cx - ax; // bounded 2^15
         var v0y = cy - ay;
         var v1x = bx - ax;
         var v1y = by - ay;
         var v2x = px - ax;
         var v2y = py - ay;

         var dot00 = v0x * v0x + v0y * v0y; // bounded 2^31
         var dot01 = v0x * v1x + v0y * v1y;
         var dot02 = v0x * v2x + v0y * v2y;
         var dot11 = v1x * v1x + v1y * v1y;
         var dot12 = v1x * v2x + v1y * v2y;

#if !use_fixed
         var invDenom = CDoubleMath.c1 / (dot00 * dot11 - dot01 * dot01);
         var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
         var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
#else
         // in OpenMOBA, above dots can each reach INT32_MAX/MIN. 
         // This results in saturating overflow in the below divisor.
         // The workaround is to multiply numerator/denominator vs small c
         var c = (cDouble)1 / (cDouble)(1 << 16);
         var invDenom = (cDouble)1 / ((dot00 * c) * (dot11 * c) - (dot01 * c) * (dot01 * c));
         var u = ((dot11 * c) * (dot02 * c) - (dot01 * c) * (dot12 * c)) * invDenom;
         var v = ((dot00 * c) * (dot12 * c) - (dot01 * c) * (dot02 * c)) * invDenom;
#endif
         var uPlusV = u + v;

#if use_fixed
throw new NotImplementedException();
         u = cDouble.Round(u, 24);
         v = cDouble.Round(v, 24);
         uPlusV = cDouble.Round(uPlusV, 24);
         return (u >= CDoubleMath.c0) && (v >= CDoubleMath.c0) && (uPlusV <= CDoubleMath.c1);
#else
         // Todo: If epsilon determines outcome (rare), report that and continue PIP
         // comparisons (if additional ones exist), then pick the best positive.
         const double epsilon = kPointInTriangleEpsilon;
         if ((u >= 0) && (v >= 0) && (uPlusV <= CDoubleMath.c1)) {
            nearness = 0.0;
            return true;
         } else {
            nearness = 0.0;
            if (u < 0) nearness -= u;
            if (v < 0) nearness -= v;
            if (uPlusV > 1) nearness += uPlusV - 1;
            return false;
         }
#endif
      }

      public static bool TryIntersectRayWithContainedOriginForVertexIndexOpposingEdge(DoubleVector2 origin, DoubleVector2 direction, in Triangle3 triangle, out int indexOpposingEdge, int skippedEdge = -1) {
         // See my explanation on http://math.stackexchange.com/questions/2139740/fast-3d-algorithm-to-find-a-ray-triangle-edge-intersection/2197942#2197942
         // Note: Triangle points (A = p1, B = p2, C = p3) are CCW, origin is p, direction is v.
         // Results are undefined if ray origin is not in triangle (though you can probably math out what it means).
         // If a point is on the edge of the triangle, there will be neither-neither for clockness on the correct edge.
         var skippedEdgeFirstIndex = skippedEdge == -1 ? -1 : (skippedEdge + 1) % 3;
         for (int i = 0; i < 3; i++) {
            if (i == skippedEdgeFirstIndex) continue;

            var va = triangle.Points[i] - origin;
            var vb = triangle.Points[(i + 1) % 3] - origin;
            var cvad = Clockness(va, direction);
            var cdvb = Clockness(direction, vb);

            // In-triangle case
            if (cvad != Geometry.Clockness.Clockwise &&
                cdvb != Geometry.Clockness.Clockwise) {
               indexOpposingEdge = (i + 2) % 3;
               return true;
            }
         }
         indexOpposingEdge = -1;
         return false;
         //         throw new ArgumentException("Presumably origin wasn't in triangle (is this case reachable even with malformed input?)");
      }

      public static ContourNearestPointResult3 FindNearestPointXYZOnContour(List<IntVector3> contour, DoubleVector3 query) {
         var result = new ContourNearestPointResult3 {
            Distance = cDouble.MaxValue,
            Query = query
         };
         var pointCount = contour.First().Equals(contour.Last()) ? contour.Count - 1 : contour.Count;
         for (int i = 0; i < pointCount; i++) {
            var p1 = contour[i].ToDoubleVector3();
            var p2 = contour[(i + 1) % pointCount].ToDoubleVector3();
            var nearestPoint = FindNearestPointXYZ(p1, p2, query);
            var distance = (query - nearestPoint).Norm2D();
            if (distance < result.Distance) {
               result.Distance = distance;
               result.SegmentFirstPointContourIndex = i;
               result.NearestPoint = nearestPoint;
            }
         }
         return result;
      }

      public static DoubleVector3 FindNearestPointXYZ(DoubleVector3 p1, DoubleVector3 p2, DoubleVector3 query) {
         var p1p2 = p2 - p1;
         var p1Query = query - p1;
         var p1QueryProjP1P2Component = p1Query.ProjectOntoComponentD(p1p2);
         if (p1QueryProjP1P2Component <= CDoubleMath.c0) {
            return p1;
         } else if (p1QueryProjP1P2Component >= CDoubleMath.c1) {
            return p2;
         } else {
            return p1 + p1QueryProjP1P2Component * p1p2;
         }
      }

      public static ContourNearestPointResult2 FindNearestPointOnContour(List<IntVector2> contour, DoubleVector2 query) {
         var result = new ContourNearestPointResult2 {
            Distance = cDouble.MaxValue,
            Query = query
         };
         var pointCount = contour.First().Equals(contour.Last()) ? contour.Count - 1 : contour.Count;
         for (int i = 0; i < pointCount; i++) {
            var p1 = contour[i].ToDoubleVector2();
            var p2 = contour[(i + 1) % pointCount].ToDoubleVector2();
            var nearestPoint = FindNearestPoint(p1, p2, query);
            var distance = (query - nearestPoint).Norm2D();
            if (distance < result.Distance) {
               result.Distance = distance;
               result.SegmentFirstPointContourIndex = i;
               result.NearestPoint = nearestPoint;
            }
         }
         return result;
      }

      public static DoubleVector2 FindNearestPoint(DoubleVector2 p1, DoubleVector2 p2, DoubleVector2 query) {
         var p1p2 = p2 - p1;
         var p1Query = query - p1;
         var p1QueryProjP1P2Component = p1Query.ProjectOntoComponentD(p1p2);
         if (p1QueryProjP1P2Component <= CDoubleMath.c0) {
            return p1;
         } else if (p1QueryProjP1P2Component >= CDoubleMath.c1) {
            return p2;
         } else {
            return p1 + p1QueryProjP1P2Component * p1p2;
         }
      }

      public static DoubleVector2 FindNearestPoint(IntLineSegment2 segment, DoubleVector2 query) {
         var p1 = segment.First.ToDoubleVector2();
         var p2 = segment.Second.ToDoubleVector2();
         return FindNearestPoint(p1, p2, query);
      }

      public static DoubleVector2 FindNearestPoint(DoubleLineSegment2 segment, DoubleVector2 query) {
         return FindNearestPoint(segment.First, segment.Second, query);
      }

      public static bool TryErode(this IntLineSegment2 segment, int erosionRadius, out IntLineSegment2 result) {
         var a = segment.First;
         var b = segment.Second;
         var aToB = a.To(b);
         var aToBMagSquared = aToB.SquaredNorm2();

         var erosionDiameter = 2 * erosionRadius;
         if (aToBMagSquared <= erosionDiameter * erosionDiameter) {
            result = default(IntLineSegment2);
            return false;
         }

         var aToBMag = Math.Sqrt(aToBMagSquared);
         var shrink = aToB.LossyScale(erosionRadius / aToBMag);
         result = new IntLineSegment2(a + shrink, b - shrink);
         return true;
      }

      public static IntLineSegment2 Dilate(this IntLineSegment2 segment, int dilationRadius) {
         var a = segment.First;
         var b = segment.Second;
         var aToB = a.To(b);
         var aToBMagSquared = aToB.SquaredNorm2();

         var aToBMag = Math.Sqrt(aToBMagSquared);
         var shrink = aToB.LossyScale(dilationRadius / aToBMag);
         return new IntLineSegment2(a - shrink, b + shrink);
      }

      /// <summary>
      /// Will continue gracefully even if R3 basis can be formed. Beware!
      /// </summary>
      public static bool ComputePlaneBasis(IReadOnlyList<IntVector3> points, out IntVector3 b1, out IntVector3 b2) {
         for (int i = 1; i < points.Count; i++) {
            b1 = points[0].To(points[i]);
            if (b1 != IntVector3.Zero) {
               for (; i < points.Count; i++) {
                  var next = i + 1 == points.Count ? points[0] : points[i + 1];
                  b2 = points[i].To(next);
                  if (b2 != IntVector3.Zero && b2.Cross(b1) != IntVector3.Zero) {
                     return true;
                  }
               }
            }
         }

         b1 = b2 = default(IntVector3);
         return false;
      }

      private static Comparer<IntVector2> xThenYComparer = Comparer<IntVector2>.Create(
         (a, b) => {
            var r = a.X.CompareTo(b.X);
            return r != 0 ? r : a.Y.CompareTo(b.Y);
         });

      /// <summary>
      /// Implementation of Convex Hull using the Monotone Chain Convex Hull Algorithm.
      /// The algorithm was chosen for implementation due to its simplicity. Chan's
      /// Convex Hull algorithm is a tad more efficient when the output point set
      /// is smaller than the input set.
      /// 
      /// based on http://en.wikibooks.org/wiki/Algorithm_Implementation/Geometry/Convex_hull/Monotone_chain
      /// </summary>
      /// <param name="input"></param>
      /// <returns>
      /// The convex hull of the points in counterclockwise order (starting from the
      /// rightmost point)
      /// </returns>
      public static unsafe IntVector2[] ConvexHull(IntVector2[] input) {
         // sort input by x, then y
         Array.Sort(input, xThenYComparer);

         // Compute one side of hull
         var lower = stackalloc IntVector2[input.Length];
         var lowerLength = 0;
         for (var i = 0; i < input.Length; i++) {
            while (lowerLength >= 2 && Clockness(lower[lowerLength - 2], lower[lowerLength - 1], input[i]) != Geometry.Clockness.CounterClockwise) {
               lowerLength--;
            }
            lower[lowerLength] = input[i];
            lowerLength++;
         }
         var x = sizeof(IntVector2);

         // rm last point, which is first of other hull
         lowerLength--;

         // Compute other side of hull
         var upper = stackalloc IntVector2[input.Length];
         var upperLength = 0;
         for (var i = input.Length - 1; i >= 0; i--) {
            while (upperLength >= 2 && Clockness(upper[upperLength - 2], upper[upperLength - 1], input[i]) != Geometry.Clockness.CounterClockwise) {
               upperLength--;
            }
            upper[upperLength] = input[i];
            upperLength++;
         }

         // rm last point, which is first of other hull
         upperLength--;

         var res = new IntVector2[lowerLength + upperLength];
         fixed (IntVector2* pRes = res) {
            var lowerByteCount = lowerLength * IntVector2.Size;
            var upperByteCount = upperLength * IntVector2.Size;
            Buffer.MemoryCopy(lower, pRes, lowerByteCount + upperByteCount, lowerByteCount);
            Buffer.MemoryCopy(upper, (byte*)pRes + lowerByteCount, upperByteCount, upperByteCount);
         }
         return res;
      }

      public static IntVector2[] ConvexHull3(IntVector2 a, IntVector2 b, IntVector2 c) {
         var abc = Clockness(a, b, c);
         if (abc == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, c);
            return s == t ? new[] { s } : new[] { s, t };
         }
         if (abc == Clk.Clockwise) {
            return new[] { c, b, a };
         }
         return new[] { a, b, c };
      }

      public static (IntVector2, IntVector2) FindCollinearBounds(IntVector2 a, IntVector2 b, IntVector2 c) {
         var ab = a.To(b).SquaredNorm2();
         var ac = a.To(c).SquaredNorm2();
         var bc = b.To(c).SquaredNorm2();
         if (ab > ac) {
            return ab > bc ? (a, b) : (b, c);
         } else {
            return ac > bc ? (a, c) : (b, c);
         }
      }

      // See https://stackoverflow.com/questions/2122305/convex-hull-of-4-points
      public static IntVector2[] ConvexHull4(IntVector2 a, IntVector2 b, IntVector2 c, IntVector2 d) {
         var abc = Clockness(a, b, c);

         if (abc == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, c);
            return ConvexHull3(s, t, d);
         }

         // make abc ccw
         if (abc == Clk.Clockwise) (a, c) = (c, a);

         var abd = Clockness(a, b, d);
         var bcd = Clockness(b, c, d);
         var cad = Clockness(c, a, d);

         if (abd == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, d);
            return ConvexHull3(s, t, c);
         }

         if (bcd == Clk.Neither) {
            var (s, t) = FindCollinearBounds(b, c, d);
            return ConvexHull3(s, t, a);
         }

         if (cad == Clk.Neither) {
            var (s, t) = FindCollinearBounds(c, a, d);
            return ConvexHull3(s, t, b);
         }

         if (abd == Clk.CounterClockwise) {
            if (bcd == Clk.CounterClockwise && cad == Clk.CounterClockwise) return new[] { a, b, c };
            if (bcd == Clk.CounterClockwise && cad == Clk.Clockwise) return new[] { a, b, c, d };
            if (bcd == Clk.Clockwise && cad == Clk.CounterClockwise) return new[] { a, b, d, c };
            if (bcd == Clk.Clockwise && cad == Clk.Clockwise) return new[] { a, b, d };
            throw new InvalidStateException();
         } else {
            if (bcd == Clk.CounterClockwise && cad == Clk.CounterClockwise) return new[] { a, d, b, c };
            if (bcd == Clk.CounterClockwise && cad == Clk.Clockwise) return new[] { d, b, c };
            if (bcd == Clk.Clockwise && cad == Clk.CounterClockwise) return new[] { a, d, c };
            // 4th state impossible
            throw new InvalidStateException();
         }
      }


      public static DoubleVector2[] ConvexHull3(DoubleVector2 a, DoubleVector2 b, DoubleVector2 c) {
         var abc = Clockness(a, b, c);
         if (abc == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, c);
            return s == t ? new[] { s } : new[] { s, t };
         }
         if (abc == Clk.Clockwise) {
            return new[] { c, b, a };
         }
         return new[] { a, b, c };
      }

      public static (DoubleVector2, DoubleVector2) FindCollinearBounds(DoubleVector2 a, DoubleVector2 b, DoubleVector2 c) {
         var ab = a.To(b).SquaredNorm2D();
         var ac = a.To(c).SquaredNorm2D();
         var bc = b.To(c).SquaredNorm2D();
         if (ab > ac) {
            return ab > bc ? (a, b) : (b, c);
         } else {
            return ac > bc ? (a, c) : (b, c);
         }
      }

      // See https://stackoverflow.com/questions/2122305/convex-hull-of-4-points
      public static DoubleVector2[] ConvexHull4(DoubleVector2 a, DoubleVector2 b, DoubleVector2 c, DoubleVector2 d) {
         var abc = Clockness(a, b, c);

         if (abc == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, c);
            return ConvexHull3(s, t, d);
         }

         // make abc ccw
         if (abc == Clk.Clockwise) (a, c) = (c, a);

         var abd = Clockness(a, b, d);
         var bcd = Clockness(b, c, d);
         var cad = Clockness(c, a, d);

         if (abd == Clk.Neither) {
            var (s, t) = FindCollinearBounds(a, b, d);
            return ConvexHull3(s, t, c);
         }

         if (bcd == Clk.Neither) {
            var (s, t) = FindCollinearBounds(b, c, d);
            return ConvexHull3(s, t, a);
         }

         if (cad == Clk.Neither) {
            var (s, t) = FindCollinearBounds(c, a, d);
            return ConvexHull3(s, t, b);
         }

         if (abd == Clk.CounterClockwise) {
            if (bcd == Clk.CounterClockwise && cad == Clk.CounterClockwise) return new[] { a, b, c };
            if (bcd == Clk.CounterClockwise && cad == Clk.Clockwise) return new[] { a, b, c, d };
            if (bcd == Clk.Clockwise && cad == Clk.CounterClockwise) return new[] { a, b, d, c };
            if (bcd == Clk.Clockwise && cad == Clk.Clockwise) return new[] { a, b, d };
            throw new InvalidStateException();
         } else {
            if (bcd == Clk.CounterClockwise && cad == Clk.CounterClockwise) return new[] { a, d, b, c };
            if (bcd == Clk.CounterClockwise && cad == Clk.Clockwise) return new[] { d, b, c };
            if (bcd == Clk.Clockwise && cad == Clk.CounterClockwise) return new[] { a, d, c };
            // 4th state impossible
            throw new InvalidStateException();
         }
      }
   }
}
