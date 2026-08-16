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
    /// via a fullscreen-triangle render pass with no CPU readback.
    /// </summary>
    public sealed class WebGPUCanvasRenderer : ICanvasRenderer
    {
        private readonly WebGPUAccelerator _accelerator;
        private GPUDevice Device => _accelerator.NativeAccelerator.NativeDevice!;
        private GPUQueue Queue => _accelerator.NativeAccelerator.Queue!;

        private HTMLCanvasElement? _internalCanvas;
        private CanvasRenderingContext2D? _displayCtx;
        private GPUCanvasContext? _canvasCtx;
        private GPURenderPipeline? _pipeline;
        private GPUBindGroupLayout? _bindGroupLayout;
        private GPUBuffer? _uniformBuffer;

        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];
        private uint _lastWidth;
        private uint _lastHeight;
        private string _canvasFormat = "bgra8unorm";
        private bool _disposed;

        // WGSL: fullscreen triangle vertex shader + storage-buffer blit fragment shader.
        // Pixel buffer is packed RGBA little-endian uint32: R in bits 0–7, G 8–15, B 16–23, A 24–31.
        private const string WgslSource = @"
struct Uniforms {
    width  : u32,
    height : u32,
}

@group(0) @binding(0) var<storage, read> pixels  : array<u32>;
@group(0) @binding(1) var<uniform>       uniforms : Uniforms;

@vertex
fn vs_main(@builtin(vertex_index) vi : u32) -> @builtin(position) vec4<f32> {
    var pos = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>( 3.0, -1.0),
        vec2<f32>(-1.0,  3.0)
    );
    return vec4<f32>(pos[vi], 0.0, 1.0);
}

@fragment
fn fs_main(@builtin(position) pos : vec4<f32>) -> @location(0) vec4<f32> {
    let x = u32(pos.x);
    let y = u32(pos.y);
    let packed = pixels[y * uniforms.width + x];
    let r = f32( packed        & 0xFFu) / 255.0;
    let g = f32((packed >>  8u) & 0xFFu) / 255.0;
    let b = f32((packed >> 16u) & 0xFFu) / 255.0;
    let a = f32((packed >> 24u) & 0xFFu) / 255.0;
    return vec4<f32>(r, g, b, a);
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

            using var navigator = SpawnJSRuntime.Instance.Get<Navigator>("navigator");
            using var gpu = navigator.Gpu;
            _canvasFormat = gpu?.GetPreferredCanvasFormat() ?? "bgra8unorm";

            // Internal off-DOM canvas owns the webgpu context.
            _internalCanvas = new HTMLCanvasElement();
            _canvasCtx = _internalCanvas.GetContext<GPUCanvasContext>("webgpu")
                ?? throw new InvalidOperationException("Failed to get WebGPU canvas context.");

            BuildPipeline();
        }

        private void BuildPipeline()
        {
            _uniformBuffer?.Destroy();
            _uniformBuffer?.Dispose();
            _uniformBuffer = Device.CreateBuffer(new GPUBufferDescriptor
            {
                Size = 8,
                Usage = GPUBufferUsage.Uniform | GPUBufferUsage.CopyDst,
                MappedAtCreation = false,
            });

            _bindGroupLayout?.Dispose();
            _bindGroupLayout = Device.CreateBindGroupLayout(new GPUBindGroupLayoutDescriptor
            {
                Entries = new[]
                {
                    new GPUBindGroupLayoutEntry
                    {
                        Binding = 0,
                        Visibility = GPUShaderStageFlags.FRAGMENT,
                        Buffer = new GPUBufferBindingLayout { Type = "read-only-storage" },
                    },
                    new GPUBindGroupLayoutEntry
                    {
                        Binding = 1,
                        Visibility = GPUShaderStageFlags.FRAGMENT,
                        Buffer = new GPUBufferBindingLayout { Type = "uniform" },
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
            _pipeline = Device.CreateRenderPipeline(new GPURenderPipelineDescriptor
            {
                Layout = pipelineLayout,
                Vertex = new GPUVertexState
                {
                    Module = shaderModule,
                    EntryPoint = "vs_main",
                },
                Fragment = new GPUFragmentState
                {
                    Module = shaderModule,
                    EntryPoint = "fs_main",
                    Targets = new[]
                    {
                        new GPUColorTargetState
                        {
                            Format = _canvasFormat,
                        },
                    },
                },
                Primitive = new GPUPrimitiveState
                {
                    Topology = GPUPrimitiveTopology.TriangleList,
                },
            });
        }

        public Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer)
            => PresentBufferAsync(((IArrayView)buffer).Buffer, (uint)buffer.Extent.X, (uint)buffer.Extent.Y);

        public Task PresentAsync(MemoryBuffer2D<int, Stride2D.DenseX> buffer)
            => PresentBufferAsync(((IArrayView)buffer).Buffer, (uint)buffer.Extent.X, (uint)buffer.Extent.Y);

        private Task PresentBufferAsync(MemoryBuffer memBuf, uint width, uint height)
        {
            if (_canvasCtx == null || _pipeline == null || _bindGroupLayout == null || _uniformBuffer == null
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
                    Format = _canvasFormat,
                });
                using var uniformData = new Uint32Array(new uint[] { width, height });
                Queue.WriteBuffer(_uniformBuffer, 0, uniformData);
            }

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
                        Resource = new GPUBufferBinding { Buffer = _uniformBuffer },
                    },
                },
            });

            using var currentTexture = _canvasCtx.GetCurrentTexture();
            using var textureView = currentTexture.CreateView();

            using var encoder = Device.CreateCommandEncoder();
            using var pass = encoder.BeginRenderPass(new GPURenderPassDescriptor
            {
                ColorAttachments = new[]
                {
                    new GPURenderPassColorAttachment
                    {
                        View = textureView,
                        LoadOp = GPULoadOp.Clear,
                        StoreOp = GPUStoreOp.Store,
                    },
                },
            });

            pass.SetPipeline(_pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.Draw(3);
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
            _uniformBuffer?.Destroy(); _uniformBuffer?.Dispose(); _uniformBuffer = null;
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
