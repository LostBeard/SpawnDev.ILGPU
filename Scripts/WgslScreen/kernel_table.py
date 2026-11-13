"""Per-kernel table for a WgslScreen run folder: WGSL lines/bytes, fully inlined lines, emulated 64-bit calls after
inlining, function-scope array bytes, loops, naga verdict, the three largest helpers (own lines x copies).
usage: python -P kernel_table.py <wgsl_inline.py> <run folder> <outfile.tsv>
See README.md ("Per-kernel table").
"""
import sys, os, glob, re, importlib.util, csv

sys.dont_write_bytecode = True
analyzer, folder, outfile = sys.argv[1], sys.argv[2], sys.argv[3]
spec = importlib.util.spec_from_file_location("wgsl_inline", analyzer)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

naga = {}
p = os.path.join(folder, "naga.tsv")
if os.path.exists(p):
    with open(p, encoding="utf-8") as fh:
        for row in csv.reader(fh, delimiter="\t"):
            if len(row) >= 2:
                naga[row[0]] = row[1]

EMU = re.compile(r"^(i64|u64|f64)_")
ARR = re.compile(r"\bvar\s+\w+\s*:\s*array<\s*(\w+)\s*,\s*(\d+)u?\s*>")
ELEM = {"i32": 4, "u32": 4, "f32": 4, "bool": 4, "vec2<u32>": 8}

rows = []
for f in sorted(glob.glob(os.path.join(folder, "*.wgsl"))):
    label = os.path.basename(f)[:-5]
    raw = open(f, encoding="utf-8").read()
    r = mod.analyze(label, raw)
    emu_fns = [x for x in r.fns if EMU.match(x.name)]
    emu_names = set(x.name for x in emu_fns)
    # call sites of emulation functions from NON-emulation code, weighted by how many copies of the caller exist
    # after full inlining = number of emulated 64-bit operations D3D compiles
    emu_ops = 0
    for x in r.fns:
        if x.name in emu_names or x.copies == 0:
            continue
        for cn, c in x.calls.items():
            if cn in emu_names:
                emu_ops += c * x.copies
    emu_lines = sum(x.prod for x in emu_fns)
    arr_bytes = 0
    biggest_fn_arr = 0
    for x in r.fns:
        b = 0
        for m in ARR.finditer(x.body):
            b += int(m.group(2)) * ELEM.get(m.group(1), 4)
        arr_bytes += b
        biggest_fn_arr = max(biggest_fn_arr, b)
    loops = sum(x.n_loop + x.n_for + x.n_while for x in r.fns)
    top = sorted([x for x in r.fns if not x.entry and x.name not in emu_names and x.copies > 0], key=lambda x: -x.prod)[:3]
    topdesc = "; ".join("%s %dx%d" % (x.name, x.own, x.copies) for x in top)
    rows.append((label, r.lines, r.bytes, r.inlined, emu_ops, emu_lines, arr_bytes, loops, len(r.fns),
                 naga.get(os.path.basename(f), naga.get(label, "?")), topdesc))

rows.sort(key=lambda t: -t[3])
with open(outfile, "w", encoding="utf-8") as fh:
    fh.write("kernel\twgsl_lines\tbytes\tinlined_lines\temu64_ops_inlined\temu64_lines_inlined\tfn_array_bytes\tloops\tfns\tnaga\ttop_helpers(own_lines x copies)\n")
    for t in rows:
        fh.write("\t".join(str(v) for v in t) + "\n")
print("wrote %d rows -> %s" % (len(rows), outfile))
