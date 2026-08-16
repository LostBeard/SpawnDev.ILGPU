using SpawnDev.ILGPU.Wasm.Backend;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Services;

/// <summary>
/// Auto-dumps all generated WGSL, GLSL, and Wasm to a user-selected local folder.
/// Directory handle persisted in IndexedDB — set once, works across sessions.
/// When active, every shader compilation automatically writes to disk.
/// Each test run gets a timestamped subfolder for comparison across runs.
/// </summary>
public class ShaderDebugService : IAsyncBackgroundService, IAsyncDisposable
{
    /// <summary>Framework awaits this before pages load — restores debug folder from IDB.</summary>
    public Task Ready => _ready ??= TryRestoreAsync();
    private Task? _ready;

    private readonly SpawnJSRuntime _js;
    private FileSystemDirectoryHandle? _debugDir;
    private FileSystemDirectoryHandle? _runDir;
    private FileSystemDirectoryHandle? _wgslDir;
    private FileSystemDirectoryHandle? _glslDir;
    private FileSystemDirectoryHandle? _wasmDir;
    private int _wgslCount;
    private int _wasmCount;
    private int _glslCount;
    private const string DB_NAME = "spawndev_ilgpu_debug";
    private const string STORE_NAME = "handles";
    private const string KEY = "debugFolder";

    public FileSystemDirectoryHandle? DebugDirectory => _debugDir;
    /// <summary>The current run's timestamped subfolder (created on first shader compile or StartNewRun).</summary>
    public FileSystemDirectoryHandle? CurrentRunDirectory => _runDir;
    /// <summary>Timestamp string for the current run (e.g., "2026-03-24_14-32-15").</summary>
    public string? CurrentRunTimestamp { get; private set; }
    public bool HasDebugFolder => _debugDir != null;
    public bool HasReadPermission { get; private set; }
    public bool HasWritePermission { get; private set; }
    public bool NeedsPermissionGrant => _debugDir != null && !HasWritePermission;
    public string? FolderName { get; private set; }

    private bool _restoreAttempted;

    public ShaderDebugService(SpawnJSRuntime js)
    {
        _js = js;
        // Hook into ALL backends for auto-dump
        WebGPUBackend.OnShaderCompiled += OnWGSLCompiled;
        WasmBackend.OnKernelCompiled += OnWasmCompiled;
        WebGLAccelerator.OnShaderCompiled += OnGLSLCompiled;
    }

    /// <summary>
    /// Timestamp captured when this service (the app/page) loaded — it is a singleton, so this is fixed for the
    /// whole app session. The auto-created run folder uses it, and a results writer (e.g. UnitTestsView) can read
    /// it so the shader-dump folder and the test-results file share ONE timestamp per app load (each page reload =
    /// a new app instance = a new value). Format matches the run folder: "yyyy-MM-dd_HH-mm-ss".
    /// </summary>
    public string AppLoadTimestamp { get; } = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");

    /// <summary>Auto-restore on first callback if not already restored.</summary>
    private async Task EnsureRestoredAsync()
    {
        if (_restoreAttempted || _debugDir != null) return;
        _restoreAttempted = true;
        await TryRestoreAsync();
    }

    /// <summary>Try to restore persisted debug folder on startup.</summary>
    async Task TryRestoreAsync()
    {
        try
        {
            using var db = await GetDB();
            using var tx = db!.Transaction(STORE_NAME, false);
            using var store = tx.ObjectStore<string, FileSystemHandle>(STORE_NAME);
            var fsHandle = await store.GetAsync(KEY);
            if (fsHandle == null) return;
            var handle = fsHandle.ToFileSystemDirectoryHandle(true);
            if (handle == null) return;
            _debugDir = handle;
            FolderName = handle.Name;
            _wgslCount = 0;
            _wasmCount = 0;
            _glslCount = 0;
            // Check if we already have write permission (no prompt)
            HasReadPermission = await handle.VerifyPermission(readWrite: false, askIfNeeded: false);
            HasWritePermission = await handle.VerifyPermission(readWrite: true, askIfNeeded: false);
            if (WebGPUBackend.VerboseLogging) Console.WriteLine($"[ShaderDebug] Active: {FolderName}, readable: {HasReadPermission}, writable: {HasWritePermission}");
        }
        catch (Exception ex)
        {
            var nmt = true;
        }
    }

    /// <summary>Request write permission via user gesture (trusted click). Call from a button click handler.</summary>
    public async Task<bool> RequestWritePermissionAsync()
    {
        if (_debugDir == null) return false;
        if (HasWritePermission) return true;
        HasWritePermission = await _debugDir.VerifyPermission(readWrite: true, askIfNeeded: false);
        if (HasWritePermission) return true;
        HasWritePermission = await _debugDir.VerifyPermission(readWrite: true, askIfNeeded: true);
        return HasWritePermission;
    }

