#!/usr/bin/env python3
# bf16 + float16 oracle vs the authoritative references: ml_dtypes.bfloat16 (= PyTorch/JAX bfloat16)
# and numpy.float16 (IEEE binary16). 16-bit types are fully enumerable: ALL 65536 decode patterns
# are dumped exhaustively; encode is checked by round-trip-identity (comparator-side, from the decode
# table) + a probe set (RNE midpoints, overflow, subnormal, specials, dense sweep).
import json, struct, math
import numpy as np
import ml_dtypes

def f32_bits(x):
    return struct.unpack('<I', struct.pack('<f', np.float32(x)))[0]

def build(dtype, nbits, name):
    # decode: all 2^nbits patterns -> f32 bits
    n = 1 << nbits
    raw = np.arange(n, dtype=np.uint16)
    dec = raw.view(dtype).astype(np.float32)
    decode_rows = [f32_bits(dec[i]) for i in range(n)]   # index = the raw 16-bit pattern

    def encode(x):
        return int(np.array([np.float32(x)], dtype=np.float32).astype(dtype).view(np.uint16)[0])

    # representable finite values (for midpoint probes)
    reps = sorted({float(dec[i]) for i in range(n) if math.isfinite(float(dec[i]))})
    probes = set()
    for i in range(len(reps) - 1):
        mid = (reps[i] + reps[i + 1]) / 2.0
        for s in (mid, np.nextafter(mid, math.inf), np.nextafter(mid, -math.inf)):
            probes.add(float(np.float32(s)))
    # overflow / subnormal / specials / dense sweep
    big = 4e38 if name == "bfloat16" else 70000.0   # bf16 ~ f32 range; f16 max 65504
    for v in [big, big * 2, 1e30, math.inf, math.nan]:
        probes.add(v); probes.add(-v)
    for e in range(-150 if name == "bfloat16" else -30, 40 if name == "bfloat16" else 18):
        base = 2.0 ** e
        for m in [1.0, 1.0009765625, 1.25, 1.5, 1.75, 1.9921875]:
            probes.add(float(np.float32(base * m))); probes.add(float(np.float32(-base * m)))
    probes = [p for p in probes if not (math.isnan(p) and False)]
    enc_rows = []
    for x in sorted(probes, key=lambda z: (0 if math.isnan(z) else 1, math.copysign(1, z) if not math.isnan(z) else 0, abs(z) if not math.isnan(z) else 0)):
        enc_rows.append({"f32bits": f32_bits(x), "raw16": encode(x)})

    specials = {k: {"f32bits": f32_bits(v), "raw16": encode(v)} for k, v in
                {"pos_inf": math.inf, "neg_inf": -math.inf, "nan": math.nan}.items()}
    out = {"format": name, "nbits": nbits, "ml_dtypes_version": ml_dtypes.__version__,
           "decode": decode_rows, "encode": enc_rows, "specials": specials}
    with open(f"oracle_{name}.json", "w") as fp:
        json.dump(out, fp)
    # headline
    def show(x):
        r = encode(x); return f"0x{r:04X}"
    print(f"{name}: decode {n} patterns, encode {len(enc_rows)} probes")
    print(f"  encode(1.0)={show(1.0)} encode(+Inf)={show(math.inf)} encode(NaN)={show(math.nan)} "
          f"encode(big)={show(big)}  decode(NaN-pattern test): see comparator")

build(ml_dtypes.bfloat16, 16, "bfloat16")
build(np.float16, 16, "float16")
