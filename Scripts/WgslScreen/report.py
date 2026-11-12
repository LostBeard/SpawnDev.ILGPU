# Builds the comparison report from the result folders + analyzer outputs under one output root. Pure file reading, no GPU.
# Groups: "suite" always, "consumer" only when run.cmd had a consumer folder. OFF = the pass switch disabled, ON = default.
# usage: python -I report.py <outputRoot> > report.txt
import os, re, sys, glob, hashlib, collections

if len(sys.argv) != 2 or not os.path.isdir(sys.argv[1]):
    sys.exit("usage: python -I report.py <outputRoot>")
HERE = os.path.abspath(sys.argv[1])
# The report holds exception messages from arbitrary kernels; a redirected stdout would otherwise use the console code page.
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
GROUPS = [g for g in ("consumer", "suite") if os.path.isdir(os.path.join(HERE, g + "_on")) and os.path.isdir(os.path.join(HERE, g + "_off"))]
FOLDERS = [g + m for g in GROUPS for m in ("_on", "_off")]
STATUSES = ["OK", "EXCEPTION", "CRASHED", "TIMEOUT", "SKIPPED-GENERIC"]
PATTERNS = ["i64_add(", "i64_sub(", "i64_mul(", "i64_to_i32(", "i64_from_i32(", "i64_shl("]
BUGRX = re.compile(r"NarrowLowWordArithmetic|assert|dominat|InsertPosition|verif", re.I)


def load_results(folder):
    rows = collections.OrderedDict()
    p = os.path.join(HERE, folder, "results.tsv")
    for line in open(p, encoding="utf-8").read().split("\n")[1:]:
        if not line:
            continue
        f = line.split("\t")
        rows[f[9]] = dict(type=f[0], method=f[1], status=f[2], lines=f[3], bytes=f[4], ms=f[5], extype=f[6], msg=f[7], file=f[8], id=f[9])
    return rows


def load_naga(folder):
    res = {}
    p = os.path.join(HERE, folder, "naga.tsv")
    for line in open(p, encoding="utf-8").read().split("\n")[1:]:
        if not line:
            continue
        f = line.split("\t")
        res[f[0]] = dict(status=f[1], first=f[2], detail=f[3] if len(f) > 3 else "")
    return res


def load_analyzer(folder, tag):
    p = os.path.join(HERE, "analyzer_%s_%s.txt" % (folder.split("_")[0], tag))
    out = {}
    for line in open(p, encoding="utf-8"):
        t = line.split()
        if len(t) == 6 and t[1].isdigit() and t[4].isdigit():
            out[t[0]] = dict(lines=int(t[1]), bytes=int(t[2]), fns=int(t[3]), inlined=int(t[4]), ratio=t[5])
    return out


def read(folder, f):
    return open(os.path.join(HERE, folder, f + ".wgsl"), "rb").read()


def section(title):
    print()
    print("=" * 100)
    print(title)
    print("=" * 100)


