// Verifies that the four ILGPU package versions are kept in lockstep.
//
// A bug fixed under ILGPU/ ships in ILGPU.dll which lives in the SEPARATE
// SpawnDev.ILGPU.Fork nupkg, NOT in this SpawnDev.ILGPU wrapper. If the
// wrapper version is bumped without bumping the Fork PackageReference, the
// consumer transitively pulls the OLD Fork from nuget.org and the fix is
// invisible. Exactly what bit rc.28 on 2026-04-28.
//
// This check enforces the four-package bundle contract:
//   (1) ILGPU/ILGPU.csproj                          <Version>          (= V)
//   (2) ILGPU.Algorithms/ILGPU.Algorithms.csproj    <Version>          (= V)
//   (3) SpawnDev.ILGPU/SpawnDev.ILGPU.csproj        SpawnDev.ILGPU.Fork
//                                                   PackageReference   (= V)
//   (4) SpawnDev.ILGPU/SpawnDev.ILGPU.csproj        SpawnDev.ILGPU.Algorithms.Fork
//                                                   PackageReference   (= V)
//
// Run from repo root:  dotnet run _check-fork-version-sync.cs
// Or via the .bat:     _check-fork-version-sync.bat
// CI runs the same script in .github/workflows/fork-version-sync-check.yml

using System.Xml.Linq;

// Run from the repo root (this script's directory). The .bat handles cwd via `cd /d "%~dp0"`;
// CI invokes it from the checkout root. Manual invocation: cd to repo root first.
string repoRoot = Environment.CurrentDirectory;
if (!File.Exists(Path.Combine(repoRoot, "_check-fork-version-sync.cs")))
{
    Console.Error.WriteLine($"Could not locate _check-fork-version-sync.cs in {repoRoot}.");
    Console.Error.WriteLine("Run this from the SpawnDev.ILGPU repo root (where the .csproj/.bat live).");
    return 2;
}

string ilgpuCsproj      = Path.Combine(repoRoot, "ILGPU",            "ILGPU.csproj");
string algorithmsCsproj = Path.Combine(repoRoot, "ILGPU.Algorithms", "ILGPU.Algorithms.csproj");
string spawnDevCsproj   = Path.Combine(repoRoot, "SpawnDev.ILGPU",   "SpawnDev.ILGPU.csproj");

string GetVersionElement(string csproj)
{
    var doc = XDocument.Load(csproj);
    var v = doc.Descendants("Version").FirstOrDefault()?.Value;
    if (string.IsNullOrWhiteSpace(v)) throw new Exception($"No <Version> element in {csproj}");
    return v.Trim();
}

string GetForkPackageRefVersion(string csproj, string packageId)
{
    var doc = XDocument.Load(csproj);
    var match = doc.Descendants("PackageReference")
        .FirstOrDefault(p => (string?)p.Attribute("Include") == packageId);
    if (match == null) throw new Exception($"No <PackageReference Include=\"{packageId}\"> in {csproj}");
    var v = (string?)match.Attribute("Version");
    if (string.IsNullOrWhiteSpace(v)) throw new Exception($"No Version attribute on {packageId} reference in {csproj}");
    return v!.Trim();
}

var ilgpuVer       = GetVersionElement(ilgpuCsproj);
var algorithmsVer  = GetVersionElement(algorithmsCsproj);
var forkRefVer     = GetForkPackageRefVersion(spawnDevCsproj, "SpawnDev.ILGPU.Fork");
var algoForkRefVer = GetForkPackageRefVersion(spawnDevCsproj, "SpawnDev.ILGPU.Algorithms.Fork");

Console.WriteLine("ILGPU.csproj                          <Version>                                = " + ilgpuVer);
Console.WriteLine("ILGPU.Algorithms.csproj               <Version>                                = " + algorithmsVer);
Console.WriteLine("SpawnDev.ILGPU.csproj  PackageReference SpawnDev.ILGPU.Fork            Version = " + forkRefVer);
Console.WriteLine("SpawnDev.ILGPU.csproj  PackageReference SpawnDev.ILGPU.Algorithms.Fork Version = " + algoForkRefVer);
Console.WriteLine();

var distinct = new[] { ilgpuVer, algorithmsVer, forkRefVer, algoForkRefVer }.Distinct().ToArray();
if (distinct.Length == 1)
{
    Console.WriteLine($"OK: all four ILGPU package versions match ({distinct[0]}).");
    return 0;
}

Console.Error.WriteLine("FOUR-PACKAGE BUNDLE MISMATCH.");
Console.Error.WriteLine();
Console.Error.WriteLine("These four versions MUST match. They don't.");
Console.Error.WriteLine();
Console.Error.WriteLine("Root cause: a bugfix under ILGPU/ ships in ILGPU.dll which lives in the SEPARATE");
Console.Error.WriteLine("SpawnDev.ILGPU.Fork nupkg, NOT in the SpawnDev.ILGPU wrapper. Bumping only the");
Console.Error.WriteLine("wrapper leaves consumers transitively pulling the OLD Fork from nuget.org and the");
Console.Error.WriteLine("fix is invisible to them.");
Console.Error.WriteLine();
Console.Error.WriteLine("Fix: pick a single new version V and apply ALL FOUR of these edits, then rebuild:");
Console.Error.WriteLine();
Console.Error.WriteLine($"  (1) ILGPU/ILGPU.csproj                          <Version>{distinct.First()}</Version>  -> <Version>V</Version>");
Console.Error.WriteLine($"  (2) ILGPU.Algorithms/ILGPU.Algorithms.csproj    <Version>{distinct.First()}</Version>  -> <Version>V</Version>");
Console.Error.WriteLine($"  (3) SpawnDev.ILGPU/SpawnDev.ILGPU.csproj        PackageReference SpawnDev.ILGPU.Fork            Version=\"V\"");
Console.Error.WriteLine($"  (4) SpawnDev.ILGPU/SpawnDev.ILGPU.csproj        PackageReference SpawnDev.ILGPU.Algorithms.Fork Version=\"V\"");
Console.Error.WriteLine();
Console.Error.WriteLine("Then publish the rebuilt Fork nupkgs to local feed, then rebuild + publish the");
Console.Error.WriteLine("wrapper. See feedback_ilgpu_fork_four_package_bump.md and the banner comment in");
Console.Error.WriteLine("SpawnDev.ILGPU/SpawnDev.ILGPU.csproj for the full procedure.");
return 1;
