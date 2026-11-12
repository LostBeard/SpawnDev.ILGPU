#!/usr/bin/env python3
"""Offline WGSL fully-inlined size analyzer (no GPU, no network).

Usage:
  python wgsl_inline.py [options] INPUT [INPUT ...]
INPUT is a .wgsl file or a WebGPU trace .jsonl (records op=shader carry the code,
GPUDevice.createShaderModule maps module handle -> hash, createComputePipeline[Async]
carries the label and the module handle). Identical code is reported once.

Options:
  --detail N|all   print item 2/4/5/6 detail for the N shaders with the largest fully
                   inlined size (default 5); 'all' = every shader; 0 = summary only
  --extract DIR    write each distinct shader to DIR/<label>.wgsl
  --max-funcs N    cap rows of the item 2 table (default 0 = no cap)
"""
import sys, re, json, os, bisect, argparse, collections

FN_OR_BRACE = re.compile(r'[{}]|\bfn\s+(\w+)\s*\(')
COMMENT = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/')
SSA = re.compile(r'\b(?:let|var)(?:\s*<[^>]*>)?\s+(v_\d+)\b')


def normalize(text):
    return text.replace('\r\n', '\n').replace('\r', '\n')


def strip_comments(text):
    return COMMENT.sub(lambda m: re.sub(r'[^\n]', ' ', m.group()), text)


def count_lines(text):
    n = text.count('\n')
    if not text.endswith('\n') and text != '':
        n += 1
    return n


class Obj:
    pass


def analyze(label, raw):
    text = normalize(raw)
    clean = strip_comments(text)
    assert len(clean) == len(text)
    nl = [i for i, ch in enumerate(text) if ch == '\n']
    total_lines = count_lines(text)

    def line_of(pos):  # 0-based line number of char offset
        return bisect.bisect_left(nl, pos)

    fns = []
    depth = 0
    cur = None
    for m in FN_OR_BRACE.finditer(clean):
        tok = m.group()
        if tok == '{':
            depth += 1
            if cur is not None and depth == 1:
                cur.opened = True
        elif tok == '}':
            depth -= 1
            assert depth >= 0, 'unbalanced braces in ' + label
            if depth == 0 and cur is not None and cur.opened:
                cur.end = m.end()
                fns.append(cur)
                cur = None
        else:
            if depth == 0 and cur is None:
                cur = Obj()
                cur.name = m.group(1)
                cur.start = m.start()
                cur.defend = m.end()
                cur.opened = False
    assert depth == 0 and cur is None, 'unbalanced braces / unterminated fn in ' + label

    names = [f.name for f in fns]
    assert len(set(names)) == len(names), 'duplicate fn names in ' + label
    name_re = {n: re.compile(r'\b' + re.escape(n) + r'\s*\(') for n in names}

    covered = set()
    sum_own = 0
    for f in fns:
        f.l0 = line_of(f.start)
        f.l1 = line_of(f.end - 1)
        f.own = f.l1 - f.l0 + 1
        sum_own += f.own
        covered.update(range(f.l0, f.l1 + 1))
        f.body = clean[f.start:f.end]
        # blank out the definition token itself (fn name() so it is not counted as a call
        f.body_nodef = ' ' * (f.defend - f.start) + clean[f.defend:f.end]
        pre = clean[max(0, f.start - 400):f.start]
        k = max(pre.rfind(';'), pre.rfind('}'))
        f.entry = '@compute' in pre[k + 1:]
    module_lines = total_lines - len(covered)
    overlap = sum_own - len(covered)

    byname = {f.name: f for f in fns}
    for f in fns:
        f.calls = collections.Counter()
        for n in names:
            c = len(name_re[n].findall(f.body_nodef))
            if c:
                f.calls[n] = c
        f.direct = 0
    for f in fns:
        for n, c in f.calls.items():
            byname[n].direct += c

    entries = [f for f in fns if f.entry]
    assert len(entries) == 1, '%s: %d @compute entry points' % (label, len(entries))
    entry = entries[0]

    # inlined size (memoized DFS with cycle check)
    state = {}

    def inl(n):
        if state.get(n) == 'busy':
            raise RuntimeError('cycle through ' + n)
        if isinstance(state.get(n), int):
            return state[n]
        state[n] = 'busy'
        f = byname[n]
        s = f.own + sum(c * inl(cn) for cn, c in f.calls.items())
        state[n] = s
        return s

    sys.setrecursionlimit(100000)
    for f in fns:
        f.inlined = inl(f.name)

    # copies: callers before callees (reverse postorder of DFS from entry)
    order, seen = [], set()

    def dfs(n):
        if n in seen:
            return
        seen.add(n)
        for cn in byname[n].calls:
            dfs(cn)
        order.append(n)

    dfs(entry.name)
    for f in fns:
        f.copies = 0
    entry.copies = 1
    for n in reversed(order):
        f = byname[n]
        for cn, c in f.calls.items():
            byname[cn].copies += f.copies * c
    for f in fns:
        f.prod = f.own * f.copies

    # duplicate SSA declarations per function body
    for f in fns:
        cnt = collections.Counter(SSA.findall(f.body))
        f.ssa_total = sum(cnt.values())
        f.dup_names = sum(1 for v in cnt.values() if v > 1)
        f.dup_surplus = sum(v - 1 for v in cnt.values() if v > 1)

    # construct counts
    for f in fns:
        b = f.body
        f.n_loop = len(re.findall(r'\bloop\s*\{', b))
        f.n_for = len(re.findall(r'\bfor\s*\(', b))
        f.n_while = len(re.findall(r'\bwhile\b', b))
        f.n_if = len(re.findall(r'\bif\s*\(', b))
        f.n_switch = len(re.findall(r'\bswitch\b', b))
        f.n_var = len(re.findall(r'\bvar\b', b))

    r = Obj()
    r.label = label
    r.bytes = len(raw.encode('utf-8'))
    r.lines = total_lines
    r.fns = fns
    r.entry = entry
    r.sum_own = sum_own
    r.module_lines = module_lines
    r.overlap = overlap
    r.inlined = entry.inlined
    r.ratio = entry.inlined / total_lines
    return r


