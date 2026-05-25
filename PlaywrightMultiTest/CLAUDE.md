# PlaywrightMultiTest

Unified NUnit + Playwright test runner. Runs ALL tests (desktop + browser) via `dotnet test`.

## Running Tests

```bash
# Full sweep with timestamped results
timestamp=$(date +%Y%m%d_%H%M%S) && dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --logger "trx;LogFileName=results_${timestamp}.trx" --results-directory PlaywrightMultiTest/TestResults

# Scoped dev run — use PMT_FILTER (substring), NOT --filter (see gotcha below)
PMT_FILTER=WasmTests.KernelTest dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj
```

## How It Works
- `ProjectDiscovery` scans for `<PlaywrightMultiTest>` element in `.csproj` files
- **Blazor WASM**: publishes app, starts HTTPS static file server, launches Chromium (with `--enable-unsafe-webgpu`), navigates to test page, enumerates tests from DOM
- **Console/Exe**: publishes app, runs binary as subprocess per test (via `SpawnDev.UnitTesting.ProcessRunner`)
- Tests surfaced as NUnit `TestCaseSource`

## Parallel Scheduler (default ON)
Execution runs in `ProjectRunner.StartUp` (NUnit `OneTimeSetUp`) as backend **lanes**; `UnitTest1.RunTest` then just reports each cached `ScheduledOutcome` (so the trx + `playwright-latest.json` stay correct without re-running).
- **Phase A (parallel):** browser non-Wasm rows (sequential on the ONE shared Chromium page) ‖ CPU subprocs (cap 4) ‖ CUDA (cap 1) ‖ OpenCL (cap 1). The desktop matrix runs hidden under the browser lane, which is the Phase A long pole.
- **Phase B (isolated):** Wasm rows alone — Wasm pure-spin barrier workers STARVE under CPU oversubscription, so Wasm never overlaps any other CPU-heavy lane.
- Lane classify: `DesktopLaneOf(TestTypeName)` (Cuda/CuRand/NvJpeg→cuda, OpenCL→opencl, else cpu); `IsWasm` = TypeName contains "Wasm".

**Env switches:**
- `PMT_PARALLEL=off` → original sequential per-case path (escape hatch).
- `PMT_FILTER=<substring>` → scopes the scheduled set (matches Name/TypeName/MethodName).
- `PMT_CPU_PARALLELISM` (4) · `PMT_CUDA_PARALLELISM` (1) · `PMT_OPENCL_PARALLELISM` (1) — per-lane caps. GPU lanes default 1 to avoid OOM/contention with the browser WebGPU lane sharing the card.

**⚠ `--filter` GOTCHA:** with the scheduler ON, `dotnet test --filter` does NOT scope EXECUTION — the NUnit adapter consumes `--filter` before it reaches this testhost, and the scheduler already ran the full enumerated set in `StartUp`. `--filter` only narrows what NUnit *reports*. **Use `PMT_FILTER` for scoped runs.**

## Key Constraints
- **Blazor WASM publish** takes under 2 minutes — anything longer means it's hung
- **Subprocess output capture**: `ProcessRunner.Run` waits for the stream-EOF sentinels (`OutputDataReceived`/`ErrorDataReceived` fire with `e.Data == null` at EOF) before reading captured output, bounded by a 5s cap. A timed `WaitForExit(N)` alone does NOT drain async readers — without the sentinel wait the final `TEST: {json}` line is lost under concurrent load → spurious "Test run failed" (fixed in SpawnDev.UnitTesting 2.5.3).
- **Blazor error detection**: checks `#blazor-error-ui` before/after each test
- **Console capture**: browser console errors/warnings via Playwright `page.Console` event
- **Never start duplicate test processes** — log timing, track carefully (one PMT invocation per editor at a time; pre-flight `Get-Process testhost` first)
