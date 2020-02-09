﻿using System;
using System.Numerics;

namespace Dargon.Dviz {
   public interface IDebugCanvas {
      Matrix4x4 Transform { get; set; }
      void BatchDraw(Action callback);

      void DrawPoint(Vector3 p, StrokeStyle strokeStyle);
      void DrawLine(Vector3 p1, Vector3 p2, StrokeStyle strokeStyle);
      void DrawTriangle(Vector3 p1, Vector3 p2, Vector3 p3, StrokeStyle strokeStyle);
      void FillTriangle(Vector3 p1, Vector3 p2, Vector3 p3, FillStyle fillStyle);
      void DrawText(string text, Vector3 point);
   }
}