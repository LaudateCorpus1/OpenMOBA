﻿using System;
using System.Drawing;

namespace Canvas3D.LowLevel {
   public interface IGraphicsDevice : IDisposable {
      IImmediateDeviceContext ImmediateContext { get; }

      void DoEvents();

      IDeferredDeviceContext CreateDeferredRenderContext();

      IBuffer<T> CreateConstantBuffer<T>(int count) where T : struct;
      IBuffer<T> CreateVertexBuffer<T>(int count) where T : struct;
      IBuffer<T> CreateVertexBuffer<T>(T[] content) where T : struct;
      IBuffer<T> CreateIndexBuffer<T>(int count) where T : struct;
      IBuffer<T> CreateIndexBuffer<T>(T[] content) where T : struct;
      (IBuffer<T>, IShaderResourceView) CreateStructuredBufferAndView<T>(int count) where T : struct;

      (IRenderTargetView[], IShaderResourceView, IShaderResourceView[]) CreateScreenSizeRenderTarget(int levels);
      (ITexture2D, IRenderTargetView[], IShaderResourceView, IShaderResourceView[]) CreateRenderTarget(int levels, Size resolution);
      (IDepthStencilView, IShaderResourceView) CreateScreenSizeDepthTarget();
      (ITexture2D, IDepthStencilView[], IShaderResourceView, IShaderResourceView[]) CreateDepthTextureAndViews(int levels, Size resolution);
   }
}
