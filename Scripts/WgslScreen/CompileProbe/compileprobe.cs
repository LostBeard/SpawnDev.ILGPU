#:package Microsoft.Playwright@1.63.0
#:property PublishAot=false
// WGSL compile probe driver (no ILGPU, no dispatch): one fresh Chrome per shader.
// dotnet run compileprobe.cs -- <rootDir> <outTsv> <limitGB> <reps> <rel/path.wgsl>...
// rootDir is served on http://localhost:<free port>/ and must hold compile_one.html and the WGSL files.
// Per run: Tint module ms, pipeline (D3D) ms, the GPU process's private bytes before the compile and at its peak.
// The GPU process PID comes from CDP SystemInfo.getProcessInfo of THIS browser; at limitGB the driver closes ITS OWN
// browser (never another process) and records KILLED.
using Microsoft.Playwright;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

string root = Path.GetFullPath(args[0]);
string outTsv = Path.GetFullPath(args[1]);
double limitGb = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
int reps = int.Parse(args[3]);
var files = args.Skip(4).ToArray();
foreach (var f in files) if (!File.Exists(Path.Combine(root, f))) { Console.WriteLine("MISSING " + f); return 2; }

// static file server on a free localhost port (localhost is a secure context, which WebGPU needs)
int port;
{ var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
var http = new HttpListener();
http.Prefixes.Add($"http://localhost:{port}/");
http.Start();
var serverTask = Task.Run(async () =>
{
    while (http.IsListening)
    {
        HttpListenerContext c;
        try { c = await http.GetContextAsync(); } catch { break; }
        try
        {
            string rel = Uri.UnescapeDataString(c.Request.Url!.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) { c.Response.StatusCode = 404; c.Response.Close(); continue; }
            byte[] b = await File.ReadAllBytesAsync(full);
            c.Response.ContentType = full.EndsWith(".html") ? "text/html; charset=utf-8" : "text/plain; charset=utf-8";
            c.Response.ContentLength64 = b.Length;
            await c.Response.OutputStream.WriteAsync(b);
            c.Response.Close();
        }
        catch { try { c.Response.Abort(); } catch { } }
    }
});

bool header = !File.Exists(outTsv);
using var outw = new StreamWriter(outTsv, append: true);
if (header) outw.WriteLine("file\trep\tstatus\tlines\tbytes\tmodule_ms\tpipeline_ms\ttotal_ms\tgpu_base_mb\tgpu_peak_mb\tgpu_delta_mb\tadapter\tnote");
using var pw = await Playwright.CreateAsync();
int rc = 0;
for (int rep = 1; rep <= reps; rep++)
    foreach (var file in files)
    {
        string status = "OK", note = "";
        long baseB = 0, peakB = 0;
        JsonElement? res = null;
        IBrowser? browser = null;
        try
        {
            browser = await pw.Chromium.LaunchAsync(new()
            {
                Headless = false,
                Channel = "chrome",
                Args = new[] { "--enable-unsafe-webgpu", "--ignore-gpu-blocklist", "--disable-gpu-shader-disk-cache", "--window-size=520,360", "--window-position=40,40" },
            });
            var cdp = await browser.NewBrowserCDPSessionAsync();
            var page = await browser.NewPageAsync();
            page.SetDefaultTimeout(120000);
            await page.GotoAsync($"http://localhost:{port}/compile_one.html?file={Uri.EscapeDataString(file.Replace('\\', '/'))}");
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 60 && !await page.EvaluateAsync<bool>("window.__probe && (window.__probe.ready || window.__probe.done)")) await Task.Delay(100);
            // the GPU process of THIS browser
            int gpuPid = 0;
            var info = await cdp.SendAsync("SystemInfo.getProcessInfo");
            if (info is JsonElement je && je.TryGetProperty("processInfo", out var arr))
                foreach (var p in arr.EnumerateArray())
                    if (p.GetProperty("type").GetString() == "GPU") gpuPid = p.GetProperty("id").GetInt32();
            if (gpuPid == 0) { status = "NO-GPU-PID"; }
            Process? gp = null;
            if (gpuPid != 0) { try { gp = Process.GetProcessById(gpuPid); } catch { status = "NO-GPU-PROC"; } }
            long Sample() { if (gp == null) return 0; try { gp.Refresh(); return gp.PrivateMemorySize64; } catch { return 0; } }
            baseB = peakB = Sample();
            await page.EvaluateAsync("window.__go = true");
            sw.Restart();
            bool done = false;
            while (sw.Elapsed.TotalSeconds < 420)
            {
                long s = Sample();
                if (s > peakB) peakB = s;
                if (peakB > limitGb * 1024 * 1024 * 1024) { status = "KILLED"; note = $"GPU process over {limitGb} GB after {sw.Elapsed.TotalSeconds:F1} s"; break; }
                try { done = await page.EvaluateAsync<bool>("window.__probe.done"); } catch (Exception e) { status = "PAGE-ERROR"; note = e.Message.Split('\n')[0]; break; }
                if (done) break;
                await Task.Delay(150);
            }
            if (status == "OK" && !done) { status = "TIMEOUT"; note = "420 s"; }
            if (status != "KILLED" && status != "PAGE-ERROR")
            {
                var json = await page.EvaluateAsync<string>("JSON.stringify(window.__probe.result)");
                res = JsonDocument.Parse(json).RootElement;
            }
        }
        catch (Exception e) { status = "DRIVER-ERROR"; note = e.Message.Split('\n')[0]; }
        finally { if (browser != null) { try { await browser.CloseAsync(); } catch { } } }

        string G(string k) => res is JsonElement r && r.TryGetProperty(k, out var v) ? v.ToString() : "";
        if (status == "OK")
        {
            foreach (var k in new[] { "threw", "lost", "firstError", "pipelineError", "validation" })
                if (G(k) != "") { status = k == "lost" ? "LOST" : "ERROR"; note = k + ": " + G(k).Replace('\t', ' ').Replace('\n', ' '); break; }
        }
        if (status != "OK") rc = 1;
        string line = string.Join("\t", file, rep, status, G("lines"), G("bytes"), G("moduleMs"), G("pipelineMs"), G("totalMs"),
            (baseB / 1048576).ToString(), (peakB / 1048576).ToString(), ((peakB - baseB) / 1048576).ToString(), G("adapter"), note);
        outw.WriteLine(line); outw.Flush();
        Console.WriteLine(line);
    }
http.Stop();
return rc;
