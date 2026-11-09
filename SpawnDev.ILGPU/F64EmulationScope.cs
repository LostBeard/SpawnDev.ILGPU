using System;
using System.Threading;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Compiles the kernels loaded inside it with a given <see cref="F64EmulationMode"/> on WebGPU and WebGL, without
    /// changing the accelerator's own <c>F64Mode</c>:
    /// <code>
    /// using (F64EmulationScope.Use(F64EmulationMode.Ieee))
    ///     lpc = await accelerator.LoadAutoGroupedStreamKernelAsync&lt;Index1D, ...&gt;(LpcKernel);
    /// </code>
    /// The mode is an <see cref="AsyncLocal{T}"/>: it follows this call through its awaits (the async loaders compile after
    /// returning to the browser's event loop) but NOT into other loads interleaved on the browser thread, which keep the
    /// accelerator's mode - flipping <c>accelerator.F64Mode</c> around an awaited load would leak into them. The compiled-kernel
    /// cache is keyed by the mode, so the same kernel method loaded inside and outside a scope gives two kernels. Backends
    /// with native doubles (CUDA, OpenCL, CPU, Velocity, Wasm) ignore it.
    /// </summary>
    public static class F64EmulationScope
    {
        private static readonly AsyncLocal<F64EmulationMode?> current = new();

        /// <summary>The mode set by the innermost scope of the current async flow, or null.</summary>
        public static F64EmulationMode? Current => current.Value;

        /// <summary>
        /// Sets <paramref name="mode"/> for kernels compiled in the current async flow until the returned scope is disposed.
        /// </summary>
        public static IDisposable Use(F64EmulationMode mode)
        {
            var previous = current.Value;
            current.Value = mode;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly F64EmulationMode? previous;
            private bool disposed;

            public Scope(F64EmulationMode? previous) => this.previous = previous;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                current.Value = previous;
            }
        }
    }
}
