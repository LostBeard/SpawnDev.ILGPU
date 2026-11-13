# WgslScreen

Offline WGSL screen for SpawnDev.ILGPU. It generates WGSL for many kernels without a GPU or a browser, validates the
result with naga, and measures how big each shader is after every function call is inlined. It was written to compare
a build with a WebGPU IR pass switched on against the same build with the pass switched off
(`WebGPUBackend.NarrowInt64LowWordArithmetic`), but the machinery is generic.

## What it measures and why

- **Generation.** Each kernel-shaped static method (first parameter is an `Index1D/2D/3D` or `LongIndex1D/2D/3D`) goes
  through `SpawnDev.ILGPU.ShaderCompiler.Generate` with the WebGPU baseline capability profile and a workgroup size of
  64. A crash or an exception in one kernel is recorded and does not stop the run.
- **Inlined size.** On Windows, Chrome translates WGSL with Tint and hands the result to the D3D shader compiler, which compiles the fully inlined shader, not
  the WGSL text as written. A helper function called 40 times costs 40 copies there. `wgsl_inline.py` computes, per
  shader, the line count of the entry point with every call expanded. This is the number that tracks compile time, and
  the plain line count of the WGSL file does not.
- **Validation.** naga checks each generated WGSL file offline. An invalid shader is a shader Chrome would reject.
- **ON versus OFF.** Every group is run twice, once with the default (ON) and once with the switch set to false (OFF),
  and `report.py` compares the two: status changes, naga changes, byte-identical shaders, size totals.

## Run

```
Scripts\WgslScreen\run.cmd [outputRoot] [consumerDir] [ilgpuBinDir]
```

Requirements: `dotnet` (net10.0 SDK), `python` on PATH, naga. Build `SpawnDev.ILGPU.DemoConsole` in Release first;
its output folder is the default source of the ILGPU binaries.

| Setting | Argument | Environment variable | Default |
|---|---|---|---|
| Output root | 1st | - | `%TEMP%\wgslscreen` |
| Consumer assembly folder | 2nd | `WGSLSCREEN_CONSUMER_DIR` | SpawnDev.Codecs DemoConsole Release folder, only if it exists |
| ILGPU binaries to snapshot | 3rd | `WGSLSCREEN_ILGPU_DIR` | `..\..\SpawnDev.ILGPU.DemoConsole\bin\Release\net10.0` relative to this folder |
| Consumer assembly file name | - | `WGSLSCREEN_CONSUMER_DLL` | `SpawnDev.Codecs.dll` |
| naga | - | `NAGA_EXE` | `naga.exe` on PATH, then `%USERPROFILE%\.cargo\bin\naga.exe` |
| Resume | - | `WGSLSCREEN_RESUME=1` | off |

If the consumer folder or DLL does not exist, the consumer run is skipped with a message and the rest still runs.
With `WGSLSCREEN_RESUME=1` the snapshot and result folders are kept and the driver skips kernels already recorded in
`results.tsv`; the build is still redone. Exit code 0 means every step finished. Exit code 4 means ILGPU was loaded
from somewhere other than the snapshot, and the numbers must not be used.

Steps, one process at a time: snapshot, build, generate (suite ON, suite OFF, consumer ON, consumer OFF), naga on each
folder, analyzer on each folder, report.

The tool can also be run directly after a build, for example to screen another assembly or switch:

```
Screen.exe <assembly.dll> <typeRegex> <methodRegex> <outDir> --refs <snapshotDir> [--no-narrow]
           [--switch-off <Type.FullName>.<StaticBoolMember>]... [--only <name>] [--skip-file <file>]
           [--batch N] [--timeout SECONDS]
```

`--no-narrow` is shorthand for `--switch-off SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic`.
`--switch-off` sets any public or non-public static bool property or field of the ILGPU, ILGPU.Algorithms or
SpawnDev.ILGPU assemblies (from the snapshot) to false before the first kernel is generated. A name that does not
resolve stops the run before any kernel is generated. `run.cmd` itself only uses `--no-narrow`.

## Where everything goes

Nothing is written into the repository. The project is built with `-o` and `BaseIntermediateOutputPath` under the
output root, and the snapshots live there too.