    /// <summary>Pick a debug output folder. Persists to IndexedDB.</summary>
    public async Task<bool> PickFolderAsync()
    {
        try
        {

            using var window = _js.Get<Window>("window");
            var handle = await window.ShowDirectoryPicker(new ShowDirectoryPickerOptions { Mode = "readwrite" });
            if (handle == null) return false;
            _debugDir?.Dispose();
            DisposeSubDirs();
            _debugDir = handle;
            FolderName = handle.Name;
            HasWritePermission = true; // showDirectoryPicker with readwrite mode grants it
            CurrentRunTimestamp = null;
            _wgslCount = 0;
            _wasmCount = 0;
            _glslCount = 0;
            using var db = await GetDB();
            using var tx = db!.Transaction(STORE_NAME, true);
            using var store = tx.ObjectStore<string, FileSystemHandle>(STORE_NAME);
            await store.PutAsync(handle, KEY);
            if (WebGPUBackend.VerboseLogging) Console.WriteLine($"[ShaderDebug] Active: {FolderName}");
            await WriteReadme();
            return true;
        }
        catch (Exception ex) { if (WebGPUBackend.VerboseLogging) Console.WriteLine($"[ShaderDebug] {ex.Message}"); return false; }
    }

    /// <summary>Clear persisted folder and stop auto-dumping.</summary>
    public async Task ClearAsync()
    {
        _debugDir?.Dispose();
        DisposeSubDirs();
        _debugDir = null;
        FolderName = null;
        try
        {
            using var db = await GetDB();
            using var tx = db!.Transaction(STORE_NAME, true);
            using var store = tx.ObjectStore<string, FileSystemHandle>(STORE_NAME);
            await store.DeleteAsync(KEY);
        }
        catch
        {
        }
    }

    /// <summary>Start a new timestamped run folder. Call before running tests.</summary>
    public async Task<string?> StartNewRunAsync(string? description = null, string? timestampOverride = null)
    {
        if (_debugDir == null || !HasWritePermission) return null;
        // Close previous run's subdirs
        DisposeRunDirs();
        CurrentRunTimestamp = timestampOverride ?? DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        _runDir = await _debugDir.GetDirectoryHandle(CurrentRunTimestamp, create: true);
        _wgslCount = 0;
        _wasmCount = 0;
        _glslCount = 0;
        await WriteRunNotesAsync(description);
        if (WebGPUBackend.VerboseLogging) Console.WriteLine($"[ShaderDebug] New run: {CurrentRunTimestamp}");
        return CurrentRunTimestamp;
    }

    /// <summary>Ensure a run directory exists (auto-creates on first shader compile if not started explicitly).</summary>
    private bool _creatingRun;
    private async Task EnsureRunDirectoryAsync()
    {
        if (_runDir != null) return;
        if (_creatingRun) { while (_runDir == null) await Task.Yield(); return; }
        _creatingRun = true;
        // Use the app-load timestamp so the auto-created shader-dump folder matches the results file's timestamp.
        try { await StartNewRunAsync(timestampOverride: AppLoadTimestamp); }
        finally { _creatingRun = false; }
    }

    private async Task WriteRunNotesAsync(string? description)
    {
        if (_runDir == null) return;
        try
        {
            var lines = new List<string>
            {
                $"# Test Run — {CurrentRunTimestamp}",
                "",
                $"**Started:** {DateTime.UtcNow:O}",
            };
            if (!string.IsNullOrEmpty(description))
            {
                lines.Add($"**Description:** {description}");
            }
            lines.Add("");
            lines.Add("## Notes");
            lines.Add("");
            lines.Add("<!-- Add observations, findings, and context for this run below -->");
            lines.Add("");

            using var fh = await _runDir.GetFileHandle("notes.md", create: true);
            using var ws = await fh.CreateWritable();
            await ws.Write(string.Join("\n", lines));
            await ws.Close();
        }
        catch { }
    }

    // Auto-dump callbacks — fire on every compilation
    private async void OnWGSLCompiled(string name, string source, WGSLEntry entry)
    {
        if (_debugDir == null || !HasWritePermission) return;
        try
        {
            await EnsureRunDirectoryAsync();
            _wgslDir ??= await _runDir!.GetDirectoryHandle("wgsl", create: true);
            var header = $"// Kernel: {entry.KernelName}\n// Workgroup: {entry.WorkgroupSize}\n// SharedMem: {entry.SharedMemoryBytes}B\n// Bindings: {entry.BindingCount}\n// Time: {DateTime.UtcNow:O}\n\n";
            using var fh = await _wgslDir.GetFileHandle($"{_wgslCount++:D3}_{Safe(name)}.wgsl", create: true);
            using var ws = await fh.CreateWritable();
            await ws.Write(header + source);
            await ws.Close();
        }
        catch { }
    }

    private async void OnGLSLCompiled(string name, string source)
    {
        if (_debugDir == null || !HasWritePermission) return;
        try
        {
            await EnsureRunDirectoryAsync();
            _glslDir ??= await _runDir!.GetDirectoryHandle("glsl", create: true);
            using var fh = await _glslDir.GetFileHandle($"{_glslCount++:D3}_{Safe(name)}.glsl", create: true);
            using var ws = await fh.CreateWritable();
            await ws.Write($"// GLSL Kernel: {name}\n// Time: {DateTime.UtcNow:O}\n\n" + source);
            await ws.Close();
        }
        catch { }
    }