def compare(group):
    on, off = "%s_on" % group, "%s_off" % group
    ron, roff = load_results(on), load_results(off)
    non, noff = load_naga(on), load_naga(off)

    print("--- status counts ---")
    print("%-18s %8s %8s" % ("status", "OFF", "ON"))
    for s in STATUSES:
        print("%-18s %8d %8d" % (s, sum(1 for r in roff.values() if r["status"] == s), sum(1 for r in ron.values() if r["status"] == s)))
    print("%-18s %8d %8d" % ("total rows", len(roff), len(ron)))
    other = {r["status"] for r in list(ron.values()) + list(roff.values())} - set(STATUSES)
    if other:
        print("OTHER STATUSES:", other)

    print()
    print("--- kernels whose status differs ON vs OFF ---")
    n = 0
    for k in sorted(set(ron) | set(roff)):
        a, b = roff.get(k), ron.get(k)
        sa = a["status"] if a else "(absent)"
        sb = b["status"] if b else "(absent)"
        if sa != sb:
            n += 1
            print("%s: OFF=%s ON=%s" % (k, sa, sb))
            for tag, r in (("OFF", a), ("ON", b)):
                if r and r["status"] != "OK":
                    print("    %s: %s: %s" % (tag, r["extype"], r["msg"]))
    print("count:", n)

    print()
    print("--- exceptions (same exception in both = listed once) ---")
    for k in sorted(set(ron) | set(roff)):
        a, b = roff.get(k), ron.get(k)
        if (a and a["status"] in ("EXCEPTION", "CRASHED", "TIMEOUT")) or (b and b["status"] in ("EXCEPTION", "CRASHED", "TIMEOUT")):
            if a and b and a["status"] == b["status"] and a["extype"] == b["extype"] and a["msg"] == b["msg"]:
                print("%s: BOTH %s %s: %s" % (k, a["status"], a["extype"], a["msg"]))
            else:
                for tag, r in (("OFF", a), ("ON", b)):
                    if r and r["status"] in ("EXCEPTION", "CRASHED", "TIMEOUT"):
                        print("%s: %s %s %s: %s" % (k, tag, r["status"], r["extype"], r["msg"]))

    print()
    print("--- naga counts ---")
    for tag, nn in (("OFF", noff), ("ON", non)):
        print("%s: valid=%d invalid=%d (files checked=%d)" % (tag, sum(1 for v in nn.values() if v["status"] == "valid"), sum(1 for v in nn.values() if v["status"] == "invalid"), len(nn)))

    print()
    print("--- naga result differs ON vs OFF ---")
    n = 0
    for k in sorted(set(ron) | set(roff)):
        a, b = roff.get(k), ron.get(k)
        fa = (a["file"] + ".wgsl") if a else None
        fb = (b["file"] + ".wgsl") if b else None
        na, nb = noff.get(fa), non.get(fb)
        sa = na["status"] if na else "(no wgsl)"
        sb = nb["status"] if nb else "(no wgsl)"
        if sa != sb:
            n += 1
            print("%s: OFF=%s ON=%s" % (k, sa, sb))
            if na and na["status"] == "invalid":
                print("    OFF error: %s | %s" % (na["first"], na["detail"]))
            if nb and nb["status"] == "invalid":
                print("    ON  error: %s | %s" % (nb["first"], nb["detail"]))
    print("count:", n)

    print()
    print("--- naga invalid in BOTH ON and OFF (name, first error line) ---")
    n = 0
    for k in sorted(set(ron) & set(roff)):
        a, b = roff[k], ron[k]
        na, nb = noff.get(a["file"] + ".wgsl"), non.get(b["file"] + ".wgsl")
        if na and nb and na["status"] == "invalid" and nb["status"] == "invalid":
            n += 1
            same = "same error" if na["first"] == nb["first"] else "OFF: %s || ON: %s" % (na["first"], nb["first"])
            print("%s: %s" % (k, na["first"] if na["first"] == nb["first"] else same))
    print("count:", n)

    # ---- sizes
    aon, aoff = load_analyzer(on, "on"), load_analyzer(off, "off")
    # analyzer reports identical code once (first label); fill the rest by content hash
    def fill(folder, ana, rows):
        byhash = {}
        for r in rows.values():
            if r["status"] == "OK":
                h = hashlib.sha256(read(folder, r["file"])).hexdigest()
                if r["file"] in ana:
                    byhash.setdefault(h, ana[r["file"]])
        out = {}
        for k, r in rows.items():
            if r["status"] != "OK":
                continue
            h = hashlib.sha256(read(folder, r["file"])).hexdigest()
            out[k] = ana.get(r["file"]) or byhash.get(h)
        return out
    sz_on, sz_off = fill(on, aon, ron), fill(off, aoff, roff)

    both = [k for k in ron if k in roff and ron[k]["status"] == "OK" and roff[k]["status"] == "OK"]
    ident, diff = [], []
    for k in both:
        (ident if read(on, ron[k]["file"]) == read(off, roff[k]["file"]) else diff).append(k)

    print()
    print("--- identity ---")
    print("kernels OK in both: %d ; byte-identical ON vs OFF: %d ; differing: %d" % (len(both), len(ident), len(diff)))

    print()
    print("--- totals of inlined_lines (sum over kernels OK in both) ---")
    so = sum(sz_off[k]["inlined"] for k in both if sz_off.get(k))
    sn = sum(sz_on[k]["inlined"] for k in both if sz_on.get(k))
    print("inlined_lines OFF=%d ON=%d" % (so, sn))
    print("wgsl_lines   OFF=%d ON=%d" % (sum(int(roff[k]["lines"]) for k in both), sum(int(ron[k]["lines"]) for k in both)))
    print("wgsl_bytes   OFF=%d ON=%d" % (sum(int(roff[k]["bytes"]) for k in both), sum(int(ron[k]["bytes"]) for k in both)))

    print()
    print("--- size table: kernels whose WGSL differs (sorted by inlined_lines OFF desc) ---")
    hdr = "%-70s %17s %17s" % ("kernel", "wgsl_lines OFF->ON", "inlined OFF->ON") + "".join(" %s" % p.rstrip("(")[:12].rjust(15) for p in PATTERNS)
    print("columns after inlined: occurrences OFF->ON of: " + "  ".join(PATTERNS))
    print(hdr)
    def key(k):
        return -(sz_off[k]["inlined"] if sz_off.get(k) else -1)
    for k in sorted(diff, key=key):
        a, b = roff[k], ron[k]
        so_, sn_ = sz_off.get(k), sz_on.get(k)
        fo = read(off, a["file"]).decode("utf-8", "replace")
        fn = read(on, b["file"]).decode("utf-8", "replace")
        cols = ["%d->%d" % (fo.count(p), fn.count(p)) for p in PATTERNS]
        print("%-70s %17s %17s" % (k.split("::")[0].rsplit(".", 1)[-1] + "." + k.split("::")[1] if True else k,
              "%s->%s" % (a["lines"], b["lines"]),
              "%s->%s" % (so_["inlined"] if so_ else "?", sn_["inlined"] if sn_ else "?")) + "".join(" %15s" % c for c in cols))

    print()
    print("--- differing kernels: full ids ---")
    for k in sorted(diff, key=key):
        print(k, "->", roff[k]["file"])

    if group == "suite":
        print()
        print("suite kernels differing ON vs OFF: %d (of %d OK in both)" % (len(diff), len(both)))


