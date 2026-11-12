// Offline WGSL screen tool. No GPU, no browser, no network: only SpawnDev.ILGPU.ShaderCompiler.Generate.
//   screen <assemblyPath> <typeNameRegex> <methodNameRegex> <outDir> --refs <dir> [--no-narrow]
//          [--switch-off <Type.FullName>.<StaticBoolMember>]... [--only <exactName>] [--skip-file <path>]
//          [--batch N] [--timeout SECONDS]
//   screen naga <folder> --naga <naga.exe> [--timeout SECONDS]
// Parent mode lists candidates and runs child processes (--child) in batches; a child that dies or exceeds the per-kernel
// timeout is recorded as CRASHED / TIMEOUT and the run continues with the remaining kernels. Resumable via results.tsv.
//
// Why the ILGPU family is loaded from --refs and nowhere else: the point of the tool is to measure one specific build
// of the library. If a stray copy next to the screened assembly were picked up instead, every number would describe
// the wrong binaries, so the resolver pins the family to --refs and VerifyLoaded exits with code 4 on any deviation.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

internal static class P
{
    static readonly string[] Family = { "ILGPU", "ILGPU.Algorithms", "SpawnDev.ILGPU" };
    static readonly HashSet<string> IndexTypes = new(StringComparer.Ordinal)
    {
        "ILGPU.Index1D", "ILGPU.Index2D", "ILGPU.Index3D", "ILGPU.LongIndex1D", "ILGPU.LongIndex2D", "ILGPU.LongIndex3D",
    };
    // --no-narrow is kept as a short alias for this switch (the pass this tool was written to measure).
    const string NarrowSwitch = "SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic";
    const string Header = "type\tmethod\tstatus\twgsl_lines\twgsl_bytes\tgen_ms\texception_type\tmessage\tfile\tid";

    static string RefsDir = "";
    static string? AsmDir;
    static readonly object LogLock = new();

    sealed class Cand
    {
        public string Id = "", TypeFull = "", Method = "", File = "";
        public MethodInfo M = null!;
        public bool Generic;
    }

    static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (ctx, n) => OnResolve(ctx, n);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => OnResolve(AssemblyLoadContext.Default, new AssemblyName(e.Name));

