using Microsoft.Playwright;
using SpawnDev.UnitTesting;
using System.Diagnostics;

namespace PlaywrightMultiTest
{
    [Parallelizable(ParallelScope.Self)]
    [TestFixture]
    public class Tests : PageTest
    {
        public static IEnumerable<TestCaseData> TestCases => ProjectRunner.Instance.TestCases;

        [OneTimeSetUp]
        public async Task StartApp()
        {
            await ProjectRunner.Instance.StartUp();
        }

        [Test, TestCaseSource(nameof(TestCases))]
        public async Task RunTest(ProjectTest test)
        {
            // Parallel path: the lane scheduler already ran this test in StartUp and
            // cached its outcome. Report it (keeps the NUnit trx + results JSON correct)
            // without re-running. Tests not in the cache (e.g. PMT-level integration
            // tests, or PMT_PARALLEL=off) fall through to the live path below.
            if (ProjectRunner.Instance.TryGetOutcome(test.Name, out var outcome))
            {
                TestResultsWriter.RecordResult(test.Name, outcome.Status, outcome.Message, outcome.DurationMs);
                if (outcome.Status == "Skip") Assert.Ignore(outcome.Message ?? "Skipped");
                if (outcome.Status == "Fail") Assert.Fail(outcome.Message ?? "Failed");
                return; // Pass
            }

            var sw = Stopwatch.StartNew();
            try
            {
                if (test.Project is TestableBlazorWasm blazorProj)
                {
                    await test.TestFunc(blazorProj.Page);
                    if (test.Result == TestResult.Unsupported)
                    {
                        sw.Stop();
                        TestResultsWriter.RecordResult(test.Name, "Skip", test.ResultMessage, sw.Elapsed.TotalMilliseconds);
                        Assert.Ignore(test.ResultMessage!);
                    }
                }
                else if (test.Project is TestableConsole)
                {
                    await test.TestFunc(null!);
                    if (test.Result == TestResult.Unsupported)
                    {
                        sw.Stop();
                        TestResultsWriter.RecordResult(test.Name, "Skip", test.ResultMessage, sw.Elapsed.TotalMilliseconds);
                        Assert.Ignore(test.ResultMessage!);
                    }
                }
                sw.Stop();
                TestResultsWriter.RecordResult(test.Name, "Pass", null, sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                TestResultsWriter.RecordResult(test.Name, "Fail", ex.Message, sw.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        [OneTimeTearDown]
        public async Task StopApp()
        {
            TestResultsWriter.WriteFinalSummary();
            await ProjectRunner.Instance.Shutdown();
        }
    }
}
