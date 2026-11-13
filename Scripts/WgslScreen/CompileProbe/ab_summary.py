"""Summarize a compile probe A/B: per kernel, the median and range over reps of the pipeline ms, the median Tint
module ms and the largest GPU-process peak, per version.
usage: python -P ab_summary.py <ab.tsv> [kernel_table_of_version_1.tsv kernel_table_of_version_2.tsv]
The version of a row is the first folder of its relative path (v1/k.wgsl, v2/k.wgsl); the first two versions in
order of appearance are compared (A = first, B = second). The optional tables (kernel_table.py output) add the
fully inlined line counts.
"""
import sys, csv, statistics, collections

rows = list(csv.DictReader(open(sys.argv[1], encoding="utf-8"), delimiter="\t"))
versions = []
for r in rows:
    ver = r["file"].replace("\\", "/").split("/", 1)[0]
    if ver not in versions:
        versions.append(ver)
if len(versions) < 2:
    sys.exit("ab_summary: need two versions (first folder of the file path), found %s" % versions)
if len(versions) > 2:
    print("note: %d versions found, comparing the first two: %s" % (len(versions), versions[:2]))
A, B = versions[0], versions[1]
inl = {}
for i, ver in ((2, A), (3, B)):
    if len(sys.argv) > i:
        for r in csv.DictReader(open(sys.argv[i], encoding="utf-8"), delimiter="\t"):
            inl[(ver, r["kernel"])] = int(r["inlined_lines"])
bad = [r for r in rows if r["status"] != "OK"]
data = collections.defaultdict(lambda: collections.defaultdict(list))
order = []
for r in rows:
    ver, name = r["file"].replace("\\", "/").split("/", 1)
    name = name[:-5] if name.endswith(".wgsl") else name
    if name not in order:
        order.append(name)
    if r["status"] == "OK":
        d = data[(ver, name)]
        d["module"].append(int(r["module_ms"])); d["pipe"].append(int(r["pipeline_ms"])); d["peak"].append(int(r["gpu_peak_mb"]))


def med(v):
    return statistics.median(v) if v else None


def fmt(v):
    return "-" if v is None else "%d" % v


def rng(v):
    return "%d-%d" % (min(v), max(v)) if v else "-"


print("A = %s, B = %s" % (A, B))
print("kernel\tinlined_A\tinlined_B\tpipeline_ms_A (median, range)\tpipeline_ms_B (median, range)\tchange\ttint_ms A/B\tgpu_peak_mb A/B\tn A+B")
tot = {A: 0.0, B: 0.0}
complete = True
for name in order:
    a, b = data[(A, name)], data[(B, name)]
    pa, pb = med(a["pipe"]), med(b["pipe"])
    if pa is None or pb is None:
        complete = False
    else:
        tot[A] += pa + med(a["module"]); tot[B] += pb + med(b["module"])
    ch = "%+.1f%%" % ((pb - pa) / pa * 100) if pa and pb is not None else "?"
    print("%s\t%s\t%s\t%s (%s)\t%s (%s)\t%s\t%s/%s\t%s/%s\t%d+%d" % (
        name, inl.get((A, name), ""), inl.get((B, name), ""), fmt(pa), rng(a["pipe"]), fmt(pb), rng(b["pipe"]), ch,
        fmt(med(a["module"])), fmt(med(b["module"])), fmt(max(a["peak"]) if a["peak"] else None),
        fmt(max(b["peak"]) if b["peak"] else None), len(a["pipe"]), len(b["pipe"])))
if tot[A] > 0:
    print("TOTAL module+pipeline medians%s: A %.0f ms, B %.0f ms (%+.1f%%)" % (
        "" if complete else " (kernels with both versions OK only)", tot[A], tot[B], (tot[B] - tot[A]) / tot[A] * 100))
print("non-OK rows:", len(bad))
for r in bad:
    print("  ", r["file"], r["rep"], r["status"], r["note"])
