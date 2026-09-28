using System.Diagnostics;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Opt-in phase timing of Wasm kernel dispatch (diagnostic; default off, zero cost when off).
    /// Splits a dispatch into: prepare (argument/memory setup after the previous dispatch finished), worker
    /// script build, worker acquire, posting to workers, and waiting for them. Added 2026-09-28 to find where a
    /// ~2 ms per-launch cost goes for a 32-element kernel (BackendTestBase.LaunchCost).
    /// </summary>
    public static class WasmDispatchProfile
    {
        /// <summary>Turn the timing on.</summary>
        public static bool Enabled;
        /// <summary>Dispatches timed since the last <see cref="Reset"/>.</summary>
        public static long Dispatches;
        /// <summary>Total workers used across those dispatches.</summary>
        public static long WorkersUsed;
        /// <summary>Total characters of worker script built across those dispatches.</summary>
        public static long ScriptChars;
        internal static long PrepareTicks, ScriptTicks, AcquireTicks, PostTicks, WaitTicks, CopyInTicks, CopyOutTicks, TotalTicks;
        // Pipelined dispatches: host time handling a worker's answer, and the worker's own time per dispatch
        // (zero + copy-in + kernel + copy-out, reported by the worker).
        internal static long ResponseTicks, PipelinedAnswers;
        // Host prepare split: the launcher before RunKernelAsync, the argument walk, and the flat-argument build.
        internal static long LaunchTicks, ArgsTicks, FlatTicks;
        internal static double WorkerMs;

        internal static long Add(ref long bucket, long since)
        {
            long now = Stopwatch.GetTimestamp();
            bucket += now - since;
            return now;
        }

        /// <summary>Zero all counters.</summary>
        public static void Reset()
        {
            Dispatches = WorkersUsed = ScriptChars = 0;
            PrepareTicks = ScriptTicks = AcquireTicks = PostTicks = WaitTicks = CopyInTicks = CopyOutTicks = TotalTicks = 0;
            ResponseTicks = PipelinedAnswers = 0; WorkerMs = 0;
            LaunchTicks = ArgsTicks = FlatTicks = 0;
        }

        /// <summary>Per-dispatch averages in milliseconds.</summary>
        public static string Summary()
        {
            if (Dispatches == 0) return "no dispatches";
            double Ms(long t) => t * 1000.0 / Stopwatch.Frequency / Dispatches;
            return $"{Dispatches} dispatches, per dispatch: TOTAL {Ms(TotalTicks):F3} ms = prepare {Ms(PrepareTicks):F3} (of which args {Ms(ArgsTicks):F3}, flat args {Ms(FlatTicks):F3}, copy-in {Ms(CopyInTicks):F3}), launcher {Ms(LaunchTicks):F3}, script {Ms(ScriptTicks):F3} ms " +
                   $"({ScriptChars / Dispatches} chars), acquire {Ms(AcquireTicks):F3} ms, post {Ms(PostTicks):F3} ms, " +
                   $"wait {Ms(WaitTicks):F3} ms, copy-out {Ms(CopyOutTicks):F3} ms, workers {(double)WorkersUsed / Dispatches:F1}" +
                   (PipelinedAnswers > 0
                       ? $"; pipelined answers {PipelinedAnswers}: host handling {ResponseTicks * 1000.0 / Stopwatch.Frequency / PipelinedAnswers:F3} ms, worker {WorkerMs / PipelinedAnswers:F3} ms each"
                       : "");
        }
    }
}
