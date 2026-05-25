# PMT Parallel Execution — Execution Plan

**Author:** Tuvok (sole editor, Captain's order) · **Drafted:** 2026-05-24 (pre-quota-reset planning) · **Status:** READY TO EXECUTE after quota reset.

**Goal (TJ's words):** Full PMT sweep is **4.5–5 hours** and blocks every release. Run tests in **parallel batches (~4 at a time)** to cut wall-clock toward **~25%** of current.

**Standing constraint (TJ, 2026-05-24):** *"It is fine if we mark ALL wasm tests as single test run (no parallel testing)."* Wasm is never parallelized. Treated as an isolated serial lane.

This file is self-contained: it records the verified architecture so the execution session does NOT re-derive it. Read it top-to-bottom, then start at **Step 0 (MEASURE)** — do not skip measurement (Rule 4b).

---

## 0. Hard constraints to honor (do not violate any of these)

1. **Wasm = serial + isolated.** Wasm sort kernels spawn `hardwareConcurrency` pure-spin barrier workers that STARVE under CPU oversubscription. Never run two Wasm tests concurrently, and (default/safe) never overlap a Wasm test with any other CPU-heavy lane. See `[[feedback-wasm-waitnotify-races-verdict-2026-05-24]]`, `[[project_wasm_race_v8_finding_2026_04_28]]`.
2. **One PMT run per editor.** Pre-flight `Get-Process testhost | ? { $_.Path -like '*PlaywrightMultiTest*' -or $_.Path -like '*SpawnDev.ILGPU*' }` before every invocation (Rule 5b). Kill only PIDs you started.
3. **TJ watches the Chromium windows** — non-headless is a feature. More browser lanes = more windows popping on his desktop. Keep the lane count sane (≤3 browser tabs by default).
4. **Single physical GPU.** WebGPU + WebGL + CUDA + OpenCL all share it. Concurrency is allowed (no corruption) but skews perf-sensitive tests and can OOM on the 4M-element sort battery. Cap GPU-bound concurrency conservatively; mark perf benchmarks serial.
5. **Single static-server port (5451).** Fixed so IndexedDB/OPFS persists across runs. If multiple browser contexts are used they must share the one server (they can; same origin).
6. **No test may be silently dropped or skipped** by the parallel scheduler. Validation (Step 5) must prove the parallel run covers the exact same test set as a sequential baseline.
7. **Lane ownership:** PMT lives in the **ILGPU** repo and `SpawnDev.UnitTesting` is **Geordi's lane**. Captain designated Tuvok editor for this task — that overrides default lane mapping, but **post DevComms to Geordi** for every file touched in both repos (CLAUDE.md "notify immediately when you modify another agent's project").

---

## 1. Verified architecture (current state — confirmed by reading source 2026-05-24)

### 1a. PMT is an NUnit harness driven by `TestCaseSource`
- `PlaywrightMultiTest/UnitTest1.cs` — fixture `Tests : PageTest`, `[Parallelizable(ParallelScope.Self)]` + `[TestFixture]`.
  - `[OneTimeSetUp] StartApp()` → `ProjectRunner.Instance.StartUp()` (the `Instance` getter triggers `Init()`).
  - `[Test, TestCaseSource(nameof(TestCases))] RunTest(ProjectTest test)` — **the single method every test flows through.** `ParallelScope.Self` = the fixture may run parallel to *other fixtures*, but **its cases run strictly sequentially.** This is the root cause of the 4.5 hr wall-clock.
  - `[OneTimeTearDown] StopApp()` → writes final summary, shuts down.
- `ProjectRunner.cs` (`Init()`, lines ~93–426):
  - `ProjectDiscovery.GetWorkspaceRoot()` finds csproj files with a `<PlaywrightMultiTest>` MSBuild element (`ProjectDiscovery.cs:228`, `ProjectDetails.IsTestProject`).
  - **Blazor WASM project branch (~120–305):** `dotnet publish -c Release -p:BuildInParallel=false -maxcpucount:1`; start ONE `StaticFileServer` on `https://localhost:5451`; launch ONE non-headless Chromium **persistent** context (profile `%TEMP%/SpawnDev.ILGPU.PlaywrightProfile`); open ONE `Page`; navigate to `/tests`; wait for `table.unit-test-ready`; enumerate ALL rows via a single JS `querySelectorAll('table.unit-test-view tbody tr')` returning `{typeName, methodName}` per row (`ProjectRunner.cs:272`).
  - **Console/Exe branch (~306–421):** publish; run the exe with no args to list tests (`ClassName.MethodName` per line); for each, set `TestFunc` to launch `ProcessRunner.Run(binary, testName, timeout: 600_000)` as a **fresh subprocess** and parse `TEST: {json}`.
  - `GetPlaywrightTasks()` (~434) yields one `TestCaseData` per enumerated test, plus 3 hardcoded P2P two-tab integration tests (browser-only).
- `ProjectTest.cs`:
  - Browser tests use `ProjectTest.RunTest(IPage page)` (`:73`): if `page.Url != TestPageUrl` navigate; locate row `tr.{TestClassName}` (`TestClassName = "{TypeName}-{MethodName}"`); click the row's **Run** button; poll `runButton.IsEnabledAsync()` until re-enabled (= done); read `.test-state` text + `test-error`/`test-unsupported` CSS classes for the verdict.
  - **All browser tests share the one `blazorProj.Page`** — sequential by construction.
- `TestableConsole.cs` / `TestableBlazorWasm.cs` — thin state holders.
- `UnitTest1.cs RunTest` records every result via `TestResultsWriter.RecordResult(name, status, msg, ms)` — **the .NET side is the authoritative results sink.**

### 1b. The test app (SpawnDev.UnitTesting consumers)
- `SpawnDev.UnitTesting.Blazor/UnitTestsView.razor`: renders one `<tr class="@stateClass @resultClass {TypeName}-{MethodName}">` per test, with cells `.test-type-name`, `.test-method-name`, `.test-state`, etc. **This is where browser-side per-test metadata must be emitted** (e.g. `data-parallel`, `data-backend`).
- `SpawnDev.UnitTesting/TestMethodAttribute.cs` (committed v2.5.2): properties `Name`, `Timeout`, `RetryCount`, `Category`. `[AttributeUsage(..., Inherited = true)]` — base-class `[TestMethod]`s inherit to per-backend subclasses. **This is where the new parallel flag goes.**
- `SpawnDev.UnitTesting/ConsoleRunner.cs` + `UnitTestRunner.cs`: no-arg = print test list; one-arg = run + emit `TEST: {json}`. **This is where console-side metadata must be emitted.**
- Backend is encoded in the **class name** (`TestTypeName`). Confirm exact names in Step 0 (expected: `WebGPUTests`, `WebGLTests`, `WasmTests` in the Demo; `CudaTests`/`CUDATests`, `OpenCLTests`, `CPUTests` in DemoConsole — VERIFY, don't assume).

### 1c. Reference topology (confirmed)
All three consumers use **`PackageReference` to the local NuGet feed**, NOT ProjectReference:
- `PlaywrightMultiTest.csproj:35` → `SpawnDev.UnitTesting.Blazor` 2.5.2
- `SpawnDev.ILGPU.DemoConsole.csproj:16` → `SpawnDev.UnitTesting` 2.5.2
- `SpawnDev.ILGPU.Demo.Shared.csproj:15` → `SpawnDev.UnitTesting.Blazor` 2.5.2

⇒ Library changes ship as a **new local nuget version**; consumers bump the version string. **TJ's uncommitted WIP in `SpawnDev.UnitTesting` is never touched.**

### 1d. WIP hazard
`D:\users\tj\Projects\SpawnDev.UnitTesting\SpawnDev.UnitTesting` (git root is the INNER folder) is on `master @ b70605b` (v2.5.2) with **29 uncommitted changes** — TJ was mid-refactor building a `SpawnDev.UnitTesting.Coordinator` (a library-ized PMT). Likely does not build. **Do not stash, do not commit, do not build in place.** Use a clean worktree (Step 2).

---

## Step 0 — MEASURE (mandatory first action; Rule 4b)

Do not design the schedule before knowing where the hours go.

1. Find the most recent full-sweep results:
   - `.trx` files: `PlaywrightMultiTest/TestResults/*.trx` (per-test durations).
   - `_dump/latest.json` (browser side live results) and any `test-run-*.json` in the debug dump folder.
2. Write a small `.cs` script (`dotnet run measure.cs`, per Rule 9 — no PowerShell prompt) that parses the newest `.trx` and produces, **grouped by inferred backend (from TestTypeName prefix):**
   - count of tests, sum of durations, top-20 longest tests.
   - the total per-backend wall-clock if run sequentially.
3. Produce the table: `backend | #tests | total_s | top test`. This determines feasibility of the ~25% target. The expected wall-clock model:

   `parallel_wall ≈ max(WebGPU_total, WebGL_total, desktop_total / desktop_parallelism) + Wasm_total`

   - If the 6 backends are ~equal (~45 min each), Phase-A collapse → ~45 min + Wasm ~45 min ≈ **~33%**. Hitting **25%** then *requires* also shrinking Wasm (tiering / `GpuTestVerify` / dropping redundant 2M+4M variants from the fast tier) OR cautiously overlapping the Wasm lane with GPU-only lanes (validate empirically — do NOT assume safe).
   - If one backend dominates, the plan adapts. **Record the real numbers in this file before Step 3.**
4. Also dump the distinct `TestTypeName` values from a real enumeration (run PMT enumeration only, or scrape latest.json) to lock the exact backend→class-name mapping.

**Deliverable of Step 0:** the measured per-backend distribution appended to this file. Everything downstream is tuned to it.

### Step 0 RESULTS (measured 2026-05-25, Tuvok)

**Data sources & honesty note.** No *recent* full-sweep `.trx` exists (every May run in `TestResults/` is scoped/debug — the heavy sort battery is skipped, so they finish in seconds-to-minutes). The most recent **complete** all-backend sweep is `ALL_BACKENDS_RETRY_20260322_210018.trx` (**March 22, ~2 months stale**). I used it for the structural distribution and flag the staleness explicitly. Parser scripts: `PlaywrightMultiTest/TestResults/measure-step0.cs` + `scan-all-trx.cs` (run via `dotnet run <script>.cs <trx>`).

**March 22 full sweep — 1684 results, 33:17 elapsed span (sum-of-durations == span ⇒ backends ran as strictly sequential contiguous blocks, confirming the serial bottleneck):**

| backend | #tests | block time | top test |
|---------|-------:|-----------:|----------|
| **WasmTests** | 252 | **09:42** | RadixSortSpawnSceneSimulation (1:14) |
| **CPUTests** | 233 | **09:23** | RadixSortSpawnSceneSimulation (1:05) |
| WebGPUTests | 241 | 04:34 | RadixSortSpawnSceneSimulation (0:21) |
| WebGPUNoSubgroupsTests | 233 | 04:25 | RadixSortSpawnSceneSimulation (0:21) |
| CudaTests | 233 | 02:09 | RadixSortDescending4M (0:0.8) |
| OpenCLTests | 233 | 02:04 | RadixSortDescending4M (0:0.8) |
| WebGLTests | 254 | 00:54 | BVHRayTraversal (0:0.5) |
| DefaultTests | 3 | ~0 | — |

**Wall-clock drivers (top 25 all RadixSort large-element variants):** WasmTests + CPUTests RadixSort {SpawnScene 1.4M, RepeatedResort, Descending 4M/2M/1.4M, ThresholdProbe, BarrierIsolation, OddCount, Sentinels}. WebGL/CUDA/OpenCL run the same sorts in **sub-second** (CUDA/OpenCL fast GPU; WebGL *skips* shared-mem/barrier algorithm tests entirely → its 0:54 is all non-sort).

**THE LINCHPIN FINDING — Wasm is an irreducible floor, and it caps the achievable win:**
- Backends today run sequentially ⇒ total = Σ(blocks). Parallelizing collapses Phase A to `max(non-Wasm lanes)`, then Wasm runs alone in Phase B.
- **Wasm cannot be parallelized** (pure-spin barrier oversubscription starvation — hard constraint #1). So `parallel_floor ≥ Wasm_block`.
- In March, Wasm = 9:42 / 33:17 = **29% of total**. ⇒ Even with *perfect* parallelization of the other 7 lanes, the best achievable is ~29%. **The 25% target is UNREACHABLE by parallelism alone — it requires also shrinking the Wasm lane** (tiering the redundant mega-sorts out of the fast tier, `GpuTestVerify` instead of CPU readback, dropping duplicate 2M/4M variants). This was a plan hypothesis; it is now confirmed against real data.
- Realistic model with Phase A parallelized + CPU lane run ~4-wide (its 9:23 is a few big sequential sorts → parallelizes well): Phase A ≈ max(CPU≈3-4min after 4-wide, WebGPU 4:34, WebGPUNoSub 4:25) ≈ ~4.5 min; + Wasm 9:42 ≈ **~14 min vs 33 = ~43%**. To beat that, cut Wasm.

**Scope growth since March (explains 33min→4.5hr):** base test methods went **~211 → 609** (`Demo.Shared/UnitTests/`), the biggest single add being **169 P2P tests** (`BackendTestBase.P2PTests.cs`). 609 base × ~7 backend subclasses (P2P likely browser-subset) ⇒ far more than 1684, and the heavy-sort battery now repeats across a larger matrix. The *distribution shape* (Wasm+CPU dominate via RadixSort) is structural and won't have inverted, but the **absolute Wasm share at 4.5hr scale is unverified** — see the open measurement decision below.

---

## Step 2 — SpawnDev.UnitTesting: add the parallel attribute (clean worktree)

> Sequenced before Step 3 because PMT consumes the new metadata. (Step 1 = the reference-topology check, already done in §1c.)

1. Create a clean worktree from the committed stable, isolated from TJ's WIP:
   ```
   cd D:\users\tj\Projects\SpawnDev.UnitTesting\SpawnDev.UnitTesting
   git worktree add ..\..\SpawnDev.UnitTesting-parallel b70605b
   ```
   (Sibling folder `D:\users\tj\Projects\SpawnDev.UnitTesting-parallel`. If worktree is awkward, `git clone` the GitHub remote into that folder at the v2.5.2 tag instead. Either way: a clean tree, never TJ's dirty one.)
2. In `TestMethodAttribute.cs`, add (additive, default preserves today's behavior):
   ```csharp
   /// <summary>
   /// When false, this test must never run concurrently with any other test
   /// (perf benchmarks, tests touching shared global state / fixed ports / fixed files).
   /// Default true. Backend-level policy still applies on top of this
   /// (e.g. all Wasm tests are forced serial by the runner regardless of this flag).
   /// </summary>
   public bool Parallel { get; set; } = true;
   ```
   (Decision: a single `bool Parallel` is enough for v1. A future `string? ParallelGroup` can serialize sets of tests against each other if needed — defer unless Step 0 shows a need.)
3. Surface `Parallel` on the runtime test model (`UnitTest` / whatever `UnitTestRunner` exposes per test) so both renderers can read it. Confirm the model field name when in the file.
4. **Emit metadata at both enumeration seams:**
   - **Blazor** (`UnitTestsView.razor`): add `data-parallel="@test.Parallel.ToString().ToLower()"` (and optionally `data-category="@test.Category"`) to the `<tr>`. Keep existing classes intact.
   - **Console** (`ConsoleRunner`/`UnitTestRunner` list mode): change the no-arg list output so each line carries the flag, e.g. `ClassName.MethodName\tparallel=true\tcategory=Stress` OR emit a JSON manifest line. Keep it parseable AND backward-tolerant (PMT must still work if the field is absent → default `Parallel=true`).
5. Build the worktree: `dotnet build SpawnDev.UnitTesting.slnx`. Fix anything. (Clean tree — should build; the WIP breakage is not here.)
6. Local-publish **2.5.3-local.1** to `D:\users\SpawnDevPackages` via `_publish-unittesting-*-nuget.local.release.bat` (standing authority; post the 1-line DevComms after). Bump the package version in the worktree's csprojs to `2.5.3-local.1` first.
7. Post DevComms summary (package + version + 1-liner) so Geordi/others see the local publish.

---

## Step 3 — PMT scheduler (the core change, in the ILGPU repo)

### 3a. Architecture decision (recommended)
**Move execution out of NUnit's per-case serial path; let PMT own a parallel scheduler; keep NUnit as a thin result reporter.**

- In `OneTimeSetUp` (after enumeration), run the **entire parallel schedule** (Step 3b) and cache each test's verdict in a `ConcurrentDictionary<string, TestOutcome>`.
- `RunTest(ProjectTest test)` becomes: look up the cached outcome, record it (already recorded during the scheduled run), and `Assert.Pass/Fail/Ignore` accordingly. NUnit still shows per-test pass/fail in the explorer/trx; wall-clock is spent in OneTimeSetUp.
- **Why this design:** NUnit cannot selectively parallelize individual `TestCaseSource` cases (parallelism is per-fixture/per-method, not per-case), so fine-grained "these parallel, those serial, Wasm isolated" control is impossible inside NUnit's scheduler. Owning the scheduler gives deterministic control over the hard constraints. (Alternative considered: one NUnit fixture per lane with `[Parallelizable]` + `LevelOfParallelism` — rejected: NUnit's non-parallel/parallel overlap guarantees are too murky to guarantee Wasm isolation.)

### 3b. Lane model + phase schedule
Classify every enumerated test into a **lane** by inferred backend (from `TestTypeName`, mapping locked in Step 0):

| Lane | Mechanism | Intra-lane concurrency | Notes |
|------|-----------|------------------------|-------|
| WebGPU | 1 browser page, clicks WebGPU rows | 1 (sequential) | optional 2nd page only if Step 0 shows GPU headroom AND no perf-test skew |
| WebGL | 1 browser page, clicks WebGL rows | 1 (sequential) | |
| Wasm | 1 browser page, clicks Wasm rows | 1 (sequential) | **isolated phase — see schedule** |
| CUDA | subprocess per test | cap 1–2 | GPU-mem bound; 4M sorts may need cap 1 |
| OpenCL | subprocess per test | cap 2 | |
| CPU | subprocess per test | cap 4 (tune to cores) | |

- Each browser lane opens its **own `Page` in the shared `BrowserContext`** (same origin/server), navigates to `/tests`, and the lane's worker clicks only its backend's rows (the page has all rows; lane filters by `TestTypeName`). **Minimal browser change** — no UI change needed for the lane split itself.
- The `Parallel=false` attribute (Step 2) governs **intra-lane** parallelism: a lane that allows concurrency >1 must still run `Parallel=false` tests alone (drain in-flight, run it solo, resume). Default browser lanes are concurrency 1 so the flag mainly bites the desktop subprocess lanes.

**Phase schedule (default = safe):**
- **Phase A (parallel):** WebGPU lane ∥ WebGL lane ∥ CUDA ∥ OpenCL ∥ CPU. Bounded by a global `SemaphoreSlim` per lane + a small overall GPU governor for the GPU-bound lanes. No Wasm.
- **Phase B (isolated):** Wasm lane runs **alone** (no desktop CPU lane active — Wasm pure-spin wants all cores). 
- The 3 hardcoded P2P two-tab browser integration tests: keep them in their own short serial step (they open extra tabs and do real WebRTC; do not overlap with GPU perf lanes).
- Phase B is the irreducible floor set by the hardware constraint. If Step 0 shows Wasm dominates, attack it with tiering / `GpuTestVerify` / dropping redundant mega-sort variants from the fast tier — NOT by oversubscribing.

### 3c. Concurrency governor
- Per-lane `SemaphoreSlim(cap)`. A global GPU governor (`SemaphoreSlim`) shared by WebGPU+WebGL(+CUDA+OpenCL if Step 0 shows GPU OOM) to bound simultaneous GPU-heavy dispatches.
- `Task.WhenAll` across lanes within a phase; phases run in sequence.
- All caps live in `TestRunnerConfig` (or a new `ParallelConfig`) so TJ can tune without recompiling logic. Provide an env-var / CLI override (e.g. `--parallel=off` to fall back to today's pure-sequential path for debugging).

### 3d. Backend inference helper
Add `static Backend InferBackend(string testTypeName)` in PMT mapping the Step-0-confirmed class names → lane. Unknown/ambiguous → safest lane (serial). Log any unmapped type names loudly (don't silently misroute).

---

## Step 4 — Results writer must be concurrency-safe
- `TestResultsWriter.RecordResult` is now called from multiple lane tasks. Make it thread-safe (lock around the shared list/file write). Verify final counts (`pass/fail/skip`) equal the test population.
- Browser `latest.json`: with multiple browser pages each running `UnitTestsView`, the per-page OPFS `latest.json` writes would collide. Options: (a) give each lane page a distinct `ResultsDirectory` subfolder; (b) rely on PMT-side `TestResultsWriter` as authoritative and accept browser `latest.json` reflects only one lane. Recommend (b) for v1 (PMT side is already authoritative) + note it in `_DEBUG_README`.

---

## Step 5 — Validation (Rule 5 / 5c — prove it, don't claim it)
1. **Baseline:** locate or run one known-good **sequential** full sweep; capture its exact pass/fail/skip set (test names).
2. **Parity:** run the parallel sweep; assert the set of executed test names is IDENTICAL (no test dropped, none double-counted) and the pass/fail/skip verdicts match the baseline. A test that flips Pass→Fail under parallelism = a real contention bug to fix (likely a `Parallel=false` candidate or a GPU-OOM cap), NOT something to ignore.
3. **Wasm isolation proof:** confirm via timing/log that no two Wasm tests overlapped and (default) no CPU lane overlapped Phase B. Watch for any Wasm starvation/timeout — if seen, the isolation was violated; tighten it.
4. **Wall-clock:** record parallel total vs the 4.5–5 hr baseline; compare to the Step-0 model. Report the real ratio honestly (if it's 33% not 25%, say so and state what closing the gap requires).
5. Scoped dry-run first using the proven filter technique from `[[eod-2026-05-24-tuvok-499-shipped-test-speed-next]]` before committing to a full multi-hour run.

---

## Risks / open questions (resolve during execution, don't guess)
- **R1:** Does a single Wasm test starve when a WebGPU/WebGL lane runs concurrently? Default assumes YES (isolate). If Step-0 math needs it, test empirically before relaxing.
- **R2:** Multiple browser `Page`s in one persistent context — any shared-state bleed between lanes' test runs (global JS, IndexedDB, the demo's debug-folder singleton)? Verify the demo's per-test run is page-local.
- **R3:** CUDA GPU-mem OOM when a CUDA sort test overlaps WebGPU/WebGL on the same card. Governor cap mitigates; tune from Step 0.
- **R4:** `ProcessRunner` location/behavior under concurrency (it's referenced in `ProjectRunner.cs` but defined elsewhere — likely SpawnDev.UnitTesting). Confirm it's safe to launch N concurrently (no shared temp file / fixed port).
- **R5:** The console list-output format change (Step 2.4) must stay backward-compatible so a half-updated state still runs.

## Rollback / safety
- `--parallel=off` env/CLI flag restores today's exact sequential path. Keep it until parity (Step 5) is proven over multiple runs.
- Library change is a NEW local version; reverting = bump consumers back to 2.5.2. Worktree keeps TJ's WIP pristine.

## DevComms / housekeeping
- Post to Geordi: every file touched in PMT (ILGPU repo) and the new SpawnDev.UnitTesting local package (his lane; Tuvok editing under Captain's directive).
- Update `active-agents.md` + `nuget-local-publish-log.md` on the local publish.
- After Step 0, **append the measured per-backend distribution to this file** before designing final caps.

---

## Quick file index (so the execution session jumps straight in)
- `PlaywrightMultiTest/UnitTest1.cs` — NUnit fixture (becomes thin reporter).
- `PlaywrightMultiTest/ProjectRunner.cs` — `Init()` enumeration + publish + browser/console branches; add scheduler here.
- `PlaywrightMultiTest/ProjectTest.cs` — `RunTest(IPage)` browser click-run; lane workers reuse this per page.
- `PlaywrightMultiTest/TestableBlazorWasm.cs` — add per-lane `Page` handling.
- `PlaywrightMultiTest/TestResultsWriter.cs` — make thread-safe.
- `PlaywrightMultiTest/TestRunnerConfig.cs` — add parallel caps/config.
- Worktree `SpawnDev.UnitTesting-parallel/`: `SpawnDev.UnitTesting/TestMethodAttribute.cs`, `…/ConsoleRunner.cs`, `…/UnitTestRunner.cs`, `SpawnDev.UnitTesting.Blazor/UnitTestsView.razor`.
