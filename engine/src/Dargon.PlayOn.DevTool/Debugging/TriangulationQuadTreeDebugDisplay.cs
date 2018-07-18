﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Dargon.PlayOn.DataStructures;

namespace Dargon.PlayOn.DevTool.Debugging {
   public static class TriangulationQuadTreeDebugDisplay {
      public static void DrawTriangulationQuadTree(this IDebugCanvas debugCanvas, Triangulation triangulation) {
         foreach (var island in triangulation.Islands) {
            var s = new Stack<Tuple<int, QuadTree<int>.Node>>();
            s.Push(Tuple.Create(0, island.TriangleIndexQuadTree.Root));
            while (s.Any()) {
               var tuple = s.Pop();
               var depth = tuple.Item1;
               var node = tuple.Item2;
               debugCanvas.DrawRectangle(node.Rect, 0.0f, new StrokeStyle(Color.Black));
               if (node.TopLeft != null) {
                  s.Push(Tuple.Create(depth + 1, node.TopLeft));
                  s.Push(Tuple.Create(depth + 1, node.TopRight));
                  s.Push(Tuple.Create(depth + 1, node.BottomLeft));
                  s.Push(Tuple.Create(depth + 1, node.BottomRight));
               }
            }
         }
      }
   }
}
