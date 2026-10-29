# Upstream report draft: WebGPU on D3D12 / NVIDIA loses the device (DXGI_ERROR_DEVICE_HUNG) replaying a recorded call sequence

Status: **draft, not filed.** Written 2026-10-29 to be filed together with
[nvidia-d3d12-function-array-store-upstream-report.md](nvidia-d3d12-function-array-store-upstream-report.md). SpawnDev.Codecs
works around it (a queue drain after one kernel, Codecs 8d0efe6), so nothing of ours waits on a fix.

Where it could go:

- **Chromium / Dawn** (issues.chromium.org, component `Blink>WebGPU`). First stop: the reproducer is a plain WebGPU call trace,
  and Dawn can tell whether the hang is in its D3D12 backend or in the driver.
- **NVIDIA** (developer forums or the NVIDIA developer bug form), if Dawn routes it there.

Attachments: the folder `D:\users\tj\Projects\_upstream_reports\vp9-webgpu-device-hung\` (zip it): `index.html` (replay page),
`replay.js`, `trace.jsonl` (4.9 MB, the recording), `reduced.json` (records to skip for the reduced trace), `tracer.js` (the
recorder, for reference).

---

## Title

D3D12 on NVIDIA (RTX 4070, driver 32.0.15.9621): replaying a recorded sequence of compute dispatches loses the device with
DXGI_ERROR_DEVICE_HUNG

## Summary

A WebGPU page (SpawnDev.Codecs' VP9 encoder tests, compute only) loses its device with `DXGI_ERROR_DEVICE_HUNG`. The trigger is
an order of work, not one shader: the hang needs a compute kernel with workgroup barriers to still be running when the next
dispatches are queued, and it appears on a later, small dispatch that runs fine everywhere else. Every WebGPU call of the failing
page was recorded and the recording is replayed in a fresh page with no application code: the replay hangs the same way, at
the same record.

Error text (from Chrome, both the original page and the replay):

```
ID3D12Device::GetDeviceRemovedReason failed with DXGI_ERROR_DEVICE_HUNG (0x887A0006)
 - While handling unexpected error type Internal when allowed errors are Validation.
    at CheckHRESULTImpl (..\..\third_party\dawn\src\dawn\native\d3d\D3DError.cpp:121)
```

## Environment

| | |
|---|---|
| GPU | NVIDIA GeForce RTX 4070 (`nvidia` / `lovelace`), D3D12 backend (Chrome's default) |
| Driver | 32.0.15.9621 (2026-04-12) |
| OS | Windows 11 Home 10.0.26200 |
| Browser | Chrome 156.0.8078.48 |

## Reproducer

1. Unzip the attachment and serve the folder over `http://localhost` (for example `python -m http.server 8000`).
2. Open `http://localhost:8000/index.html` in Chrome.
3. Click **Replay the full trace**. On the system above the page reports `DEVICE LOST` at record 1288 or 1289 (the screen may
   flash once: Windows resets the display driver).

**Replay the reduced trace** drops 657 of the 1316 records (`reduced.json`) and still hangs.

What the replay does: `replay.js` reads `trace.jsonl`, which `tracer.js` recorded by wrapping the WebGPU prototypes of the
original page: every call in order (`createBuffer`, `writeBuffer` with its data, `createShaderModule`, `createComputePipeline`,
`createBindGroup`, command encoders, compute passes, `copyBufferToBuffer`, `clearBuffer`, `submit`, `mapAsync`, ...), plus the
point at which each promise settled. The replay reissues the calls in record order on a new device and awaits each promise where
the original page saw it settle, so the queue holds the same work at the same times.

Fidelity: the replay of a passing run of the same tests (not attached) reissued all 1160 records with no error and its 10
readbacks were byte-identical to the original page's. The full failing trace replayed twice: both hung at the same record.

## What the reduced trace contains

Besides buffer, shader and pipeline creation and the data uploads, three submits are left. Removing submit 1 or submit 2
(tested before the per-dispatch cuts) made the hang go away; submit 3 holds the dispatch that hangs.

1. One earlier encode's submit, reduced to no dispatches.
2. A submit with `Vp9EncodeWavefrontGroupKernel` (3 workgroups of 4 invocations; `workgroupBarrier()` once per loop iteration)
   followed by one `Vp9EntropyStepKernel` dispatch (1 workgroup) that reads buffers the first kernel writes. Both are required:
   without the second dispatch, or without the first, there is no hang.
3. One later dispatch of `Vp9EncodeWavefrontBatchKernel` (1 workgroup of 64 invocations, 3 of them do work, the rest return at
   once). This is the one that never completes.

In the original application, one `onSubmittedWorkDone()` wait after the barrier kernel (before the second dispatch is queued)
prevents the hang. Waiting 3 s at the test boundary does not.

## Ruled out

- **The shaders.** Every loop in the WGSL of the dispatch that hangs has a constant bound, and dispatched on its own it completes
  in milliseconds on this GPU (the barrier kernel too: its loop counts do not depend on its input data). The same dispatches
  pass when the barrier kernel has finished before the next dispatch is queued.
- **Compile timing** (tested in the application). Creating the barrier kernel's pipeline asynchronously ahead of time does not help.
- **Submission shape** (tested in the application). One compute pass per dispatch versus several dispatches per pass, one
  submit per dispatch versus one per batch: no difference.
- **Memory.** Chrome's GPU process peaked at about 2 GB of private memory on a 16 GB system.
- **The application's runtime.** The replay runs no application code.

Not yet narrowed: whether Dawn's D3D12 backend or the driver is at fault. A different GPU vendor was not available to compare.
