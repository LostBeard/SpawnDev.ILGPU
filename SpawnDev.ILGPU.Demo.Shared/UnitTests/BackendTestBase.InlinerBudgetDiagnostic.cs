using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // One-off diagnostic: does the Inliner's cumulative-IL budget FIRE for a heavy NORMAL kernel
    // (RadixSortPairs — the most-inlined algorithm, and the family whose Wasm test races occasionally)?
    // With the VP9 Import_Sync fix active (ILInstructionCount preserved across specialization), the budget
    // CAN fire. A delta of 0 means RadixSort stays well under CumulativeInlinedILBudget (16,384 IL) → the
    // fix is INERT for it → the VP9 fix is a no-op for normal kernels (safest possible ship), and the
    // RadixSort Wasm race is unrelated to it. Inlining is backend-agnostic, so the desktop lanes (where
    // Console.Error prints cleanly, no #blazor-error-ui) give the answer.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task Inliner_BudgetFires_RadixSortDiagnostic() => await RunTest(async accelerator =>
        {
            // RadixSort needs shared memory + atomics — WebGL has neither, so it can't compile there.
            // The inliner budget is backend-agnostic (it fires at IR construction, before codegen), so the
            // answer is identical on every backend that DOES run RadixSort; skipping WebGL loses nothing.
            if (BackendName == "WebGL")
                throw new UnsupportedTestException("RadixSort requires shared memory + atomics (WebGL has neither).");

            long before = global::ILGPU.IR.Transformations.Inliner.CumulativeBudgetSkipCount;

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();

            const int n = 4096;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(7);
            for (int i = 0; i < n; i++) { keys[i] = rng.Next(); values[i] = i; }
            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            long after = global::ILGPU.IR.Transformations.Inliner.CumulativeBudgetSkipCount;
            long delta = after - before;
            Console.WriteLine(
                $"===BUDGETFIRES=== RadixSortPairs: before={before} after={after} delta={delta} " +
                "(delta==0 => budget did NOT fire for RadixSort => the inliner budget is inert for normal kernels)");

            // Regression guard: a heavy NORMAL algorithm kernel must NOT trip the cumulative-IL budget —
            // it stays well under CumulativeInlinedILBudget (16,384 IL). Measured 2026-06-05: delta=0 on
            // every backend (so the VP9 Import_Sync fix that makes the budget functional is a no-op for
            // normal kernels; it only bounds pathological deep-inline trees like the VP9 walker). If this
            // ever fires (delta>0), a normal kernel's inlining is being bounded — investigate before it
            // changes that kernel's codegen.
            if (delta != 0)
                throw new Exception(
                    $"Inliner cumulative-IL budget fired for RadixSortPairs ({delta} call(s) left un-inlined) — " +
                    "a NORMAL kernel now exceeds CumulativeInlinedILBudget. This changes its codegen; investigate.");
        });
    }
}