| Path under the output root | Content |
|---|---|
| `refs\` | Snapshot of the ILGPU binaries. The tool compiles against it and loads the ILGPU family only from here. |
| `consumer\` | Snapshot of the consumer assembly folder. |
| `build\bin\`, `build\obj\` | Tool build output, deleted and rebuilt on every run. |
| `suite_on\`, `suite_off\`, `consumer_on\`, `consumer_off\` | One folder per run: the generated `*.wgsl` files plus the files below. |
| `<run>\results.tsv` | One row per kernel: type, method, status, WGSL lines and bytes, generation ms, exception type and message, file name, id. Status is OK, EXCEPTION, CRASHED, TIMEOUT or SKIPPED-GENERIC. The driver resumes from it. |
| `<run>\loaded_assemblies.txt` | Proof of where each ILGPU assembly was loaded from. Each line must end in `[refs OK]`. |
| `<run>\naga.tsv`, `<run>\naga_out\` | naga verdict per file (valid or invalid with the first error line), and the full naga text for each invalid file. |
| `<run>\exceptions\` | Full exception text for each failed kernel. |
| `<run>\child.log` | Output of the child processes. |
| `analyzer_<group>_on.txt`, `analyzer_<group>_off.txt` | Per-shader table from `wgsl_inline.py`: WGSL lines, bytes, function count, fully inlined lines, ratio. |
| `report.txt` | The comparison: status counts, kernels whose status or naga result differs, byte-identity, size totals, size table for the kernels whose WGSL differs, exceptions that match the pass's own error patterns, loaded-assembly proof. |

## How the driver works

The parent process lists the kernels and runs child processes of up to 25 kernels each. A child writes the id of the
kernel in progress to `_current.txt`. If a child exits abnormally the parent records that kernel as CRASHED; if one
kernel runs longer than 120 seconds the parent kills that child (by its own recorded PID only) and records TIMEOUT.
Then it continues with the rest.

## Per-kernel table (`kernel_table.py`)

```
python -P Scripts\WgslScreen\kernel_table.py Scripts\WgslScreen\wgsl_inline.py <run folder> <out.tsv>
```

One row per `*.wgsl` of a run folder, largest first: WGSL lines and bytes, fully inlined lines, the number of
emulated 64-bit operations after inlining (calls of `i64_*` / `u64_*` / `f64_*` from other code, times the copies of
the caller) and the inlined lines they expand to, the bytes of function-scope arrays (the NVIDIA large-array flush
engages above `WebGPUBackend.NvidiaLargeFunctionArrayFlushBytes`), loop count, the naga verdict when `naga.tsv` is in
the folder, and the three helpers with the most inlined lines (own lines x copies).

## Compile probe (`CompileProbe\`): what the browser pays for a shader

The line counts above are a proxy. `CompileProbe` measures the real thing for a set of WGSL files, without running
any kernel: one fresh Chrome per shader, `createShaderModule` + `getCompilationInfo` (Tint: parse and validate),
then `createComputePipelineAsync` (the backend compile: D3D12 on Windows), and the private bytes of that Chrome's
GPU process before the compile and at its peak.

```
cd <folder holding compile_one.html and the WGSL files>
dotnet run <repo>\Scripts\WgslScreen\CompileProbe\compileprobe.cs -- <rootDir> <out.tsv> <limitGB> <reps> <rel\a.wgsl> <rel\b.wgsl> ...
python -P <repo>\Scripts\WgslScreen\CompileProbe\ab_summary.py <out.tsv> [table_before.tsv table_after.tsv]
```

- `rootDir` is served on `http://localhost:<free port>/` by the driver and must contain `compile_one.html` (copy it
  there) and the WGSL files. Use a folder outside the repository.
- It uses the GPU (compile only, nothing is dispatched) and opens a small Chrome window per shader: agree the GPU
  with the other agents first, as for any browser run.
- Guard: the driver reads the GPU process id of ITS OWN browser from CDP (`SystemInfo.getProcessInfo`) and closes
  that browser when the GPU process passes `limitGB` (status `KILLED`). It never touches another process. Use 3 for
  a new giant kernel.
- `--disable-gpu-shader-disk-cache` and a fresh profile per launch: every compile is cold.
- `reps` repeats the whole file list; list the files of two versions alternately (`v1/k.wgsl v2/k.wgsl ...`) for an
  interleaved A/B. `ab_summary.py` expects the version to be the first folder of the relative path and prints the
  median and range per kernel and version. The backend compile varies run to run (10% is common): one run under
  about 15% apart is not a result.
- It is a file-based app (`#:package Microsoft.Playwright`); that is allowed here because it does not create an ILGPU
  context (project rule 9 is about ILGPU / GPU compute in file-based apps). It needs Chrome installed (channel
  `chrome`).
- A shader that compiles here can still fail or hang when it runs: nothing is dispatched.

## Known limits

- naga does not check WGSL uniformity rules (barriers or derivative-like operations in non-uniform control flow) and
  does not detect an atomic variable that is read as a plain value. A shader can be naga-valid and still be rejected by
  Chrome.
- 41 suite kernels need a launch-time specialization or take delegates. `Generate` throws for them, identically in
  both modes, so they count as EXCEPTION in both and carry no information about the switch. 10 more are generic and
  are recorded as SKIPPED-GENERIC without being generated.
- The inlined size is computed from the WGSL text (function definitions, call counts, line spans). It is a line count,
  not a measurement of D3D compile time. The analyzer asserts on duplicate function names, unbalanced braces, cycles,
  and anything other than exactly one `@compute` entry point.
- Only the WebGPU WGSL path is covered. Nothing here exercises WebGL, Wasm, CUDA or OpenCL output, and nothing runs a
  shader, so numerical results are not checked.
- It is a project (`Screen\Screen.csproj`), not a file-based `dotnet run x.cs` app, because a file-based app runs with
  dynamic code disabled and ILGPU cannot create its context there (project rule 9). A project also gives the snapshot
  references and the separate output folders a place to be declared.
- The project is not in `SpawnDev.ILGPU.slnx`, and it has no `PlaywrightMultiTest` element or Playwright package, so
  neither the solution build nor PlaywrightMultiTest project discovery picks it up.
- Repo `.gitignore` ignores `Scripts/` except this folder (see the two lines for `Scripts/*` and `!Scripts/WgslScreen/`).
