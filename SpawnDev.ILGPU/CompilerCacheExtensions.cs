using global::ILGPU;
using global::ILGPU.Runtime;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Releasing the memory a <see cref="Context"/> keeps for compiling kernels.
    /// </summary>
    public static class CompilerCacheExtensions
    {
        /// <summary>
        /// Releases the compiler's cached copy of your kernel code: the IR and type information of every method the
        /// <see cref="Context"/> has compiled from your assemblies, which it otherwise keeps for its whole life.
        /// Call it after loading a batch of kernels, for example at the end of a constructor that loads all of its kernels.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why:</b> the cache grows with every kernel loaded, about 0.9 MiB per kernel for large kernels. In a browser,
        /// where the context usually lives as long as the page, that can exhaust the .NET heap: SpawnDev.Codecs' lossy
        /// WebP encoder (about 20 kernels) adds 20.1 MiB on Wasm and 28.4 MiB on WebGPU, of which this frees 16.1 and
        /// 19.6 MiB (measured 2026-11-20).
        /// </para>
        /// <para>
        /// <b>Safe:</b> kernels loaded before the call keep running, and kernels loaded after it compile as usual
        /// (test <c>Context_ClearCacheDefault_LoadedKernelsKeepRunning_NewOnesCompile</c>, every backend). ILGPU's own
        /// built-in types and intrinsics stay cached; only what came from your code is dropped.
        /// </para>
        /// <para>
        /// <b>Cost:</b> the next kernel loaded re-imports the code it shares with earlier kernels: about 5 to 15 ms more
        /// for that first load (measured: Wasm 9.4 to 20.5 ms, WebGPU about 20 to 25-32 ms, CUDA 12.7 to 20.4 ms). So call
        /// it once after a batch of loads, not after every load.
        /// </para>
        /// <para>
        /// The release applies to the whole <see cref="Context"/>, so every accelerator of that context is affected.
        /// <b>Thread-safe:</b> call it from any thread, also while other threads load kernels on the same context (two
        /// warm-ups on <c>Task.Run</c>). It waits for the kernel compiles in flight to finish, and loads that start while it
        /// runs wait for it (test <c>Context_ClearCacheDefault_WhileAnotherThreadLoadsKernels_EveryKernelCorrect</c>). Do
        /// not call it from inside a kernel compile (a backend hook): that throws <see cref="System.InvalidOperationException"/>.
        /// It does the same as <c>accelerator.Context.ClearCache(ClearCacheMode.Default)</c>.
        /// </para>
        /// </remarks>
        /// <param name="accelerator">Any accelerator of the context to release.</param>
        public static void ReleaseCompilerCaches(this Accelerator accelerator)
        {
            if (accelerator is null) throw new System.ArgumentNullException(nameof(accelerator));
            accelerator.Context.ClearCache(ClearCacheMode.Default);
        }
    }
}
