using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// DIAGNOSTIC (2026-05-29, Tuvok) — localizes the intermittent Wasm large-sort residual to a
    /// single radix PASS by snapshotting each pass's per-group bucket counts (the pre-scan
    /// <c>counterView</c> written by RadixSort Kernel1) and checking the count invariant.
    ///
    /// WHY A COPY KERNEL: <see cref="RadixSortExtensions.PerPassHook"/> is a SYNCHRONOUS
    /// <c>Action</c> and the orchestration calls <c>stream.Synchronize()</c> before it — but that
    /// is a NO-OP on the Wasm backend (Blazor WASM is single-threaded async; the only real barrier
    /// is <c>await SynchronizeAsync()</c>, which a sync hook cannot call). And Wasm <c>CopyTo</c> is a
    /// synchronous immediate SharedArrayBuffer read that does NOT serialize through the dispatch
    /// pipeline — so a host/device copy from the hook would race the still-queued worker dispatches.
    /// The ONLY stream-ordered primitive on Wasm is a KERNEL dispatch (it serializes via
    /// <c>_pendingWork</c>). So each pass is snapshotted with a trivial copy kernel that queues after
    /// that pass's Kernel1 and before the next pass overwrites <c>counterView</c>. All snapshots are
    /// read after the sort's own <c>await SynchronizeAsync()</c>.
    ///
    /// INVARIANT (reference-free): every pass's bucket-count sum must equal the in-range element
    /// count — the SAME value every pass. The first pass whose sum deviates is where Kernel1
    /// miscounted (localizes the corruption to one pass + the count kernel). If all sums match yet
    /// the sort is still wrong, the counts are fine and the bug is in the SCAN or the SCATTER (pos),
    /// not the count. Negative / absurd entries flag a corrupted shared-memory scan directly.
    ///
    /// CAVEAT: the extra per-pass copy-kernel dispatches add serialization points that can shift
    /// timing; if enabling this masks the residual entirely, that itself points to an inter-pass /
    /// dispatch-overlap race rather than a within-pass kernel bug.
    ///
    /// Wasm-gated (re-entrant mid-sort kernel launches are risky on WebGPU/CUDA and unnecessary —
    /// the residual is Wasm-only). Uncommitted diagnostic; remove once root-caused.
    /// </summary>
    public static class RadixCounterLocalizer
    {
        /// <summary>Master switch. Default ON so the next FO76 sweep captures localization.</summary>
        public static bool Enabled = true;

        private static Accelerator? _acc;
        private static Action<Index1D, ArrayView<int>, ArrayView<int>>? _copyKernel;
        private static readonly List<(int bitIdx, MemoryBuffer1D<int, Stride1D.Dense> buf)> _snaps = new();

        /// <summary>True only for the Wasm accelerator (where the residual lives).</summary>
        public static bool ShouldInstrument(Accelerator accelerator) =>
            Enabled && accelerator.GetType().Name.IndexOf("Wasm", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Installs the per-pass snapshot hook. Call before launching the radix sort; pair with
        /// <see cref="AnalyzeAsync"/> after the sort's SynchronizeAsync and <see cref="Uninstall"/>
        /// in a finally. No-op (returns false) when not instrumenting this accelerator.
        /// </summary>
        public static bool Install(Accelerator accelerator)
        {
            if (!ShouldInstrument(accelerator)) return false;
            _acc = accelerator;
            // Load fresh per Install (tied to THIS accelerator; cheap — ILGPU caches the compile).
            // Never cache across Installs: the accelerator may have been disposed + recreated.
            _copyKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                (i, src, dst) => dst[i] = src[i]);
            ClearSnapshots();
            RadixSortExtensions.PerPassHook = (bitIdx, counterView) =>
            {
                // counterView = Kernel1 per-group bucket counts (pre-scan). Stream-ordered snapshot
                // via copy kernel — serializes through _pendingWork on Wasm, so it captures the
                // post-Kernel1 state even though the preceding stream.Synchronize() is a no-op here.
                int len = (int)counterView.Length;
                var buf = accelerator.Allocate1D<int>(len);
                _copyKernel!((Index1D)len, counterView, buf.View.AsContiguous());
                _snaps.Add((bitIdx, buf));
            };
            return true;
        }

        public static void Uninstall()
        {
            RadixSortExtensions.PerPassHook = null;
            ClearSnapshots();
            _acc = null;
        }

        private static void ClearSnapshots()
        {
            for (int i = 0; i < _snaps.Count; i++)
            {
                try { _snaps[i].buf.Dispose(); } catch { /* best-effort */ }
            }
            _snaps.Clear();
        }

        /// <summary>
        /// Reads all per-pass snapshots and builds a localization report. Reference-free: flags the
        /// first pass whose bucket-count sum differs from pass 0's (== the element count), or that
        /// has negative/absurd entries. Returns (allConsistent, report). Call AFTER the sort's
        /// <c>await SynchronizeAsync()</c> (which drains the snapshot copy kernels too).
        /// </summary>
        public static async Task<(bool allConsistent, string report)> AnalyzeAsync()
        {
            var sb = new StringBuilder();
            if (_snaps.Count == 0)
                return (true, "[RadixCounterLocalizer] no pass snapshots captured (hook not installed or no radix sort ran).");

            if (_acc != null)
                await _acc.SynchronizeAsync(); // ensure all snapshot copy kernels have drained

            sb.AppendLine($"[RadixCounterLocalizer] {_snaps.Count} pass-snapshots — bucket-count sum must be CONSTANT across passes (== element count):");
            long expectedSum = long.MinValue;
            int firstBad = -1;
            for (int p = 0; p < _snaps.Count; p++)
            {
                var (bitIdx, buf) = _snaps[p];
                int[] h;
                try { h = await buf.CopyToHostAsync(); }
                catch (Exception ex) { sb.AppendLine($"  pass#{p} (bitIdx={bitIdx}): readback FAILED: {ex.Message}"); continue; }

                long sum = 0; int min = int.MaxValue, max = int.MinValue, neg = 0;
                for (int k = 0; k < h.Length; k++)
                {
                    int v = h[k];
                    sum += v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    if (v < 0) neg++;
                }
                if (expectedSum == long.MinValue) expectedSum = sum; // pass 0 defines the reference
                bool ok = (sum == expectedSum) && (neg == 0);
                if (!ok && firstBad < 0) firstBad = p;
                sb.AppendLine($"  pass#{p} (bitIdx={bitIdx}): sum={sum}{(sum == expectedSum ? "" : $"  <-- DEVIATES from pass0 sum {expectedSum}")} min={min} max={max} neg={neg} entries={h.Length}");
            }

            if (firstBad >= 0)
                sb.AppendLine($"  ROOT: FIRST corrupted counter at pass#{firstBad} -> RadixSort Kernel1 (per-group count) miscounted on that pass under contention. Read that pass's emitted phase code.");
            else
                sb.AppendLine("  ROOT: all per-pass counter sums CONSISTENT + non-negative -> Kernel1 counts are FINE; the corruption is in the SCAN or the SCATTER (pos computation), NOT the count.");
            return (firstBad < 0, sb.ToString());
        }
    }
}