        if (args.Length >= 2 && args[0] == "naga") return Naga(args);
        if (args.Length < 5)
        {
            Console.Error.WriteLine("usage: screen <assemblyPath> <typeNameRegex> <methodNameRegex> <outDir> --refs <dir> [--no-narrow] [--switch-off Type.Member]... [--only <name>] [--skip-file <path>] [--batch N] [--timeout S]\n       screen naga <folder> --naga <naga.exe> [--timeout S]");
            return 2;
        }
        string asmPath = Path.GetFullPath(args[0]);
        string typeRx = args[1], methRx = args[2];
        string outDir = Path.GetFullPath(args[3]);
        bool child = false;
        string? only = null, skipFile = null, onlyList = null, refs = null;
        var switchesOff = new List<string>();
        int batch = 25, timeoutS = 120;
        for (int i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-narrow": switchesOff.Add(NarrowSwitch); break;
                case "--switch-off": switchesOff.Add(args[++i]); break;
                case "--refs": refs = args[++i]; break;
                case "--child": child = true; break;
                case "--only": only = args[++i]; break;
                case "--only-list": onlyList = args[++i]; break;
                case "--skip-file": skipFile = args[++i]; break;
                case "--batch": batch = int.Parse(args[++i]); break;
                case "--timeout": timeoutS = int.Parse(args[++i]); break;
                default: Console.Error.WriteLine("unknown arg " + args[i]); return 2;
            }
        }
        if (refs == null || !Directory.Exists(refs)) { Console.Error.WriteLine("--refs <folder with the ILGPU snapshot> is required and must exist"); return 2; }
        RefsDir = Path.GetFullPath(refs).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(outDir);
        AsmDir = Path.GetDirectoryName(asmPath);

        var asm = Assembly.LoadFrom(asmPath);
        var all = Enumerate(asm, typeRx, methRx);
        return child
            ? RunChild(all, outDir, switchesOff, onlyList!)
            : RunParent(all, asmPath, typeRx, methRx, outDir, switchesOff, only, skipFile, batch, timeoutS);
    }

    // ------------------------------------------------------------------ resolution
    static Assembly? OnResolve(AssemblyLoadContext ctx, AssemblyName n)
    {
        var sn = n.Name;
        if (string.IsNullOrEmpty(sn) || RefsDir.Length == 0) return null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic) continue;
            if (string.Equals(a.GetName().Name, sn, StringComparison.OrdinalIgnoreCase)) return a;
        }
        bool fam = Array.Exists(Family, f => string.Equals(f, sn, StringComparison.OrdinalIgnoreCase));
        var dirs = fam || AsmDir == null ? new[] { RefsDir } : new[] { AsmDir, RefsDir };
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, sn + ".dll");
            if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
        }
        return null;
    }

    // Returns the proof text; a failure exits the process with code 4.
    static string VerifyLoaded(string phase)
    {
        var sb = new StringBuilder();
        bool bad = false;
        foreach (var name in Family)
        {
            var hits = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name == name).ToList();
            if (hits.Count == 0) { sb.AppendLine($"{name}: NOT LOADED ({phase})"); continue; }
            foreach (var h in hits)
            {
                bool ok = h.Location.StartsWith(RefsDir, StringComparison.OrdinalIgnoreCase);
                sb.AppendLine($"{name} {h.GetName().Version} loaded from {h.Location} {(ok ? "[refs OK]" : "[NOT FROM refs - FATAL]")}");
                if (!ok) bad = true;
            }
            if (hits.Count > 1) { sb.AppendLine($"{name}: loaded {hits.Count} times - FATAL"); bad = true; }
        }
        if (bad)
        {
            Console.Error.WriteLine("FATAL: ILGPU family not loaded from refs snapshot (" + phase + ")\n" + sb);
            Console.Out.Flush();
            Environment.Exit(4);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ switches
    // Resolves "<Type.FullName>.<Member>" to a static bool property or field of the refs copy of the library.
    // Done by reflection so the tool needs no compile-time knowledge of the switch; null return = not found.
    static (Type type, PropertyInfo? prop, FieldInfo? field)? FindSwitch(string spec)
    {
        int dot = spec.LastIndexOf('.');
        if (dot <= 0 || dot == spec.Length - 1) return null;
        string typeName = spec.Substring(0, dot), member = spec.Substring(dot + 1);
        const BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var fam in Family)
        {
            var asm = OnResolve(AssemblyLoadContext.Default, new AssemblyName(fam));
            var t = asm?.GetType(typeName, false);
            if (t == null) continue;
            var pi = t.GetProperty(member, bf);
            if (pi != null && pi.PropertyType == typeof(bool) && pi.CanRead && pi.CanWrite) return (t, pi, null);
            var fi = t.GetField(member, bf);
            if (fi != null && fi.FieldType == typeof(bool) && !fi.IsInitOnly && !fi.IsLiteral) return (t, null, fi);
        }
        return null;
    }

    static bool CheckSwitches(List<string> specs)
    {
        bool ok = true;
        foreach (var s in specs)
            if (FindSwitch(s) == null) { Console.Error.WriteLine($"FATAL: --switch-off {s}: no writable static bool property or field with that name in {string.Join(", ", Family)}"); ok = false; }
        return ok;
    }

    static void ApplySwitches(List<string> specs)
    {
        foreach (var s in specs)
        {
            var f = FindSwitch(s)!.Value;
            object? was = f.prop != null ? f.prop.GetValue(null) : f.field!.GetValue(null);
            if (f.prop != null) f.prop.SetValue(null, false); else f.field!.SetValue(null, false);
            object? now = f.prop != null ? f.prop.GetValue(null) : f.field!.GetValue(null);
            Console.WriteLine($"[child] switch-off {s}: was {was}, now {now}");
            if (!(now is bool b) || b) { Console.Error.WriteLine("FATAL: switch did not take effect: " + s); Environment.Exit(5); }
        }
    }

    // ------------------------------------------------------------------ enumeration
    static List<Cand> Enumerate(Assembly asm, string typeRx, string methRx)
    {
        var tr = new Regex(typeRx);
        var mr = new Regex(methRx);
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            Console.WriteLine($"ENUM: ReflectionTypeLoadException, {e.LoaderExceptions.Length} loader exceptions, first: {e.LoaderExceptions.FirstOrDefault()?.Message}");
            types = e.Types.Where(t => t != null).ToArray()!;
        }
        var list = new List<Cand>();
        foreach (var t in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            string tfn = t.FullName ?? t.Name;
            if (!tr.IsMatch(tfn)) continue;
            MethodInfo[] ms;
            try { ms = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch (Exception e) { Console.WriteLine($"ENUM: {tfn} GetMethods failed: {e.GetType().Name}: {e.Message}"); continue; }
            foreach (var m in ms.OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.MetadataToken))
            {
                if (!mr.IsMatch(m.Name)) continue;
                try
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    var pt = ps[0].ParameterType;
                    if (pt.IsByRef) pt = pt.GetElementType()!;
                    if (pt.FullName == null || !IndexTypes.Contains(pt.FullName)) continue;
                }
                catch (Exception e) { Console.WriteLine($"ENUM: {tfn}.{m.Name} parameters failed: {e.GetType().Name}"); continue; }
                list.Add(new Cand
                {
                    TypeFull = tfn,
                    Method = m.Name,
                    M = m,
                    Generic = t.ContainsGenericParameters || m.IsGenericMethodDefinition || m.ContainsGenericParameters,
                    Id = tfn + "::" + m.Name,
                });
            }
        }
        // disambiguate ids (overloads) and file names
        foreach (var g in list.GroupBy(c => c.Id).Where(g => g.Count() > 1))
            foreach (var c in g) c.Id += "#" + c.M.MetadataToken.ToString("x");
        foreach (var c in list)
        {
            string shortType = c.TypeFull.Substring(c.TypeFull.LastIndexOf('.') + 1).Replace('+', '_');
            c.File = Sanitize(shortType + "." + c.Method);
        }
        foreach (var g in list.GroupBy(c => c.File, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var c in g) c.File += "_" + c.M.MetadataToken.ToString("x");
        foreach (var c in list)
            if (c.File.Length > 150) c.File = c.File.Substring(0, 140) + "_" + c.M.MetadataToken.ToString("x");
        return list;
    }

    static string Sanitize(string s) => Regex.Replace(s, @"[^A-Za-z0-9_.\-]", "_");
    static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    static string FirstLine(string? s) { s ??= ""; int i = s.IndexOfAny(new[] { '\r', '\n' }); return i < 0 ? s : s.Substring(0, i); }

    // ------------------------------------------------------------------ results.tsv
    static HashSet<string> ReadDone(string tsv)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(tsv)) return set;
        foreach (var line in File.ReadAllLines(tsv))
        {
            if (line.StartsWith("type\t")) continue;
            var parts = line.Split('\t');
            if (parts.Length >= 10) set.Add(parts[9]);
        }
        return set;
    }

    static void Append(string tsv, Cand c, string status, string lines, string bytes, string ms, string exType, string msg)
    {
        bool fresh = !File.Exists(tsv);
        using var fs = new FileStream(tsv, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var w = new StreamWriter(fs, new UTF8Encoding(false));
        if (fresh) w.WriteLine(Header);
        w.WriteLine(string.Join('\t', Clean(c.TypeFull), Clean(c.Method), status, lines, bytes, ms, Clean(exType), Clean(msg), c.File, c.Id));
        w.Flush();
        fs.Flush(true);
    }

    // ------------------------------------------------------------------ child
    // NoInlining keeps the JIT from resolving the library types before Main has installed the resolver and RefsDir.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static string? Gen(MethodInfo m)
    {
        var g = SpawnDev.ILGPU.ShaderCompiler.Generate(m, SpawnDev.ILGPU.CapabilityProfiles.WebGPUBaseline, new ILGPU.Runtime.KernelSpecialization(64, null));
        return g.Source;
    }

    static int RunChild(List<Cand> all, string outDir, List<string> switchesOff, string onlyList)
    {
        string tsv = Path.Combine(outDir, "results.tsv");
        string cur = Path.Combine(outDir, "_current.txt");
        string excDir = Path.Combine(outDir, "exceptions");
        Directory.CreateDirectory(excDir);
        var byId = all.ToDictionary(c => c.Id);
        bool proofWritten = false;
        Console.WriteLine($"[child] pid={Environment.ProcessId} switchesOff={string.Join(",", switchesOff)}");
        ApplySwitches(switchesOff);
        foreach (var id in File.ReadAllLines(onlyList).Where(l => l.Length > 0))
        {
            var c = byId[id];
            File.WriteAllText(cur, id);
            var sw = Stopwatch.StartNew();
            string? src = null;
            Exception? err = null;
            try { src = Gen(c.M); }
            catch (Exception e) { err = e; }
            sw.Stop();
            string ms = sw.ElapsedMilliseconds.ToString();
            if (!proofWritten)
            {
                var proof = VerifyLoaded("after first Generate");
                File.WriteAllText(Path.Combine(outDir, "loaded_assemblies.txt"), proof);
                Console.Write(proof);
                proofWritten = true;
            }
            if (err == null && src != null)
            {
                File.WriteAllText(Path.Combine(outDir, c.File + ".wgsl"), src, new UTF8Encoding(false));
                int lines = src.Count(ch => ch == '\n') + (src.Length > 0 && src[^1] != '\n' ? 1 : 0);
                int bytes = Encoding.UTF8.GetByteCount(src);
                Append(tsv, c, "OK", lines.ToString(), bytes.ToString(), ms, "", "");
                Console.WriteLine($"[child] OK {c.Id} lines={lines} bytes={bytes} ms={ms}");
            }
            else
            {
                Exception e = err ?? new InvalidOperationException("Generate returned null Source");
                while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
                File.WriteAllText(Path.Combine(excDir, c.File + ".txt"), c.Id + "\n" + e);
                Append(tsv, c, "EXCEPTION", "-", "-", ms, e.GetType().FullName ?? e.GetType().Name, FirstLine(e.Message));
                Console.WriteLine($"[child] EXCEPTION {c.Id} {e.GetType().Name}: {FirstLine(e.Message)}");
            }
            File.WriteAllText(cur, "");
            Console.Out.Flush();
        }
        VerifyLoaded("end of child");
        return 0;
    }

    // ------------------------------------------------------------------ parent / driver
    static int RunParent(List<Cand> all, string asmPath, string typeRx, string methRx, string outDir, List<string> switchesOff,
                         string? only, string? skipFile, int batch, int timeoutS)
    {
        string tsv = Path.Combine(outDir, "results.tsv");
        string cur = Path.Combine(outDir, "_current.txt");
        string log = Path.Combine(outDir, "child.log");
        Console.WriteLine($"[screen] assembly={asmPath} outDir={outDir} refs={RefsDir} switchesOff={string.Join(",", switchesOff)} candidates={all.Count}");
        VerifyLoaded("parent after enumerate");
        // Fail in the parent, once, instead of in every child, when a switch name is mistyped.
        if (!CheckSwitches(switchesOff)) return 2;

        if (only != null)
        {
            all = all.Where(c => c.Id == only || c.Method == only).ToList();
            if (all.Count == 0) { Console.Error.WriteLine("--only matched nothing"); return 2; }
        }
        var skip = new HashSet<string>();
        if (skipFile != null) foreach (var l in File.ReadAllLines(skipFile)) if (l.Length > 0) skip.Add(l.Trim());
        all = all.Where(c => !skip.Contains(c.Id) && !skip.Contains(c.Method)).ToList();

        var done = ReadDone(tsv);
        foreach (var c in all.Where(c => c.Generic && !done.Contains(c.Id)))
            Append(tsv, c, "SKIPPED-GENERIC", "-", "-", "-", "", "");
        done = ReadDone(tsv);
        var byId = all.ToDictionary(c => c.Id);
        var pending = all.Where(c => !c.Generic && !done.Contains(c.Id)).Select(c => c.Id).ToList();
        Console.WriteLine($"[screen] generic-skipped={all.Count(c => c.Generic)} pending={pending.Count} already-done={done.Count}");

        var noProgress = new Dictionary<string, int>();
        int total = pending.Count;
        while (pending.Count > 0)
        {
            var ids = pending.Take(batch).ToList();
            string batchFile = Path.Combine(outDir, "_batch.txt");
            File.WriteAllLines(batchFile, ids);
            File.WriteAllText(cur, "");
            var (exit, timedOut, culprit, stderrTail) = LaunchChild(asmPath, typeRx, methRx, outDir, switchesOff, batchFile, log, timeoutS, cur);
            if (exit == 4) { Console.Error.WriteLine("FATAL: child reported ILGPU not loaded from refs. Aborting."); return 4; }
            if (exit == 5) { Console.Error.WriteLine("FATAL: child could not apply a --switch-off. Aborting."); return 5; }
            done = ReadDone(tsv);
            int newly = ids.Count(i => done.Contains(i));
            if (culprit.Length > 0 && byId.TryGetValue(culprit, out var cc) && !done.Contains(culprit))
            {
                if (timedOut)
                    Append(tsv, cc, "TIMEOUT", "-", "-", (timeoutS * 1000).ToString(), "Timeout", $"exceeded {timeoutS}s, child killed");
                else
                    Append(tsv, cc, "CRASHED", "-", "-", "-", "ExitCode=0x" + exit.ToString("X8"), FirstLine(stderrTail));
                Console.WriteLine($"[screen] {(timedOut ? "TIMEOUT" : "CRASHED")} {culprit} (exit 0x{exit:X8})");
            }
            else if (newly == 0)
            {
                string first = ids[0];
                noProgress[first] = noProgress.GetValueOrDefault(first) + 1;
                if (noProgress[first] >= 2)
                {
                    Append(tsv, byId[first], "CRASHED", "-", "-", "-", "ExitCode=0x" + exit.ToString("X8"), "child made no progress: " + FirstLine(stderrTail));
                    Console.WriteLine($"[screen] CRASHED(no progress) {first}");
                }
            }
            done = ReadDone(tsv);
            pending = pending.Where(i => !done.Contains(i)).ToList();
            Console.WriteLine($"[screen] progress {total - pending.Count}/{total}");
        }
        File.Delete(cur);
        Summarize(tsv);
        return 0;
    }

    static (int exit, bool timedOut, string culprit, string stderrTail) LaunchChild(string asmPath, string typeRx, string methRx,
        string outDir, List<string> switchesOff, string batchFile, string log, int timeoutS, string cur)
    {
        string host = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var a in new[] { asmPath, typeRx, methRx, outDir, "--child", "--only-list", batchFile, "--refs", RefsDir }) psi.ArgumentList.Add(a);
        foreach (var s in switchesOff) { psi.ArgumentList.Add("--switch-off"); psi.ArgumentList.Add(s); }

        var errTail = new Queue<string>();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (LogLock) File.AppendAllText(log, e.Data + "\n"); };
        p.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            lock (LogLock)
            {
                File.AppendAllText(log, "[stderr] " + e.Data + "\n");
                if (e.Data.Trim().Length > 0) { errTail.Enqueue(e.Data.Trim()); while (errTail.Count > 6) errTail.Dequeue(); }
            }
        };
        p.Start();
        int pid = p.Id; // our own child; the only PID this tool ever kills
        Console.WriteLine($"[screen] child pid {pid} started");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        string lastId = ""; var t0 = DateTime.UtcNow; bool timedOut = false;
        string culprit = "";
        while (!p.WaitForExit(250))
        {
            string now = ReadCur(cur);
            if (now.Length == 0) { lastId = ""; continue; }
            if (now != lastId) { lastId = now; t0 = DateTime.UtcNow; }
            else if ((DateTime.UtcNow - t0).TotalSeconds > timeoutS)
            {
                timedOut = true; culprit = now;
                try { p.Kill(false); } catch { }
                break;
            }
        }
        p.WaitForExit();
        if (!timedOut) culprit = ReadCur(cur);
        string tail;
        lock (LogLock) tail = string.Join(" | ", errTail);
        Console.WriteLine($"[screen] child pid {pid} exit {p.ExitCode}");
        return (p.ExitCode, timedOut, culprit, tail);
    }

    static string ReadCur(string cur)
    {
        try
        {
            using var fs = new FileStream(cur, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadToEnd().Trim();
        }
        catch { return ""; }
    }

    static void Summarize(string tsv)
    {
        var counts = new SortedDictionary<string, int>();
        foreach (var l in File.ReadAllLines(tsv).Skip(1)) { var s = l.Split('\t')[2]; counts[s] = counts.GetValueOrDefault(s) + 1; }
        Console.WriteLine("[screen] summary: " + string.Join(", ", counts.Select(kv => kv.Key + "=" + kv.Value)));
    }

    // ------------------------------------------------------------------ naga
    // naga is run once per file (not once for the folder) so one invalid shader cannot hide the verdict of the others.
    static int Naga(string[] args)
    {
        string folder = Path.GetFullPath(args[1]);
        int timeoutS = 60;
        string? naga = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--timeout") timeoutS = int.Parse(args[++i]);
            else if (args[i] == "--naga") naga = args[++i];
            else { Console.Error.WriteLine("unknown arg " + args[i]); return 2; }
        }
        if (naga == null || !File.Exists(naga)) { Console.Error.WriteLine("--naga <path to naga.exe> is required and must exist"); return 2; }
        string tsv = Path.Combine(folder, "naga.tsv");
        string outDir = Path.Combine(folder, "naga_out");
        Directory.CreateDirectory(outDir);
        var rows = new List<string> { "file\tstatus\tfirst_error_line\tdetail" };
        int valid = 0, invalid = 0;
        foreach (var f in Directory.GetFiles(folder, "*.wgsl").OrderBy(f => f, StringComparer.Ordinal))
        {
            var psi = new ProcessStartInfo(naga) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.ArgumentList.Add(f);
            psi.Environment["NO_COLOR"] = "1";
            using var p = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            bool timedOut = false;
            if (!p.WaitForExit(timeoutS * 1000)) { timedOut = true; try { p.Kill(false); } catch { } }
            p.WaitForExit();
            string text = Regex.Replace(sb.ToString(), "\x1b\\[[0-9;]*m", "");
            string name = Path.GetFileName(f);
            if (!timedOut && p.ExitCode == 0 && text.Contains("Validation successful"))
            {
                valid++;
                rows.Add($"{name}\tvalid\t\t");
            }
            else
            {
                invalid++;
                var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
                string first = timedOut ? "naga timeout" : lines.FirstOrDefault(l => l.TrimStart().StartsWith("error", StringComparison.OrdinalIgnoreCase)) ?? lines.FirstOrDefault() ?? $"exit {p.ExitCode}, no output";
                string detail = string.Join(" | ", lines.Select(l => l.Trim())).Replace('\t', ' ');
                if (detail.Length > 600) detail = detail.Substring(0, 600);
                File.WriteAllText(Path.Combine(outDir, name + ".txt"), text);
                rows.Add($"{name}\tinvalid\t{Clean(first.Trim())}\t{detail}");
            }
            Console.WriteLine($"[naga] {name} {(rows[^1].Split('\t')[1])}");
        }
        File.WriteAllLines(tsv, rows, new UTF8Encoding(false));
        Console.WriteLine($"[naga] {folder}: valid={valid} invalid={invalid}");
        return 0;
    }
}
