using Microsoft.Playwright;
using Microsoft.Testing.Platform.Requests;
using PlaywrightMultiTest;
using SpawnDev.UnitTesting;

public class ProjectTest
{
    public TestableProject Project { get; }
    public string Name { get; }
    public string? TestClassName { get; }
    public string? TestTypeName { get; }
    public string? TestMethodName { get; }
    public string? TestPageUrl { get; }
    public TestResult Result { get; set; }
    public string? ResultMessage { get; set; }
    public Func<IPage, Task> TestFunc { get; set; }
    public ProjectTest(TestableProject testableProject, string name)
    {
        Project = testableProject;
        Name = name;
        SetSuccess();
    }
    public ProjectTest(TestableProject testableProject, string typeName, string methodName, string testPage = "")
    {
        Project = testableProject;
        TestTypeName = typeName;
        TestMethodName = methodName;
        Name = $"{TestTypeName}.{TestMethodName}";
        TestClassName = $"{TestTypeName}-{TestMethodName}";
        TestPageUrl = testPage;
        TestFunc = RunTest;
    }
    public void SetSuccess()
    {
        Result = TestResult.Success;
        TestFunc = (page) => Task.CompletedTask;
    }
    public void SetError(string? err = null)
    {
        Result = TestResult.Error;
        TestFunc = async (page) =>
        {
            throw new Exception(string.IsNullOrWhiteSpace(err) ? "Failed" : err);
        };
    }
    public void SetDefault()
    {
        TestFunc = (page) => RunTest(page);
    }

    /// <summary>
    /// Checks if the Blazor error UI (#blazor-error-ui) is visible on the page.
    /// </summary>
    private static async Task<bool> IsBlazorErrorVisible(IPage page)
    {
        return await page.EvaluateAsync<bool>(
            "() => { var el = document.getElementById('blazor-error-ui'); return el != null && getComputedStyle(el).display !== 'none'; }");
    }

    /// <summary>
    /// Dismisses the Blazor error UI by reloading the page if it's visible.
    /// Returns the error text if one was found, null otherwise.
    /// </summary>
    private static async Task<string?> DismissBlazorErrorIfVisible(IPage page)
    {
        var visible = await IsBlazorErrorVisible(page);
        if (!visible) return null;
        var errorText = await page.EvaluateAsync<string>(
            "() => { var el = document.getElementById('blazor-error-ui'); return el ? el.innerText : ''; }");
        return string.IsNullOrWhiteSpace(errorText) ? "Blazor unhandled error (no message)" : errorText.Trim();
    }

    public async Task RunTest(IPage page)
    {
        try
        {
            var rowSelector = $"tr.{TestClassName}";

            // make sure we are on the test page this test is on
            if (page.Url != TestPageUrl)
            {
                await page.GotoAsync(TestPageUrl);

                // wait for test to load
                await page.WaitForSelectorAsync(rowSelector, new() { Timeout = 30000 });
            }

            // Check if Blazor error UI is already visible before this test
            var hadPreExistingBlazorError = await IsBlazorErrorVisible(page);

            // Capture console messages (errors + warnings) during the test
            var consoleErrors = new List<string>();
            var consoleWarnings = new List<string>();

            // ⚠️ WITHOUT THIS, A BROWSER TEST'S OWN Console.WriteLine IS THROWN AWAY. Only errors and
            // warnings were kept, so a benchmark or diagnostic line printed by a WebGPU/WebGL/Wasm test
            // appeared NOWHERE - the test passed and its numbers vanished, which reads as "the test
            // printed nothing" rather than "the harness dropped it". Hit 2026-09-15 by
            // BindGroupCost_WhereDoesCreateBindGroupGo, whose entire purpose is the line it prints.
            // Off by default because a full sweep's log volume is enormous.
            //   PMT_CONSOLE_LOG=<substring>   keep only matching lines
            //   PMT_CONSOLE_LOG=1 (or *)      keep everything, for one scoped run
            // Ported from SpawnDev.ILGPU.ML, which has had it since 2026-09-03.
            var logFilter = Environment.GetEnvironmentVariable("PMT_CONSOLE_LOG");
            var wantAllLogs = logFilter is "1" or "*";
            var captureLogs = !string.IsNullOrEmpty(logFilter);
            var consoleLogs = new List<string>();

            void OnConsole(object? sender, IConsoleMessage msg)
            {
                if (msg.Type == "error")
                    consoleErrors.Add(msg.Text);
                else if (msg.Type == "warning")
                    consoleWarnings.Add(msg.Text);
                else if (captureLogs && msg.Text != null
                         && (wantAllLogs || msg.Text.Contains(logFilter!, StringComparison.OrdinalIgnoreCase)))
                    consoleLogs.Add(msg.Text);
            }
            page.Console += OnConsole;

            // run the test
            var row = page.Locator(rowSelector);

            // find the button within THIS specific row
            var runButton = row.GetByRole(AriaRole.Button, new() { Name = "Run" });

            // wait for test button to be enabled
            await page.WaitForConditionAsync(async () =>
            {
                return await runButton.IsEnabledAsync();
            });

            // click the button to start the process for this row (button will be disabled)
            await runButton.ClickAsync();

            // wait for test to finish (button will become enabled again)
            await page.WaitForConditionAsync(async () =>
            {
                return await runButton.IsEnabledAsync();
            });

            // Stop capturing console
            page.Console -= OnConsole;

            // Only flag a Blazor error if it appeared NEW during this test (wasn't already there)
            string? blazorError = null;
            if (!hadPreExistingBlazorError)
                blazorError = await DismissBlazorErrorIfVisible(page);

            // current state text
            var stateMessage = await row.Locator(".test-state").TextContentAsync();

            //  check for error  class
            var wasError = await row.EvaluateAsync<bool>("el => el.classList.contains('test-error')");

            //  check for error  class
            var unsupported = await row.EvaluateAsync<bool>("el => el.classList.contains('test-unsupported')");

            // Log console errors/warnings/matched logs to stderr for diagnostics
            if (consoleErrors.Count > 0 || consoleWarnings.Count > 0 || consoleLogs.Count > 0)
            {
                Console.Error.WriteLine($"[{Name}] Console: {consoleErrors.Count} error(s), {consoleWarnings.Count} warning(s)"
                                        + (consoleLogs.Count > 0 ? $", {consoleLogs.Count} matched log(s)" : ""));
                foreach (var err in consoleErrors)
                    Console.Error.WriteLine($"  ERROR: {err}");
                foreach (var warn in consoleWarnings)
                    Console.Error.WriteLine($"  WARN: {warn}");
                foreach (var log in consoleLogs)
                    Console.Error.WriteLine($"  LOG: {log}");
            }

            ResultMessage = stateMessage;

            if (unsupported)
            {
                Result = TestResult.Unsupported;
                if (string.IsNullOrWhiteSpace(stateMessage))
                {
                    stateMessage = "Skipped";
                }
            }
            else if (wasError || blazorError != null)
            {
                Result = TestResult.Error;
                if (blazorError != null && !wasError)
                {
                    // Test reported success but Blazor framework threw an unhandled error
                    stateMessage = $"Blazor error during test: {blazorError}";
                }
                else if (string.IsNullOrWhiteSpace(stateMessage))
                {
                    stateMessage = "Failed";
                }
                throw new Exception(stateMessage);
            }
            else
            {
                Result = TestResult.Success;
                if (string.IsNullOrWhiteSpace(stateMessage))
                {
                    stateMessage = "Success";
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Test {Name} failed with error: {ex.Message}");
        }
    }
}
