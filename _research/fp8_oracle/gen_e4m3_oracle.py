#!/usr/bin/env python3
# FP8 E4M3FN oracle generator using ml_dtypes (the reference impl PyTorch/JAX float8_e4m3fn share).
# Produces oracle_e4m3.json consumed by the C# comparator, and prints the headline edge cases
# that answer the flagged convention question (overflow: saturate-to-448 vs NaN; Inf; NaN).
import json, struct, math
import numpy as np
import ml_dtypes

f8 = ml_dtypes.float8_e4m3fn

def f32_bits(x):
    return struct.unpack('<I', struct.pack('<f', np.float32(x)))[0]

def encode(x):
    # f32 -> e4m3fn (the library's RNE + overflow convention). Returns raw uint8.
    a = np.array([np.float32(x)], dtype=np.float32).astype(f8)
    return int(a.view(np.uint8)[0])

def decode(b):
    # e4m3fn raw byte -> f32. Returns f32 bit pattern (exact; preserves NaN/-0).
    a = np.array([b], dtype=np.uint8).view(f8).astype(np.float32)
    return f32_bits(a[0])

# ---- decode: all 256 byte patterns (exhaustive) ----
decode_rows = [{"byte": b, "f32bits": decode(b)} for b in range(256)]

# ---- encode: comprehensive input set ----
inputs = set()
# every e4m3 value's exact f32 (round-trip identity)
for b in range(256):
    bits = decode(b)
    f = struct.unpack('<f', struct.pack('<I', bits))[0]
    if not math.isnan(f):
        inputs.add(f)
# midpoints between adjacent representable magnitudes (RNE tie probes), both signs
reps = sorted({struct.unpack('<f', struct.pack('<I', decode(b)))[0]
               for b in range(128)
               if not math.isnan(struct.unpack('<f', struct.pack('<I', decode(b)))[0])})
for i in range(len(reps) - 1):
    mid = (reps[i] + reps[i+1]) / 2.0
    for s in (mid, -mid, np.nextafter(mid, math.inf), np.nextafter(mid, -math.inf)):
        inputs.add(float(np.float32(s)))
# overflow / saturation boundary region
for v in [448.0, 449.0, 456.0, 463.9, 464.0, 464.1, 480.0, 512.0, 1000.0, 1e4, 1e30, 3.4e38]:
    inputs.add(v); inputs.add(-v)
# tiny / subnormal / zero region
for v in [0.0, -0.0, 1e-3, 1e-9, 1e-30, 2**-9, 2**-10, 2**-12, 1.5*2**-9, 2**-6, 2**-7]:
    inputs.add(float(np.float32(v))); inputs.add(float(np.float32(-v)))
# dense exponential + linear sweep
for i in range(-40, 12):
    base = 2.0**i
    for m in [1.0, 1.0625, 1.125, 1.25, 1.375, 1.5, 1.75, 1.9375]:
        inputs.add(float(np.float32(base*m))); inputs.add(float(np.float32(-base*m)))

inputs = sorted(inputs, key=lambda x: (math.copysign(1, x), abs(x)))
encode_rows = [{"f32bits": f32_bits(x), "e4m3": encode(x)} for x in inputs]

# specials (kept separate so the C# side can report them explicitly)
specials = {
    "pos_inf": {"f32bits": f32_bits(math.inf), "e4m3": encode(math.inf)},
    "neg_inf": {"f32bits": f32_bits(-math.inf), "e4m3": encode(-math.inf)},
    "nan":     {"f32bits": f32_bits(math.nan), "e4m3": encode(math.nan)},
    "neg_nan": {"f32bits": (f32_bits(math.nan) | 0x80000000), "e4m3": encode(struct.unpack('<f', struct.pack('<I', f32_bits(math.nan)|0x80000000))[0])},
}

out = {"format": "float8_e4m3fn", "ml_dtypes_version": ml_dtypes.__version__,
       "decode": decode_rows, "encode": encode_rows, "specials": specials}
with open("oracle_e4m3.json", "w") as fp:
    json.dump(out, fp)

# ---- headline: the flagged convention answers ----
def show(x):
    b = encode(x);
    return f"0x{b:02X} (sign={b>>7}, mag=0x{b&0x7f:02X}, isNaN={(b&0x7f)==0x7f})"
print("ml_dtypes", ml_dtypes.__version__, "float8_e4m3fn convention:")
print(f"  encode(448.0)   -> {show(448.0)}   [max finite = 0x7E]")
print(f"  encode(449.0)   -> {show(449.0)}")
print(f"  encode(463.9)   -> {show(463.9)}   [< midpoint 448..(2^9)=496? actual next-up boundary]")
print(f"  encode(464.0)   -> {show(464.0)}   [464 = (448+480)/2 tie region]")
print(f"  encode(480.0)   -> {show(480.0)}")
print(f"  encode(512.0)   -> {show(512.0)}")
print(f"  encode(1000.0)  -> {show(1000.0)}")
print(f"  encode(1e30)    -> {show(1e30)}")
print(f"  encode(+Inf)    -> {show(math.inf)}")
print(f"  encode(-Inf)    -> {show(-math.inf)}")
print(f"  encode(NaN)     -> {show(math.nan)}")
print(f"  decode(0x7F)    -> bits=0x{decode(0x7F):08X}  decode(0xFF) -> bits=0x{decode(0xFF):08X}")
print(f"  decode(0x7E)    -> {struct.unpack('<f', struct.pack('<I', decode(0x7E)))[0]}  (max finite)")
print(f"  wrote oracle_e4m3.json: {len(decode_rows)} decode, {len(encode_rows)} encode rows")
