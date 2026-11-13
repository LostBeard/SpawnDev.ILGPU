"""Summarize the compile probe A/B: per kernel, median over reps of module ms, pipeline ms, GPU delta MB, per version.
usage: python -P ab_summary.py <ab.tsv> [inlined_5429.tsv inlined_5430.tsv]
"""
import sys, csv, statistics, collections

rows = list(csv.DictReader(open(sys.argv[1], encoding="utf-8"), delimiter="\t"))
inl = {}
for i, ver in ((2, "v5429"), (3, "v5430")):
    if len(sys.argv) > i:
        for r in csv.DictReader(open(sys.argv[i], encoding="utf-8"), delimiter="\t"):
            inl[(ver, r["kernel"])] = int(r["inlined_lines"])
bad = [r for r in rows if r["status"] != "OK"]
data = collections.defaultdict(lambda: collections.defaultdict(list))
order = []
for r in rows:
    ver, name = r["file"].split("/", 1)
    name = name[:-5]
    if name not in order:
        order.append(name)
    if r["status"] == "OK":
        d = data[(ver, name)]
        d["module"].append(int(r["module_ms"])); d["pipe"].append(int(r["pipeline_ms"])); d["delta"].append(int(r["gpu_delta_mb"])); d["peak"].append(int(r["gpu_peak_mb"]))

def med(v): return statistics.median(v) if v else float("nan")
def rng(v): return "%d-%d" % (min(v), max(v)) if v else "-"

print("kernel\tinl_5429\tinl_5430\tpipe_ms_5429(med, range)\tpipe_ms_5430(med, range)\tchange\ttint_ms 5429/5430\tgpu_peak_mb 5429/5430\tn")
tot = {"v5429": 0.0, "v5430": 0.0}
for name in order:
    a, b = data[("v5429", name)], data[("v5430", name)]
    pa, pb = med(a["pipe"]), med(b["pipe"])
    tot["v5429"] += pa + med(a["module"]); tot["v5430"] += pb + med(b["module"])
    ch = "%+.1f%%" % ((pb - pa) / pa * 100) if pa == pa and pa > 0 else "?"
    print("%s\t%s\t%s\t%d (%s)\t%d (%s)\t%s\t%d/%d\t%d/%d\t%d+%d" % (
        name, inl.get(("v5429", name), ""), inl.get(("v5430", name), ""), pa, rng(a["pipe"]), pb, rng(b["pipe"]), ch,
        med(a["module"]), med(b["module"]), max(a["peak"] or [0]), max(b["peak"] or [0]), len(a["pipe"]), len(b["pipe"])))
print("TOTAL module+pipeline medians: 5.4.29 %.0f ms, 5.4.30 %.0f ms (%+.1f%%)" % (tot["v5429"], tot["v5430"], (tot["v5430"] - tot["v5429"]) / tot["v5429"] * 100))
print("non-OK rows:", len(bad))
for r in bad:
    print("  ", r["file"], r["rep"], r["status"], r["note"])
