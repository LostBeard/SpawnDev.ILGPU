using SpawnDev.SpawnJS;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.NodeTests;

// SpawnJS test host for the SpawnDev.ILGPU kernel suite, run headless under Node with a real WebGPU
// adapter supplied by Google Dawn through npm `webgpu` (see main.mjs). No Blazor, no browser, no Playwright.
Console.WriteLine("SpawnDev.ILGPU.NodeTests starting");

var JS = SpawnJSRuntime.Instance;
JS.Verbose = false;

var gpuPresent = JS.Get<string?>("navigator?.gpu?.constructor?.name");
Console.WriteLine($"HOST: navigator.gpu={gpuPresent ?? "(absent)"}");

var runner = new UnitTestRunner();
runner.SetTestTypes(new[] { typeof(WebGPUTests) });
// `node main.mjs <text>`: only the tests whose name contains <text>
var filter = args.FirstOrDefault(a => !a.StartsWith("--"));

Console.WriteLine($"READY: {runner.Tests.Count} tests{(filter != null ? $" (filter '{filter}')" : "")}");

// Stream each result the instant it lands. The runner fires TestStatusChanged as tests complete; we
// print any test that has a result we have not printed yet, and flush, so the log shows live progress
// and a hang points at the NEXT test after the last printed line rather than blackholing the whole run.
var printed = new HashSet<UnitTest>();
int pass = 0, fail = 0, skip = 0;

void Drain()
{
    foreach (var t in runner.Tests)
    {
        if (t.Result == TestResult.None || !printed.Add(t)) continue;
        var result = t.Result switch
        {
            TestResult.Success => "Success",
            TestResult.Error => "Failed",
            TestResult.Unsupported => "Skipped",
            _ => t.Result.ToString(),
        };
        if (t.Result == TestResult.Success) pass++;
        else if (t.Result == TestResult.Error) fail++;
        else skip++;
        var detail = t.Result == TestResult.Error ? t.Error : t.ResultText;
        Console.WriteLine($"TEST: {t.TestName}|{result}|{t.Duration:F0}|{detail}");
        Console.Out.Flush();
    }
}

runner.TestStatusChanged += Drain;
// --skip=<name>[,<name>...]: leave out tests whose name contains one of these (e.g. one a binding cannot compile). An
// argument, not an environment variable: a .NET WebAssembly app does not see the Node process environment.
var skipList = (args.FirstOrDefault(a => a.StartsWith("--skip=")) is { } skipArg ? skipArg["--skip=".Length..] : "").Split(',', StringSplitOptions.RemoveEmptyEntries);
if (filter != null || skipList.Length > 0)
{
    foreach (var t in runner.Tests.Where(t => (filter == null || t.TestName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                              && !skipList.Any(s => t.TestName.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList())
        await runner.RunTest(t);
    if (skipList.Length > 0) Console.WriteLine($"SKIPPED BY --skip: {string.Join(", ", skipList)}");
}
else await runner.RunTests();
Drain();   // catch any final transitions the event missed
Console.WriteLine($"RESULTS: Failed: {fail} Passed: {pass} Skipped: {skip} Ran: {pass + fail + skip}");
Console.Out.Flush();
