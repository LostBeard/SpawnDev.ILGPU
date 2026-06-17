#!/usr/bin/env python3
# FP4 E2M1FN oracle vs ml_dtypes.float4_e2m1fn (the NVFP4/MXFP4 element format; 16 finite codes,
# no Inf/NaN). Consumed by the C# CompareFP4 (DemoConsole -- bf16-f16-oracle). 1-byte storage
# (value in the low nibble), exactly like FP8.
import numpy as np, ml_dtypes, struct, math, json
dt = ml_dtypes.float4_e2m1fn
def fb(x): return struct.unpack('<I', struct.pack('<f', np.float32(x)))[0]
def dec(b): return float(np.array([b], dtype=np.uint8).view(dt).astype(np.float32)[0])
def enc(x): return int(np.array([np.float32(x)], dtype=np.float32).astype(dt).view(np.uint8)[0])

decode = [fb(dec(b)) for b in range(16)]
reps = sorted({dec(b) for b in range(16) if math.isfinite(dec(b))})
probes = set()
for i in range(len(reps) - 1):
    mid = (reps[i] + reps[i + 1]) / 2.0
    for s in (mid, np.nextafter(mid, math.inf), np.nextafter(mid, -math.inf)):
        probes.add(float(np.float32(s)))
for v in [0.0, -0.0, 6.0, 6.5, 7.0, 8.0, 100.0, 1e30, math.inf, math.nan, -6.0, -7.0, -100.0, -math.inf, 0.1, 0.24, 0.26, 0.3]:
    probes.add(v)
for i in range(-4, 4):
    for m in [1.0, 1.1, 1.25, 1.5, 1.75, 1.9]:
        probes.add(float(np.float32(2.0 ** i * m))); probes.add(float(np.float32(-2.0 ** i * m)))
enc_rows = [{"f32bits": fb(x), "raw": enc(x)} for x in
            sorted(probes, key=lambda z: (0 if math.isnan(z) else 1,
                                          math.copysign(1, z) if not math.isnan(z) else 0,
                                          abs(z) if not math.isnan(z) else 0))]
json.dump({"format": "float4_e2m1fn", "ml_dtypes_version": ml_dtypes.__version__,
           "decode": decode, "encode": enc_rows}, open("oracle_float4_e2m1.json", "w"))
print(f"wrote oracle_float4_e2m1.json: 16 decode + {len(enc_rows)} encode probes")
print("decode:", [round(dec(b), 3) for b in range(16)])
