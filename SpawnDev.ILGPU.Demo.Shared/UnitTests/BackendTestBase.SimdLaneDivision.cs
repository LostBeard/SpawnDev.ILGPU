using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Integer division and remainder in the lanes of a SIMD warp that do not take part. The Velocity backend executes ALL four lanes of a warp for every instruction and masks
    // the results: the tail lanes of the last warp (a thread count that is not a multiple of 4) and the lanes switched off by `if (divisor != 0)` still ran the division with
    // whatever operands the registers held, usually zero, and a zero divisor in ANY lane made the vector division throw DivideByZeroException for the whole warp.
    // Found running SpawnDev.ILGPU.ML's tests on Velocity: 60 of 150 failures. Fixed with NonZeroDivisor in Vec128Operations (zero divisor lanes become one; the lanes that
    // matter are untouched). Every other backend must give the same answers, so this runs on all of them.
    public abstract partial class BackendTestBase
    {
        // thread count not a multiple of the warp: the tail lanes read past the buffers (masked) and must not divide by their zeros
        static void SimdDivTailKernel(Index1D i, ArrayView<int> a, ArrayView<int> b, ArrayView<int> q, ArrayView<int> r, int n)
        {
            if (i < n)
            {
                q[i] = a[i] / b[i];
                r[i] = a[i] % b[i];
            }
        }

        // the usual guard: lanes with a zero divisor are switched off but their division still runs in a SIMD warp
        static void SimdDivGuardedKernel(Index1D i, ArrayView<int> a, ArrayView<int> b, ArrayView<int> q, ArrayView<int> r, ArrayView<uint> uq)
        {
            if (b[i] != 0)
            {
                q[i] = a[i] / b[i];
                r[i] = a[i] % b[i];
                uq[i] = (uint)a[i] / (uint)b[i];
            }
        }

        [TestMethod]
        public async Task SimdLaneDivision_TailLanesAndGuardedLanesDoNotThrow() => await RunTest(async accelerator =>
        {
            const int n = 11;                                  // 2 full warps of 4 plus a tail of 3
            var a = new int[n]; var b = new int[n];
            for (int i = 0; i < n; i++) { a[i] = 100 + 7 * i; b[i] = (i % 3 == 1) ? 0 : (i + 2); }     // zero divisors in some lanes
            var bTail = new int[n]; for (int i = 0; i < n; i++) bTail[i] = i + 2;                     // no zeros: only the tail lanes can be zero

            using var ab = accelerator.Allocate1D(a); using var bb = accelerator.Allocate1D(b); using var btb = accelerator.Allocate1D(bTail);
            using var q = accelerator.Allocate1D<int>(n); using var r = accelerator.Allocate1D<int>(n); using var uq = accelerator.Allocate1D<uint>(n);
            q.MemSetToZero(); r.MemSetToZero(); uq.MemSetToZero();

            var tail = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(SimdDivTailKernel);
            tail((Index1D)n, ab.View, btb.View, q.View, r.View, n);
            await accelerator.SynchronizeAsync();
            var gq = await q.CopyToHostAsync<int>(); var gr = await r.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (gq[i] != a[i] / bTail[i] || gr[i] != a[i] % bTail[i])
                    throw new Exception($"tail kernel: element {i}: {a[i]} / {bTail[i]} = {gq[i]} rem {gr[i]}, expected {a[i] / bTail[i]} rem {a[i] % bTail[i]}");
            }

            q.MemSetToZero(); r.MemSetToZero(); uq.MemSetToZero();
            var guarded = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<uint>>(SimdDivGuardedKernel);
            guarded((Index1D)n, ab.View, bb.View, q.View, r.View, uq.View);
            await accelerator.SynchronizeAsync();
            gq = await q.CopyToHostAsync<int>(); gr = await r.CopyToHostAsync<int>(); var guq = await uq.CopyToHostAsync<uint>();
            for (int i = 0; i < n; i++)
            {
                int eq = b[i] != 0 ? a[i] / b[i] : 0, er = b[i] != 0 ? a[i] % b[i] : 0; uint euq = b[i] != 0 ? (uint)a[i] / (uint)b[i] : 0u;
                if (gq[i] != eq || gr[i] != er || guq[i] != euq)
                    throw new Exception($"guarded kernel: element {i} (divisor {b[i]}): got {gq[i]} rem {gr[i]} u {guq[i]}, expected {eq} rem {er} u {euq}");
            }
        });
    }
}
