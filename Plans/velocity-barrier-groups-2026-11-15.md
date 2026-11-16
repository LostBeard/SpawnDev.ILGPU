# Velocity: barrier-heavy kernels (VP9 loop filter) - where the time goes and what would fix it (2026-11-15, Data)

Status: PROPOSAL, nothing built. Option B is a runtime/codegen change and needs TJ's go; options A and C are kernel
changes (Geordi, SpawnDev.Codecs).

## Measured (Codecs 4066dc3, ILGPU 5.4.33, 1080p vp90-2-02-size-lf, 10 frames x 3 reps)

| device | loop filter ms/frame | note |
|---|---|---|
| Velocity V256, adaptive teams (2 x 8 warp threads) | 265 - 271 | default |
| Velocity V256, one team (8 threads, AdaptiveTeamCount = false) | 302 | the second team buys 12% |
| Velocity V128 | 484 | |
| CPU accelerator (5.4.33) | ~1,150 | ~1,060 of it launch dispatch, fixed in 5.4.34 (6b03356); filtering itself ~8 ms |
| CUDA | 2.78 | |

dotnet-trace (sampled thread time, CPU attributed to the calling managed frame; scratchpad lftrace2):

- the wavefront kernel is 63% of the process's CPU;
- 2 teams: the 16 warp threads spend 67% of their CPU in `VelocityGroupBarrier.SignalAndWait`;
- 1 team (not oversubscribed): 44% waiting, and per-thread WORK totals are even (3.8 - 4.4 s, +-7%). So no fixed lane
  skew: the waiting is per-phase imbalance that rotates between warps (which edges filter, at 4 / 8 / 16 taps, depends
  on the superblock), at the vertical -> horizontal barrier and at the group end, twice per superblock. The barrier
  code is fine (spins then yields; never sleeps unless oversubscribed).

## The three levers

**C. Divergence: the work itself (largest).** Velocity spends ~1.35 CPU-s of filtering per 1080p frame; the CPU
accelerator (scalar, one lane at a time) spends ~8 ms of filtering on the same frame. SIMT on SIMD runs every filter
variant any lane of the warp takes for all lanes. Kernel-side (Geordi): group lanes by filter width so a warp's lanes
take the same Lpf4 / 8 / 16 path. Measure before and after.

**A. Rotating imbalance, kernel-side (Geordi).** Hand out each pass's rows / columns dynamically from a counter in
shared memory instead of a fixed share per warp. On GPUs it costs one atomic per item.

**B. Runtime: run a barrier group on ONE thread, warps serialized between barriers, parallel across groups.** A wavefront
step has ~8 active superblocks on average (17 x 30 superblocks, 62 steps), so 12 threads would each run whole groups
with no barrier waits at all. Upper bound from the one-team trace: removing the 44% wait leaves ~170 ms/frame on 8
threads; spread over 12 threads and the step's ~8 groups, roughly 120 - 170 ms/frame vs 265 now (1.5 - 2x).
Mechanism: the kernel must stop at a barrier and resume per warp, which Velocity cannot do today (a warp is a real
thread with its own stack). The parked chunking patch (`velocity-chunking-parked-2026-11-15.patch`, see
`velocity-jit-method-budget-2026-11-15.md`) already splits an emitted function into chunk methods sharing a frame, with
a dispatcher that returns the next block id; a barrier would become a forced chunk boundary, each warp would get its
own frame, and one thread would loop "for each warp: run until the next barrier". Risks: every value live across a
barrier moves into the frame (slower code between barriers), shared memory and warp shuffles inside a phase need care,
and it is a second execution model to keep correct on the whole suite.

## Recommendation

C first (largest: ~45x the scalar work), then A (cheap, kernel-local), and decide B only after both are measured. If
B is wanted, prototype it on the loop filter alone behind a flag and A/B it before generalizing.