def load_inputs(paths):
    """Return list of [label, code, source, hash]; identical code is reported once."""
    out, seen = [], {}
    for p in paths:
        if p.lower().endswith('.jsonl'):
            recs = [json.loads(l) for l in open(p, encoding='utf-8') if l.strip()]
            code_by_hash = {r['hash']: r['code'] for r in recs if r.get('op') == 'shader'}
            mod_hash = {}
            for r in recs:
                if r.get('op') == 'GPUDevice.createShaderModule':
                    cd = r['args'][0]['code']
                    h = cd['$code'] if isinstance(cd, dict) else None
                    if h is not None:
                        mod_hash[r['ret']] = (h, r['args'][0].get('label') or '')
            hash_label = collections.OrderedDict()
            for r in recs:
                if r.get('op', '').startswith('GPUDevice.createComputePipeline'):
                    a = r['args'][0]
                    mh = mod_hash.get(a['compute']['module']['$'])
                    if mh and mh[0] not in hash_label:
                        hash_label[mh[0]] = a.get('label') or mh[1] or ''
            for h, code in code_by_hash.items():
                if code in seen:
                    continue
                lab = hash_label.get(h) or None
                if not lab:
                    m = re.search(r'// Kernel: (\S+)', code)
                    lab = m.group(1) if m else h
                seen[code] = lab
                out.append([lab, code, p, h])
        else:
            code = open(p, encoding='utf-8', newline='').read()
            if code in seen:
                continue
            lab = os.path.splitext(os.path.basename(p))[0]
            seen[code] = lab
            out.append([lab, code, p, ''])
    used = collections.Counter(o[0] for o in out)
    for o in out:
        if used[o[0]] > 1 and o[3]:
            o[0] = o[0] + '_' + o[3]
    return out


def sanitize(s):
    return re.sub(r'[^A-Za-z0-9_.\-]', '_', s)


def print_summary(results):
    print('=== SUMMARY (items 1 and 3) ===')
    print('%-44s %10s %10s %5s %14s %9s' % ('label', 'wgsl_lines', 'bytes', 'fns', 'inlined_lines', 'ratio'))
    for r in results:
        print('%-44s %10d %10d %5d %14d %9.2f' % (r.label, r.lines, r.bytes, len(r.fns), r.inlined, r.ratio))
    print('shaders=%d' % len(results))