def bug_scan(n):
    section("%d. Generate exceptions matching NarrowLowWordArithmetic / assert / dominat / InsertPosition / verif" % n)
    found = 0
    for folder in FOLDERS:
        rows = load_results(folder)
        for k, r in rows.items():
            if r["status"] in ("EXCEPTION", "CRASHED", "TIMEOUT"):
                full = ""
                ef = os.path.join(HERE, folder, "exceptions", r["file"] + ".txt")
                if os.path.exists(ef):
                    full = open(ef, encoding="utf-8", errors="replace").read()
                if BUGRX.search(r["msg"] + r["extype"] + full):
                    found += 1
                    print("[%s] %s" % (folder, k))
                    print(full if full else "%s: %s" % (r["extype"], r["msg"]))
                    print("-" * 60)
    print("matches:", found)


if __name__ == "__main__":
    print("Output root: %s" % HERE)
    print("OFF = run with --no-narrow (WebGPUBackend.NarrowInt64LowWordArithmetic = false); ON = default (true).")
    if "consumer" not in GROUPS:
        print("No consumer run present (folders consumer_on / consumer_off missing).")
    n = 0
    for g in GROUPS:
        n += 1
        if g == "consumer":
            section("%d. Consumer assembly (ON = default narrowing, OFF = --no-narrow)" % n)
        else:
            section("%d. ILGPU suite (BackendTestBase)" % n)
        compare(g)
    n += 1
    bug_scan(n)
    section("Loaded-assembly proof")
    for d in FOLDERS:
        print("[%s]" % d)
        print(open(os.path.join(HERE, d, "loaded_assemblies.txt"), encoding="utf-8").read())
