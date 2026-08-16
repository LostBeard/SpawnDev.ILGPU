// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGL
//                 WebGL2 Compute Library for Blazor WebAssembly
//
// File: WebGLDevice.cs
// ---------------------------------------------------------------------------------------

using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using GL = SpawnDev.SpawnJS.JSObjects.GL;
using System.Collections.Immutable;

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>
    /// Represents a WebGL2 device available in the browser.
    /// Creates an OffscreenCanvas and obtains a WebGL2RenderingContext for GPGPU
    /// via Transform Feedback.
    /// </summary>
    public sealed class WebGLDevice : IDisposable
    {
        #region Static

        /// <summary>
        /// Checks if WebGL2 is supported in the current browser.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                try
                {
                    using var canvas = new OffscreenCanvas(1, 1);
                    using var gl = canvas.GetWebGL2Context();
                    return gl != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Asynchronously detects all available WebGL2 devices.
        /// WebGL2 typically exposes a single device per browser context.
        /// </summary>
        public static Task<ImmutableArray<WebGLDevice>> GetDevicesAsync()
        {
            var devices = ImmutableArray.CreateBuilder<WebGLDevice>();

            if (!IsSupported)
                return Task.FromResult(devices.ToImmutable());

            try
            {
                var device = new WebGLDevice(0);
                devices.Add(device);
            }
            catch
            {
                // WebGL2 not available
            }

            return Task.FromResult(devices.ToImmutable());
        }

        /// <summary>
        /// Gets the default WebGL2 device if available.
        /// </summary>
        public static async Task<WebGLDevice?> GetDefaultDeviceAsync()
        {
            var devices = await GetDevicesAsync();
            return devices.Length > 0 ? devices[0] : null;
        }

        #endregion

        #region Instance

        private readonly int _deviceIndex;
        private bool _disposed;

        internal WebGLDevice(int deviceIndex)
        {
            _deviceIndex = deviceIndex;

            // Create a short-lived OffscreenCanvas + WebGL2 context to probe capabilities,
            // then explicitly destroy the WebGL context before returning.
            //
            // Why explicit destruction: .NET `using` / `Dispose` on a SpawnDev.SpawnJS
            // SpawnJSObject only releases the .NET-side IJSInProcessObjectReference. The
            // underlying JS OffscreenCanvas and its WebGL2 context survive until JS GC,
            // which on a long-lived SPA can take many page navigations. Browsers throttle
            // past ~16 live WebGL contexts per page; an app that registers WebGL devices
            // across many pages (e.g. via AllAcceleratorsAsync at every demo page mount)
            // hits the throttle warning even when WebGL is never the selected backend.
            //
            // The fix: call WEBGL_lose_context.loseContext() to force the browser to
            // release the GL resources synchronously. Capability values stay cached on
            // the device. Real accelerator contexts are still minted on-demand by
            // CreateContext() when WebGL is actually selected.
            using var canvas = new OffscreenCanvas(1, 1);
            using var gl = canvas.GetWebGL2Context()
                ?? throw new InvalidOperationException("WebGL2 is not supported in this browser.");

            Name = GetRendererString(gl) ?? "WebGL2 Device";
            Vendor = GetVendorString(gl) ?? "Unknown";

            MaxTextureSize = gl.GetParameter<int>(GL.MAX_TEXTURE_SIZE);
            MaxUniformBlockSize = gl.GetParameter<int>(GL.MAX_UNIFORM_BLOCK_SIZE);
            MaxTransformFeedbackSeparateComponents = gl.GetParameter<int>(GL.MAX_TRANSFORM_FEEDBACK_SEPARATE_COMPONENTS);
            MaxTransformFeedbackInterleavedComponents = gl.GetParameter<int>(GL.MAX_TRANSFORM_FEEDBACK_INTERLEAVED_COMPONENTS);

            // Estimate max vertex count for GPGPU dispatch
            // WebGL2 guarantees at least 2^24 − 1 vertices
            MaxVertexCount = 16777215; // 2^24 - 1

            // Explicitly destroy the probe WebGL context. WEBGL_lose_context is universally
            // available in WebGL2; the extension call is best-effort — if it fails for any
            // reason the .NET dispose still releases the IJSInProcessObjectReference.
            try
            {
                using var loseExt = gl.GetExtension("WEBGL_lose_context");
                if (loseExt is not null)
                {
                    loseExt.JSRef!.CallVoid("loseContext");
                }
            }
            catch
            {
                // Best-effort — capability probe still succeeded.
            }
        }

        private static string? GetRendererString(WebGL2RenderingContext gl)
        {
            try
            {
                // Try WEBGL_debug_renderer_info for unmasked renderer
                var ext = gl.GetExtension("WEBGL_debug_renderer_info");
                if (ext != null)
                {
                    return gl.GetParameter<string>(GL.UNMASKED_RENDERER_WEBGL);
                }
                return gl.GetParameter<string>(GL.RENDERER);
            }
            catch
            {
                return null;
            }
        }

        private static string? GetVendorString(WebGL2RenderingContext gl)
        {
            try
            {
                var ext = gl.GetExtension("WEBGL_debug_renderer_info");
                if (ext != null)
                {
                    return gl.GetParameter<string>(GL.UNMASKED_VENDOR_WEBGL);
                }
                return gl.GetParameter<string>(GL.VENDOR);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a new WebGL2 context (creates a fresh OffscreenCanvas).
        /// Each accelerator should call this to get its own context.
        /// </summary>
        public (OffscreenCanvas canvas, WebGL2RenderingContext gl) CreateContext()
        {
            var canvas = new OffscreenCanvas(1, 1);
            var gl = canvas.GetWebGL2Context();
            if (gl == null)
            {
                canvas.Dispose();
                throw new InvalidOperationException("Failed to create WebGL2 context.");
            }
            return (canvas, gl);
        }

        /// <summary>
        /// Creates a new OffscreenCanvas without obtaining a WebGL2 context.
        /// Used for worker offloading — the GL context is created inside the worker
        /// after the canvas is transferred via postMessage.
        /// </summary>
        public OffscreenCanvas CreateOffscreenCanvas()
        {
            return new OffscreenCanvas(1, 1);
        }

        /// <summary>
        /// Returns the device name (GPU renderer string).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Returns the GPU vendor string.
        /// </summary>
        public string Vendor { get; }

        /// <summary>
        /// Gets the maximum texture size (used for TBO data width).
        /// </summary>
        public int MaxTextureSize { get; }

        /// <summary>
        /// Gets the maximum uniform block size in bytes.
        /// </summary>
        public int MaxUniformBlockSize { get; }

        /// <summary>
        /// Gets the max transform feedback separate components.
        /// </summary>
        public int MaxTransformFeedbackSeparateComponents { get; }

        /// <summary>
        /// Gets the max transform feedback interleaved components.
        /// </summary>
        public int MaxTransformFeedbackInterleavedComponents { get; }

        /// <summary>
        /// Gets the maximum vertex count for a single drawArrays call.
        /// </summary>
        public int MaxVertexCount { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Prints device information to the console.
        /// </summary>
        public void PrintInfo(TextWriter writer)
        {
            writer.WriteLine($"WebGL2 Device: {Name}");
            writer.WriteLine($"  Vendor:             {Vendor}");
            writer.WriteLine($"  Max Texture Size:   {MaxTextureSize}");
            writer.WriteLine($"  Max UBO Size:       {MaxUniformBlockSize} bytes");
            writer.WriteLine($"  Max TF Components:  {MaxTransformFeedbackInterleavedComponents} (interleaved)");
            writer.WriteLine($"  Max Vertex Count:   {MaxVertexCount}");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // No long-lived OffscreenCanvas/WebGL2RenderingContext is held by the device
            // itself — the constructor's probe context is disposed at the end of the
            // constructor. Per-accelerator contexts are owned by their respective
            // WebGLAccelerator instances and disposed when those accelerators dispose.
        }

        #endregion
    }
}
