using System.Text.Json;

namespace PlaywrightMultiTest;

/// <summary>
/// Writes test results to _mldump/ as JSON files.
/// - playwright-latest.json: overwritten after every test (live progress)
/// - playwright-YYYY-MM-DD_HH-mm-ss.json: permanent record per run
///
/// This eliminates the need to re-run tests just because we lost console output.
/// Results are always on disk at a known location.
/// </summary>
public static class TestResultsWriter
{
    private static readonly string OutputDir;
    private static readonly string LatestPath;
    private static readonly string TimestampedPath;
    private static readonly List<TestResultEntry> _results = new();
    private static readonly DateTime _runStartTime = DateTime.Now;
    private static readonly object _lock = new();

    static TestResultsWriter()
    {
        // Write to _mldump/ relative to the solution root
        var solutionDir = FindSolutionRoot();
        OutputDir = Path.Combine(solutionDir, "_ilgpudump");
        Directory.CreateDirectory(OutputDir);

        LatestPath = Path.Combine(OutputDir, "playwright-latest.json");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        TimestampedPath = Path.Combine(OutputDir, $"playwright-{timestamp}.json");
    }

    /// <summary>Record a test result. Called after each test completes.</summary>
    public static void RecordResult(string testName, string result, string? error, double durationMs)
    {
        lock (_lock)
        {
            _results.Add(new TestResultEntry
            {
                Name = testName,
                Result = result,
                Error = error?.Length > 4000 ? error[..4000] : error,
                DurationMs = double.IsNaN(durationMs) || double.IsInfinity(durationMs) ? -1 : Math.Round(durationMs, 1),
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            });
            WriteJson();
        }
    }

    /// <summary>Write the final summary (called at teardown).</summary>
    public static void WriteFinalSummary()
    {
        lock (_lock)
        {
            WriteJson();
            // Also write the timestamped copy
            try
            {
                File.Copy(LatestPath, TimestampedPath, overwrite: true);
            }
            catch { /* Non-critical */ }
        }
    }

    private static void WriteJson()
    {
        try
        {
            var passed = _results.Count(r => r.Result == "Pass");
            var failed = _results.Count(r => r.Result == "Fail");
            var skipped = _results.Count(r => r.Result == "Skip");
            var pending = 0; // We don't know total ahead of time

            var summary = new
            {
                runStart = _runStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                lastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                elapsed = (DateTime.Now - _runStartTime).TotalSeconds.ToString("F0") + "s",
                passed,
                failed,
                skipped,
                total = _results.Count,
                tests = _results,
            };

            var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            });
            File.WriteAllText(LatestPath, json);
        }
        catch { /* Non-critical — don't crash the test runner */ }
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        // Fallback: use the base directory
        return AppContext.BaseDirectory;
    }
}

public class TestResultEntry
{
    public string Name { get; init; } = "";
    public string Result { get; init; } = "";
    public string? Error { get; init; }
    public double DurationMs { get; init; }
    public string Timestamp { get; init; } = "";
}
