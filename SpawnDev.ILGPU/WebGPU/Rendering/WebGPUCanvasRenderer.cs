using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.WebGPU.Rendering
{
    /// <summary>
    /// Zero-copy WebGPU canvas renderer. Presents an ILGPU pixel buffer directly to the canvas
    /// via a compute pass that writes the canvas texture as a storage texture, with no CPU readback.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately compute-only - NO render pipeline. MEASURED 2026-10-01 on an Intel HD 620 (gen-9,
    /// driver 23.20.16.4849 from 2017) in Chrome 154: every render-pipeline draw silently rasterized
    /// nothing (plain textures and canvas textures, rgba/bgra, with or without vertex buffers) - only the
    /// pass clear landed, and no validation error was raised - while compute dispatches were correct.
    /// The old fullscreen-triangle blit therefore produced a blank canvas while the kernel ran at full
    /// speed. A compute blit uses only the path ILGPU itself already depends on.
    /// ⚠️ alphaMode is "opaque", with a ONE-TIME check that falls back to "premultiplied". On the same HD 620,
    /// drawImage() from an "opaque"-configured WebGPU canvas returned fully transparent pixels even when the
    /// texture held the correct data; "premultiplied" worked. But "premultiplied" is not free elsewhere:
    /// MEASURED 2026-10-08 on an RTX 4070 / Chrome, drawImage() from a "premultiplied" WebGPU canvas BLOCKED the
    /// main thread ~3 ms per present at 900x700 and ~6-9 ms at 1920x1080, while "opaque" took ~0.01 ms (the
    /// compute pass, rgba8unorm and STORAGE_BINDING usage measured free). So the first present also draws the
    /// "opaque" canvas into a private 1x1 2D canvas (same drawImage call, default alpha) and reads that pixel:
    /// alpha 255 keeps "opaque", anything else switches this renderer to "premultiplied" and re-presents the
    /// frame. The caller's canvas is never cleared or read (a caller's 2D context with alpha:false would always
    /// read 255 and hide the bug). The shader forces alpha = 1 in both modes, so both look identical.
    /// </remarks>
    public sealed class WebGPUCanvasRenderer : ICanvasRenderer
    {
        private readonly WebGPUAccelerator _accelerator;
        private GPUDevice Device => _accelerator.NativeAccelerator.NativeDevice!;
        private GPUQueue Queue => _accelerator.NativeAccelerator.Queue!;

        private HTMLCanvasElement? _internalCanvas;
        private CanvasRenderingContext2D? _displayCtx;
        private GPUCanvasContext? _canvasCtx;
        private GPUComputePipeline? _pipeline;
        private GPUBindGroupLayout? _bindGroupLayout;

        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];
        private uint _lastWidth;
        private uint _lastHeight;
        // rgba8unorm is always a valid canvas format AND always storage-capable (bgra8unorm storage
        // needs the optional "bgra8unorm-storage" feature).
        private const string CanvasFormat = "rgba8unorm";
        private const uint WorkgroupSize = 8;
        private bool _disposed;
        // WORKAROUND: Intel HD 620 (2017 driver) drawImage() of an "opaque" WebGPU canvas reads back transparent.
        // Starts "opaque" (fast); the first present verifies it and may switch to "premultiplied" (see remarks).
        private string _alphaMode = "opaque";
        private bool _alphaModeChecked;

        /// <summary>The canvas alphaMode in use: "opaque", or "premultiplied" after the first-present check failed.</summary>
        internal string AlphaMode => _alphaMode;
        /// <summary>TEST SWITCH: make the first-present check see a transparent pixel, as the HD 620 does.</summary>
        internal static bool TestSimulateOpaqueReadsTransparent { get; set; }

        // WGSL: storage-buffer -> storage-texture blit.
        // Pixel buffer is packed RGBA little-endian uint32: R in bits 0–7, G 8–15, B 16–23, A 24–31,
        // which is exactly the component order unpack4x8unorm returns.
        private const string WgslSource = @"
@group(0) @binding(0) var<storage, read> pixels : array<u32>;
@group(0) @binding(1) var outTex : texture_storage_2d<rgba8unorm, write>;

@compute @workgroup_size(8, 8)
fn cs_main(@builtin(global_invocation_id) gid : vec3<u32>) {
    let dims = textureDimensions(outTex);
    if (gid.x >= dims.x || gid.y >= dims.y) {
        return;
    }
    let c = unpack4x8unorm(pixels[gid.y * dims.x + gid.x]);
    textureStore(outTex, vec2<i32>(gid.xy), vec4<f32>(c.rgb, 1.0));
}
";

        public WebGPUCanvasRenderer(WebGPUAccelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        public void AttachCanvas(HTMLCanvasElement canvas)
        {
            DisposeGpuResources();

            // Display canvas always uses 2d context — no context-type conflict when switching backends.
            _displayCtx = canvas.GetContext<CanvasRenderingContext2D>("2d");

            // Internal off-DOM canvas owns the webgpu context.
            _internalCanvas = new HTMLCanvasElement();
            _canvasCtx = _internalCanvas.GetContext<GPUCanvasContext>("webgpu")
                ?? throw new InvalidOperationException("Failed to get WebGPU canvas context.");

            BuildPipeline();
        }

        private void BuildPipeline()
        {
            _bindGroupLayout?.Dispose();
            _bindGroupLayout = Device.CreateBindGroupLayout(new GPUBindGroupLayoutDescriptor
            {
                Entries = new[]
                {
                    new GPUBindGroupLayoutEntry
                    {
                        Binding = 0,
                        Visibility = GPUShaderStageFlags.COMPUTE,
                        Buffer = new GPUBufferBindingLayout { Type = "read-only-storage" },
                    },
                    new GPUBindGroupLayoutEntry
                    {
                        Binding = 1,
                        Visibility = GPUShaderStageFlags.COMPUTE,
                        StorageTexture = new GPUStorageTextureBindingLayout
                        {
                            Format = CanvasFormat,
                            Access = "write-only",
                            ViewDimension = "2d",
                        },
                    },
                },
            });

            using var pipelineLayout = Device.CreatePipelineLayout(new GPUPipelineLayoutDescriptor
            {
                BindGroupLayouts = new[] { _bindGroupLayout },
            });

            using var shaderModule = Device.CreateShaderModule(new GPUShaderModuleDescriptor
            {
                Code = WgslSource,
            });

            _pipeline?.Dispose();
            _pipeline = Device.CreateComputePipeline(new GPUComputePipelineDescriptor
            {
                Layout = pipelineLayout,
                Compute = new GPUProgrammableStage
                {
                    Module = shaderModule,
                    EntryPoint = "cs_main",
                },
            });
        }

        public Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer)
            => PresentBufferAsync(((IArrayView)buffer).Buffer, (uint)buffer.Extent.X, (uint)buffer.Extent.Y);

        public Task PresentAsync(MemoryBuffer2D<int, Stride2D.DenseX> buffer)
            => PresentBufferAsync(((IArrayView)buffer).Buffer, (uint)buffer.Extent.X, (uint)buffer.Extent.Y);

        private Task PresentBufferAsync(MemoryBuffer memBuf, uint width, uint height)
        {
            if (_canvasCtx == null || _pipeline == null || _bindGroupLayout == null
                || _internalCanvas == null || _displayCtx == null)
                return Task.CompletedTask;

            var webGpuMemBuf = memBuf as WebGPUMemoryBuffer
                ?? throw new InvalidOperationException("Buffer is not backed by a WebGPUMemoryBuffer.");

            var gpuBuffer = webGpuMemBuf.NativeBuffer.NativeBuffer
                ?? throw new InvalidOperationException("Underlying GPUBuffer is null.");

            // Flush any pending kernel dispatches before this buffer is read by the blit.
            _accelerator.FlushPendingCommands();

            if (_alphaModeChecked)
            {
                PresentFrame(gpuBuffer, width, height);
                return Task.CompletedTask;
            }

            // First present: verify "opaque" survives drawImage() on this device (see remarks).
            _alphaModeChecked = true;
            PresentFrame(gpuBuffer, width, height);
            if (_alphaMode == "opaque" && !OpaqueReadBackWorks())
            {
                _alphaMode = "premultiplied";
                _lastWidth = 0; _lastHeight = 0;   // reconfigure the canvas with the new alphaMode
                PresentFrame(gpuBuffer, width, height);
            }
            return Task.CompletedTask;
        }

        private bool OpaqueReadBackWorks()
        {
            if (TestSimulateOpaqueReadsTransparent) return false;
            // One 1x1 draw + readback per renderer, on its first present only, into a private canvas.
            using var probe = new HTMLCanvasElement { Width = 1, Height = 1 };
            using var probeCtx = probe.GetContext<CanvasRenderingContext2D>("2d");
            if (probeCtx == null) return true;
            probeCtx.DrawImage(_internalCanvas!);
            using var imageData = probeCtx.GetImageData(0, 0, 1, 1);
            if (imageData == null) return true;
            using var data = imageData.Data;
            return data[3] == 255;
        }

        private void PresentFrame(GPUBuffer gpuBuffer, uint width, uint height)
        {
            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                _internalCanvas!.Width = (int)width;
                _internalCanvas.Height = (int)height;
                _canvasCtx!.Configure(new GPUCanvasConfiguration
                {
                    Device = Device,
                    Format = CanvasFormat,
                    Usage = (uint)GPUTextureUsage.StorageBinding,
                    AlphaMode = _alphaMode,
                });
            }

            using var currentTexture = _canvasCtx!.GetCurrentTexture();
            using var textureView = currentTexture.CreateView();

            using var bindGroup = Device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                Entries = new[]
                {
                    new GPUBindGroupEntry
                    {
                        Binding = 0,
                        Resource = new GPUBufferBinding { Buffer = gpuBuffer },
                    },
                    new GPUBindGroupEntry
                    {
                        Binding = 1,
                        Resource = textureView,
                    },
                },
            });

            using var encoder = Device.CreateCommandEncoder();
            using var pass = encoder.BeginComputePass();
            pass.SetPipeline(_pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.DispatchWorkgroups(
                (width + WorkgroupSize - 1) / WorkgroupSize,
                (height + WorkgroupSize - 1) / WorkgroupSize,
                1);
            pass.End();

            using var commandBuffer = encoder.Finish();
            _submitArray[0] = commandBuffer;
            Queue.Submit(_submitArray);

            // Blit internal WebGPU canvas to the display canvas via 2d context.
            _displayCtx!.DrawImage(_internalCanvas!);
        }

        private void DisposeGpuResources()
        {
            _pipeline?.Dispose(); _pipeline = null;
            _bindGroupLayout?.Dispose(); _bindGroupLayout = null;
            _canvasCtx?.Unconfigure(); _canvasCtx?.Dispose(); _canvasCtx = null;
            _internalCanvas?.Dispose(); _internalCanvas = null;
            _displayCtx?.Dispose(); _displayCtx = null;
            _lastWidth = 0; _lastHeight = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeGpuResources();
        }
    }
}
