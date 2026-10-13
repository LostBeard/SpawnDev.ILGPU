# Canvas Rendering & Image Input

SpawnDev.ILGPU ships two mirror-image browser APIs that keep pixels on the device:

- **`ICanvasRenderer`** presents an ILGPU pixel buffer to an HTML `<canvas>` (GPU buffer -> canvas).
- **`IExternalImageCopier`** copies a `<video>`, `<img>`, canvas, `ImageBitmap`, `VideoFrame` or `ImageData` into an ILGPU buffer (image -> GPU buffer). See [Image and Video Input](#image-and-video-input-iexternalimagecopier).

Both use the same pixel layout (packed RGBA8, R in the low byte), so a video frame can go source -> kernels -> canvas without leaving the device.

## Overview

```
ICanvasRenderer
├── WebGPUCanvasRenderer   - zero-copy fullscreen-triangle render pass, no CPU readback
├── WebGLCanvasRenderer    - ImageBitmap blit from the GL worker, draw inside callback
└── CPUCanvasRenderer      - reused ImageData object, fast Uint8Array copy (Wasm / desktop CPU)

IExternalImageCopier
├── WebGPUExternalImageCopier   - copyExternalImageToTexture + copyTextureToBuffer, GPU only
└── Canvas2DExternalImageCopier - 2D canvas + CopyFromJS, pixels stay JS-side (WebGL / Wasm)
```

`CanvasRendererFactory.Create(accelerator)` and `ExternalImageCopier.Create(accelerator)` return the right implementation automatically.

## Quick Start

```csharp
using SpawnDev.ILGPU.Rendering;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

// Create the best renderer for the active accelerator (call once)
ICanvasRenderer _renderer = CanvasRendererFactory.Create(accelerator);

// Attach to the canvas element (call once, or again when the canvas changes).
// JS is the injected SpawnJSRuntime; the canvas has id="myCanvas".
using var canvas = JS.Call<string, HTMLCanvasElement>("document.getElementById", "myCanvas");
_renderer.AttachCanvas(canvas);

// Each frame: run kernel → present
_kernel(_outputBuffer.IntExtent, _outputBuffer.View /*, ...args */);
await accelerator.SynchronizeAsync();
await _renderer.PresentAsync(_outputBuffer);
```

`PresentAsync` accepts both `MemoryBuffer2D<uint, Stride2D.DenseX>` and `MemoryBuffer2D<int, Stride2D.DenseX>`. Pixels are packed RGBA little-endian: R in bits 0–7, G 8–15, B 16–23, A 24–31.

---

## How Each Implementation Works

### WebGPU — `WebGPUCanvasRenderer`

No CPU readback at all. On every `PresentAsync` call:

1. `FlushPendingCommands()` ensures all queued kernel dispatches have been submitted.
2. A cached **fullscreen-triangle render pipeline** reads the pixel buffer directly from a `read-only-storage` binding.
3. A `GPURenderPass` rasterises a 3-vertex triangle that covers the entire viewport, with the fragment shader unpacking each `uint32` pixel into RGBA.
4. The result is blitted from an off-DOM internal canvas to the user-visible canvas via `CanvasRenderingContext2D.drawImage`.

The render pipeline and bind-group layout are built once in `AttachCanvas` and reused every frame. The uniform buffer (width/height) is only re-uploaded when the resolution changes.

```
kernel output buffer (GPUBuffer)
        │  storage read
        ▼
  fullscreen triangle renderpass   ← no CPU round-trip
        │
  internal WebGPU canvas
        │  drawImage (zero-copy GPU blit)
        ▼
  display canvas (2d context)
```

### WebGL — `WebGLCanvasRenderer`

The WebGL backend runs in a dedicated Web Worker. Blitting to a visible canvas therefore requires getting an `ImageBitmap` across the worker boundary. The implementation avoids a race where the browser could clear the canvas between the blit and the draw:

1. `BlitAndDrawAsync` posts a `blit` message to the GL worker.
2. The worker renders the texture to an offscreen framebuffer and calls `transferToImageBitmap()`.
3. The `ImageBitmap` is transferred back to the main thread.
4. **Synchronously inside the message-handler callback** — before any JS event-loop turn can run — `ctx.drawImage(bitmap)` paints the bitmap onto the canvas.
5. Only after the draw does `BlitAndDrawAsync` resolve its `Task`.

The synchronous draw is the critical detail. Without it, Blazor's render cycle can overwrite the canvas between frames.

```
kernel output buffer (WebGL texture in worker)
        │  texelFetch + offscreen FBO
        ▼
  worker: transferToImageBitmap()
        │  postMessage (structured clone, zero-copy)
        ▼
  main thread callback: ctx.drawImage(bitmap)   ← synchronous, in the handler
        │
  display canvas (2d context)
```

### Wasm — `CPUCanvasRenderer`

Used for any accelerator that is neither WebGPU nor WebGL (Wasm in the browser; CPU on desktop). It reuses a single `ImageData` object to avoid GC churn:

1. If the buffer is browser-backed (`IBrowserMemoryBuffer`) — true for Wasm buffers — it calls `CopyToHostUint8ArrayAsync` for a fast JS-side copy with no managed allocation.
2. Otherwise it falls back to synchronous `CopyToCPU` into a freshly allocated `uint[]` (only the destination `ImageData` / `Uint8ClampedArray` is reused across calls, not this staging array).
3. `ctx.putImageData` writes the `ImageData` to the canvas.

---

## Pixel Format

Kernels should pack pixels as `uint32` little-endian RGBA:

```csharp
static void PixelKernel(Index2D idx, ArrayView2D<uint, Stride2D.DenseX> output)
{
    byte r = /* red   0–255 */;
    byte g = /* green 0–255 */;
    byte b = /* blue  0–255 */;
    byte a = 255;

    // Little-endian: R in byte 0, A in byte 3
    output[idx] = (uint)((a << 24) | (b << 16) | (g << 8) | r);
}
```

The same packing works identically on all three renderer implementations.

---

## Complete Blazor Page Example

```razor
@page "/mypage"
@using global::ILGPU
@using global::ILGPU.Runtime
@using SpawnDev.ILGPU
@using SpawnDev.ILGPU.Rendering
@using SpawnDev.SpawnJS
@using SpawnDev.SpawnJS.JSObjects
@implements IAsyncDisposable
@inject SpawnJSRuntime JS

<canvas id="myCanvas" width="800" height="600" />

@code {
    private Context? _context;
    private Accelerator? _accelerator;
    private MemoryBuffer2D<uint, Stride2D.DenseX>? _output;
    private Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>>? _kernel;
    private ICanvasRenderer? _renderer;
    private bool _running;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        _context    = await Context.CreateAsync(b => b.AllAcceleratorsAsync());
        _accelerator = await _context.CreatePreferredAcceleratorAsync();
        _output     = _accelerator.Allocate2DDenseX<uint>(new Index2D(800, 600));
        _kernel     = _accelerator.LoadAutoGroupedStreamKernel<
                          Index2D, ArrayView2D<uint, Stride2D.DenseX>>(PixelKernel);

        _renderer = CanvasRendererFactory.Create(_accelerator);
        using var canvas = JS.Call<string, HTMLCanvasElement>("document.getElementById", "myCanvas");
        _renderer.AttachCanvas(canvas);

        _running = true;
        _ = RenderLoop();
    }

    private async Task RenderLoop()
    {
        while (_running)
        {
            _kernel!(_output!.IntExtent, _output.View);
            await _accelerator!.SynchronizeAsync();
            await _renderer!.PresentAsync(_output);
            await Task.Yield();  // yield to keep the browser responsive
        }
    }

    static void PixelKernel(Index2D idx, ArrayView2D<uint, Stride2D.DenseX> output)
    {
        byte r = (byte)(255 * idx.X / 800);
        byte g = (byte)(255 * idx.Y / 600);
        output[idx] = (uint)(0xFF000000u | ((uint)128 << 16) | ((uint)g << 8) | r);
    }

    public async ValueTask DisposeAsync()
    {
        _running = false;
        _renderer?.Dispose();
        _output?.Dispose();
        _accelerator?.Dispose();
        _context?.Dispose();
    }
}
```

---

## Performance Characteristics

| | WebGPU | WebGL | Wasm (+ desktop CPU) |
|---|---|---|---|
| **CPU readback** | ❌ None | ❌ None (ImageBitmap) | ✅ Required |
| **Extra allocations per frame** | Bind group only | None | None (cached ImageData) |
| **GPU stall** | None | None | Sync on CPU path |
| **Main-thread work** | `drawImage` | `drawImage` | `putImageData` |

The WebGPU and WebGL paths avoid all CPU ↔ GPU data transfers during rendering. The pixel data stays GPU-resident from kernel output to the canvas.

---

## Lifecycle

```csharp
// Create once alongside the accelerator
ICanvasRenderer renderer = CanvasRendererFactory.Create(accelerator);

// Attach whenever the canvas element is available/changes
renderer.AttachCanvas(canvas);

// Call every frame
await renderer.PresentAsync(buffer);

// Dispose alongside the accelerator
renderer.Dispose();
```

`AttachCanvas` can be called multiple times (e.g., when the Blazor component re-renders and the element changes). The previous context is disposed and replaced. The renderer keeps its own reference to the context, so disposing your `HTMLCanvasElement` wrapper after `AttachCanvas` (the `using var` above) is fine.

> With the [SpawnDev.SpawnJS.Blazor](https://github.com/LostBeard/SpawnDev.SpawnJS) package you can also capture the element with `@ref`: declare `ElementRef<HTMLCanvasElement> _canvas;`, put `@ref="_canvas"` on the `<canvas>`, and call `using var canvas = _canvas.Get();` after the first render.

---

## Image and Video Input: `IExternalImageCopier`

The input-side twin of `ICanvasRenderer` (5.2.25+). It copies the current pixels of a browser image source into an `ArrayView1D<int, Stride1D.Dense>` as packed RGBA8 - one `int` per pixel, R in the low byte, row-major, top row first. That is the layout `PresentAsync` presents, so a frame can go source -> kernels -> canvas without a CPU round trip.

```csharp
using SpawnDev.ILGPU.Rendering;
using SpawnDev.SpawnJS.JSObjects;

// Create once per accelerator and reuse across frames: it keeps its staging resources.
using var copier = ExternalImageCopier.Create(accelerator);
using var frame  = accelerator.Allocate1D<int>(width * height);

// Each frame (video is an HTMLVideoElement; use its natural size: VideoWidth / VideoHeight)
copier.CopyToBuffer(video, width, height, frame.View);
myKernel(frame.IntExtent, frame.View /*, ... */);
```

Sources: `HTMLVideoElement`, `HTMLImageElement`, `HTMLCanvasElement`, `OffscreenCanvas`, `ImageBitmap`, `VideoFrame`, `ImageData`. `CopyToBuffer` copies `width x height` pixels from the top-left of the source; the source must be at least that big and the destination must hold at least `width * height` elements.

| Backend | Implementation | Path |
|---|---|---|
| **WebGPU** | `WebGPUExternalImageCopier` | `queue.copyExternalImageToTexture` -> `copyTextureToBuffer`, entirely on the GPU. When `width * 4` is not a multiple of 256 (the `bytesPerRow` rule of `copyTextureToBuffer`) the rows land in a padded staging buffer and one kernel compacts them. Pending ILGPU dispatches are flushed first, because the copy runs on the queue timeline at call time. |
| **WebGL / Wasm** | `Canvas2DExternalImageCopier` | Draws the source into a 2D canvas and uploads its pixels JS-side with `IBrowserMemoryBuffer.CopyFromJS` (no trip through the .NET heap). On Wasm that is the zero-extra-copy path, since device memory is JS memory. |

Notes:
- **Firefox and video:** Firefox's `copyExternalImageToTexture` refuses `HTMLVideoElement` and `VideoFrame` sources (Chrome accepts them). The first refusal switches that copier to drawing such sources into a reused `OffscreenCanvas` and copying that: one exception, then none per frame. `WebGPUExternalImageCopier.FrameSourcesThroughCanvas` reports the switch; `ForceFrameSourcesThroughCanvas` forces it (tests).
- **Dispatch plans:** the WebGPU copier refuses to run while a dispatch plan is recording (a copy cannot be replayed). Copy the frame before the captured work.
- **Cross-origin sources:** a cross-origin image or video without CORS approval is *tainted*; the browser will not expose its pixels and the copy throws (a `SecurityError` on WebGPU). That is the browser's rule, not a backend limitation.

---

## Advanced: Manual Approach

If you need direct control over the blit pipeline - or need to support a buffer layout that does not fit `MemoryBuffer2D<uint>` - you can replicate what the renderers do:

**WebGPU:**

```csharp
// Flush batched dispatches (and batched small uploads) first. ILGPU records dispatches,
// GPU copies, clears and uploads up to 64 KiB into a pending batch; your own
// device.queue.submit would otherwise run BEFORE them.
webGpuAccelerator.FlushPendingCommands();

// Then execute your own render pass using NativeAccelerator.NativeDevice
var gpuBuffer = ((WebGPUMemoryBuffer)rawBuffer).NativeBuffer.NativeBuffer;
```

**WebGL:**

```csharp
// BlitAndDrawAsync posts to the worker and calls your callback synchronously
// with the ImageBitmap before resolving, keeping the draw in the same event-loop turn.
await webGlAccelerator.BlitAndDrawAsync(memBuf, width, height, bitmap =>
{
    ctx.DrawImage(bitmap);
    // Additional compositing can go here
});
```
