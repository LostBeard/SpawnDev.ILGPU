using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline shader dump of ONE emulated-double operation per kernel, Dekker and Ozaki, WGSL and GLSL,
/// so each operation's shader-compile cost (D3D FXC / DXC) can be timed on its own. No device, no
/// browser. Run:
///   dotnet run -c Release --project SpawnDev.ILGPU.DemoConsole -- f64-op-cost [outDir]
/// Writes &lt;outDir&gt;/&lt;op&gt;.&lt;dekker|ozaki&gt;.&lt;wgsl|glsl&gt; (default %TEMP%\f64_op_cost).
/// </summary>
internal static class F64OpCostProbe
{
    static void Add(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = a[i] + b[i];
    static void Mul(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = a[i] * b[i];
    static void Div(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = a[i] / b[i];
    static void Sqrt(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Sqrt(a[i]);
    static void Rem(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = a[i] % b[i];
    static void IEEERem(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = global::ILGPU.Algorithms.XMath.IEEERemainder(a[i], b[i]);
    static void Round(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Round(a[i]);
    static void RoundAway(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Round(a[i], MidpointRounding.AwayFromZero);
    static void Truncate(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Truncate(a[i]);
    static void Floor(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Floor(a[i]);
    static void Clamp(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = Math.Min(Math.Max(a[i], -100.25), 100.5);
    static void Copy(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> o) => o[i] = a[i];

    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "f64_op_cost");
        Directory.CreateDirectory(dir);
        var spec = new KernelSpecialization(64, null);
        var ops = new (string Name, Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>> Fn)[]
        {
            ("copy", Copy), ("add", Add), ("mul", Mul), ("div", Div), ("sqrt", Sqrt), ("rem", Rem),
            ("ieeerem", IEEERem), ("round", Round), ("roundaway", RoundAway), ("truncate", Truncate),
            ("floor", Floor), ("clamp", Clamp),
        };
        foreach (var mode in new[] { F64EmulationMode.Dekker, F64EmulationMode.Ozaki })
        {
            string tag = mode == F64EmulationMode.Ozaki ? "ozaki" : "dekker";
            foreach (var (lang, baseProfile) in new[] { ("wgsl", CapabilityProfiles.WebGPUFull), ("glsl", CapabilityProfiles.WebGL2Baseline) })
            {
                var profile = baseProfile with { Float64Mode = mode };
                foreach (var (name, fn) in ops)
                {
                    var g = ShaderCompiler.Generate(fn.Method, profile, spec);
                    var path = Path.Combine(dir, $"{name}.{tag}.{lang}");
                    File.WriteAllText(path, g.Source);
                    Console.WriteLine($"{Path.GetFileName(path)}: {g.Source!.Length} chars{(g.HasErrors ? " HAS ERRORS" : "")}");
                }
            }
        }
        return Task.FromResult(0);
    }
}