    private async void OnWasmCompiled(string name, byte[] binary, string info)
    {
        if (_debugDir == null || !HasWritePermission) return;
        try
        {
            await EnsureRunDirectoryAsync();
            _wasmDir ??= await _runDir!.GetDirectoryHandle("wasm", create: true);
            var idx = _wasmCount++;
            using var fh = await _wasmDir.GetFileHandle($"{idx:D3}_{Safe(name)}.wasm", create: true);
            using var ws = await fh.CreateWritable();
            await ws.Write(binary);
            await ws.Close();

            using var fh2 = await _wasmDir.GetFileHandle($"{idx:D3}_{Safe(name)}.txt", create: true);
            using var ws2 = await fh2.CreateWritable();
            await ws2.Write(info);
            await ws2.Close();
        }
        catch { }
    }
    async Task<IDBDatabase> GetDB()
    {
        return await IDBDatabase.OpenAsync(DB_NAME, 1, Db_OnUpgradeNeeded);
    }
    private void Db_OnUpgradeNeeded(IDBVersionChangeEvent evt)
    {
        try
        {
            var oldVersion = evt.OldVersion;
            var newVersion = evt.NewVersion;
            using var request = evt.Target;
            using var db = request.Result;
            var names = db.ObjectStoreNames.ToArray();
            var stores = db.ObjectStoreNames;
            if (!stores.Contains(STORE_NAME))
            {
                db.CreateObjectStore<string, FileSystemHandle>(STORE_NAME);
            }
        }
        catch (Exception ex)
        {
            var nmt = true;
        }
    }
    private async Task WriteReadme()
    {
        if (_debugDir == null || !HasWritePermission) return;
        try
        {
            using var fh = await _debugDir.GetFileHandle("_DEBUG_README.md", create: true);
            using var ws = await fh.CreateWritable();
            await ws.Write(@"# SpawnDev.ILGPU Debug Output Folder

This folder receives auto-dumped shaders and Wasm binaries from SpawnDev.ILGPU
every time a kernel is compiled. Set via the 'Set Debug Folder' button on the /tests page.

## Folder Structure

```
debugfolder/
├── _DEBUG_README.md    (this file)
├── wgsl/               (WebGPU WGSL shaders)
│   ├── 000_KernelName.wgsl
│   └── ...
├── glsl/               (WebGL GLSL shaders)
│   ├── 000_KernelName.glsl
│   └── ...
└── wasm/               (Wasm backend binaries + info)
    ├── 000_KernelName.wasm
    ├── 000_KernelName.txt
    └── ...
```

## Files Written Automatically

- `wgsl/NNN_KernelName.wgsl` — WGSL shaders (WebGPU) with metadata headers
- `glsl/NNN_KernelName.glsl` — GLSL shaders (WebGL) with metadata headers
- `wasm/NNN_KernelName.wasm` — Wasm binary (disassemble: `wasm2wat --enable-threads file.wasm`)
- `wasm/NNN_KernelName.txt` — Wasm kernel compilation info (params, locals, shared mem, etc.)

## For AI Agents Debugging

If you are a Claude, Gemini, or other AI agent examining this folder:

1. **WGSL files** (`wgsl/`) contain the generated GPU shader code. Look for the `@workgroup_size`
   annotation and the kernel entry point (`@compute @workgroup_size(X) fn main()`). Check for:
   - Correct shared memory declarations (`var<workgroup>`)
   - Barrier placement (`workgroupBarrier()`)
   - PHI variable merge blocks (variables set in multiple branches)
   - Loop structure (loop/continuing/break_if patterns)

2. **GLSL files** (`glsl/`) contain WebGL compute shaders. Check for:
   - Transform feedback output declarations
   - Uniform/varying bindings
   - Precision qualifiers

3. **Wasm files** (`wasm/`) are WebAssembly binaries. Disassemble with:
   ```
   wasm2wat --enable-threads kernel.wasm > kernel.wat
   ```
   Look for:
   - Function signatures (params, locals)
   - `memory.atomic.wait32` / `memory.atomic.notify` for barriers
   - Block/loop/br_table structure for the state machine
   - Shared memory access patterns

## Persistence

The folder handle is saved in IndexedDB. On next browser visit, click 'allow' when
prompted for filesystem permission and the folder is automatically reconnected.
");
            await ws.Close();
        }
        catch { }
    }

    private static string Safe(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars()));

    private void DisposeRunDirs()
    {
        _wgslDir?.Dispose();
        _glslDir?.Dispose();
        _wasmDir?.Dispose();
        _wgslDir = null;
        _glslDir = null;
        _wasmDir = null;
        _runDir?.Dispose();
        _runDir = null;
    }

    private void DisposeSubDirs()
    {
        DisposeRunDirs();
    }

    public async ValueTask DisposeAsync()
    {
        WebGPUBackend.OnShaderCompiled -= OnWGSLCompiled;
        WasmBackend.OnKernelCompiled -= OnWasmCompiled;
        WebGLAccelerator.OnShaderCompiled -= OnGLSLCompiled;
        DisposeRunDirs();
        _debugDir?.Dispose();
    }
}
