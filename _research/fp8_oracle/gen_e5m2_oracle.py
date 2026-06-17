#!/usr/bin/env python3
# FP8 E5M2 oracle generator using ml_dtypes (reference float8_e5m2). E5M2 is IEEE-like:
# 1/5/2, bias 15, HAS +-Inf and NaN, max normal 57344. Overflow -> Inf (not NaN).
import json, struct, math
import numpy as np
import ml_dtypes

f8 = ml_dtypes.float8_e5m2

def f32_bits(x):
    return struct.unpack('<I', struct.pack('<f', np.float32(x)))[0]
def encode(x):
    return int(np.array([np.float32(x)], dtype=np.float32).astype(f8).view(np.uint8)[0])
def decode(b):
    return f32_bits(np.array([b], dtype=np.uint8).view(f8).astype(np.float32)[0])

decode_rows = [{"byte": b, "f32bits": decode(b)} for b in range(256)]

inputs = set()
for b in range(256):
    f = struct.unpack('<f', struct.pack('<I', decode(b)))[0]
    if math.isfinite(f):
        inputs.add(f)
reps = sorted({struct.unpack('<f', struct.pack('<I', decode(b)))[0]
               for b in range(128)
               if math.isfinite(struct.unpack('<f', struct.pack('<I', decode(b)))[0])})
for i in range(len(reps) - 1):
    mid = (reps[i] + reps[i+1]) / 2.0
    for s in (mid, -mid, np.nextafter(mid, math.inf), np.nextafter(mid, -math.inf)):
        inputs.add(float(np.float32(s)))
for v in [57344.0, 57345.0, 61439.0, 61440.0, 61441.0, 65504.0, 65536.0, 1e5, 1e30, 3.4e38]:
    inputs.add(v); inputs.add(-v)
for v in [0.0, -0.0, 2**-16, 2**-14, 2**-15, 1.5*2**-16, 2**-13, 1e-9, 1e-30]:
    inputs.add(float(np.float32(v))); inputs.add(float(np.float32(-v)))
for i in range(-40, 18):
    base = 2.0**i
    for m in [1.0, 1.25, 1.5, 1.75]:
        inputs.add(float(np.float32(base*m))); inputs.add(float(np.float32(-base*m)))

inputs = sorted(inputs, key=lambda x: (math.copysign(1, x), abs(x)))
encode_rows = [{"f32bits": f32_bits(x), "e5m2": encode(x)} for x in inputs]

specials = {
    "pos_inf": {"f32bits": f32_bits(math.inf), "e5m2": encode(math.inf)},
    "neg_inf": {"f32bits": f32_bits(-math.inf), "e5m2": encode(-math.inf)},
    "nan":     {"f32bits": f32_bits(math.nan), "e5m2": encode(math.nan)},
}
out = {"format": "float8_e5m2", "ml_dtypes_version": ml_dtypes.__version__,
       "decode": decode_rows, "encode": encode_rows, "specials": specials}
with open("oracle_e5m2.json", "w") as fp:
    json.dump(out, fp)

def show(x):
    b = encode(x); mag=b&0x7f
    isinf = mag==0x7c; isnan = mag>0x7c
    return f"0x{b:02X} (sign={b>>7}, mag=0x{mag:02X}, isInf={isinf}, isNaN={isnan})"
print("ml_dtypes", ml_dtypes.__version__, "float8_e5m2 convention:")
print(f"  encode(57344)  -> {show(57344.0)}   [max normal]")
print(f"  encode(57345)  -> {show(57345.0)}")
print(f"  encode(61439)  -> {show(61439.0)}   [just below overflow midpoint 61440]")
print(f"  encode(61440)  -> {show(61440.0)}   [overflow midpoint -> Inf]")
print(f"  encode(65504)  -> {show(65504.0)}")
print(f"  encode(1e30)   -> {show(1e30)}")
print(f"  encode(+Inf)   -> {show(math.inf)}")
print(f"  encode(NaN)    -> {show(math.nan)}")
print(f"  decode(0x7C)=Inf? bits=0x{decode(0x7C):08X}  decode(0x7B)={struct.unpack('<f',struct.pack('<I',decode(0x7B)))[0]} (max normal)")
print(f"  wrote oracle_e5m2.json: {len(decode_rows)} decode, {len(encode_rows)} encode rows")
