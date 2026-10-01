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
    /// ⚠️ alphaMode is "premultiplied", NOT the default "opaque": on the same machine drawImage() from an
    /// "opaque"-configured WebGPU canvas returned fully transparent pixels even when the texture held the
    /// correct data. The shader forces alpha = 1, which is visually identical to "opaque".
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

            // Flush any pending kernel dispatches before this buffer is read by the render pass.
            _accelerator.FlushPendingCommands();

            if (width != _lastWidth || height != _lastHeight)
            {
                _lastWidth = width;
                _lastHeight = height;
                _internalCanvas.Width = (int)width;
                _internalCanvas.Height = (int)height;
                _canvasCtx.Configure(new GPUCanvasConfiguration
                {
                    Device = Device,
                    Format = CanvasFormat,
                    Usage = (uint)GPUTextureUsage.StorageBinding,
                    AlphaMode = "premultiplied",
                });
            }

            using var currentTexture = _canvasCtx.GetCurrentTexture();
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
            _displayCtx.DrawImage(_internalCanvas);

            return Task.CompletedTask;
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