def print_detail(r, max_funcs):
    e = r.entry
    print()
    print('=' * 100)
    print('SHADER %s' % r.label)
    print('=' * 100)
    print('[1] label=%s total_wgsl_lines=%d bytes=%d fn_definitions=%d' % (r.label, r.lines, r.bytes, len(r.fns)))
    print('    sanity: sum_own_lines=%d + module_scope_lines=%d = %d ; file_lines=%d ; overlapping_lines=%d ; %s'
          % (r.sum_own, r.module_lines, r.sum_own + r.module_lines, r.lines, r.overlap,
             'OK' if r.sum_own + r.module_lines == r.lines and r.overlap == 0 else 'MISMATCH'))
    print('[3] entry_point=%s entry_own_lines=%d fully_inlined_lines=%d ratio_inlined_over_wgsl_lines=%.2f'
          % (e.name, e.own, r.inlined, r.ratio))
    print('[2] per function (definition order)')
    print('    %-52s %6s %7s %10s %12s %12s' % ('fn', 'own', 'direct', 'copies', 'own*copies', 'inlined'))
    rows = r.fns if not max_funcs else r.fns[:max_funcs]
    for f in rows:
        print('    %-52s %6d %7d %10d %12d %12d' % (f.name + ('  [ENTRY]' if f.entry else ''), f.own, f.direct, f.copies, f.prod, f.inlined))
    if max_funcs and len(r.fns) > max_funcs:
        print('    ... %d more rows omitted by --max-funcs' % (len(r.fns) - max_funcs))
    print('[4] top 15 by own_lines x copies')
    print('    %-52s %6s %7s %10s %12s' % ('fn', 'own', 'direct', 'copies', 'own*copies'))
    for f in sorted(r.fns, key=lambda f: (-f.prod, f.name))[:15]:
        print('    %-52s %6d %7d %10d %12d' % (f.name, f.own, f.direct, f.copies, f.prod))
    print('[5] duplicate SSA declarations (let/var v_<digits> declared more than once in one fn); top 5 by surplus')
    print('    %-52s %9s %9s %9s' % ('fn', 'ssa_decls', 'dup_names', 'surplus'))
    for f in sorted(r.fns, key=lambda f: (-f.dup_surplus, -f.dup_names, f.name))[:5]:
        print('    %-52s %9d %9d %9d' % (f.name, f.ssa_total, f.dup_names, f.dup_surplus))
    print('    all functions: fns_with_dups=%d total_dup_names=%d total_surplus=%d'
          % (sum(1 for f in r.fns if f.dup_names), sum(f.dup_names for f in r.fns), sum(f.dup_surplus for f in r.fns)))
    print('[6] construct counts (entry + 5 largest fns by own lines)')
    print('    %-52s %6s %6s %6s %6s %6s %6s %6s' % ('fn', 'own', 'loop{', 'for(', 'while', 'if(', 'switch', 'var'))
    sel = [e] + [f for f in sorted(r.fns, key=lambda f: (-f.own, f.name)) if f is not e][:5]
    for f in sel:
        print('    %-52s %6d %6d %6d %6d %6d %6d %6d' % (f.name + ('  [ENTRY]' if f.entry else ''), f.own,
              f.n_loop, f.n_for, f.n_while, f.n_if, f.n_switch, f.n_var))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('inputs', nargs='+')
    ap.add_argument('--detail', default='5')
    ap.add_argument('--extract', default=None)
    ap.add_argument('--max-funcs', type=int, default=0)
    a = ap.parse_args()
    items = load_inputs(a.inputs)
    if a.extract:
        os.makedirs(a.extract, exist_ok=True)
        for lab, code, src, h in items:
            with open(os.path.join(a.extract, sanitize(lab) + '.wgsl'), 'w', encoding='utf-8', newline='') as fh:
                fh.write(code)
    results = [analyze(lab, code) for lab, code, src, h in items]
    print_summary(results)
    bysize = sorted(results, key=lambda r: -r.inlined)
    sel = bysize if a.detail == 'all' else bysize[:int(a.detail)]
    for r in sel:
        print_detail(r, a.max_funcs)


if __name__ == '__main__':
    main()
