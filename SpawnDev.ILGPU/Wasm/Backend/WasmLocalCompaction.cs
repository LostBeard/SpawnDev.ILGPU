using System;
using System.Collections.Generic;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Reuses Wasm locals within a finished function body. The code generators give every IR value its own local
    /// and never reuse one, so a large kernel declares one local per value: SpawnDev.Codecs' VP9 loop filter
    /// (WavefrontKernel) declared 68,382 and V8 rejected the module ("local count too large", its limit is
    /// 50,000). Most of those values live inside one straight-line stretch of code.
    /// </summary>
    /// <remarks>
    /// Works on the emitted bytecode alone, so it does not depend on how a generator laid out its state machine,
    /// spills or helpers. A local is <b>block-local</b> when every touch is inside one basic block (code between
    /// two control instructions) and the first touch writes it: it is dead when that block ends, before control
    /// can come back to its first write. Block-local locals share per-type slots through a linear scan over each
    /// block; every other touched local (read before written, or touched in more than one block: phis,
    /// loop-carried values, spilled state) keeps a slot of its own; locals no instruction touches are dropped.
    /// Parameters are never renumbered. Basic-block boundaries are block, loop, if, else, end, br, br_if,
    /// br_table, return and unreachable; a call is not one (a callee cannot touch the caller's locals).
    /// An instruction the decoder does not know throws, so a gap fails the compile instead of corrupting it.
    /// </remarks>
    internal static class WasmLocalCompaction
    {
        /// <summary>Reuses block-local locals of one function body.</summary>
        /// <param name="code">The body's instructions (without the final <c>end</c>).</param>
        /// <param name="locals">The declared locals (excluding parameters).</param>
        /// <param name="paramCount">The function's parameter count.</param>
        /// <param name="declaredBefore">Declared locals before.</param>
        /// <param name="declaredAfter">Declared locals after.</param>
        /// <returns>The rewritten code and local declarations.</returns>
        public static (byte[] Code, List<WasmLocal> Locals) Compact(
            byte[] code, List<WasmLocal> locals, int paramCount, out int declaredBefore, out int declaredAfter)
        {
            var types = new List<byte>();
            foreach (var l in locals)
                for (uint i = 0; i < l.Count; i++)
                    types.Add(l.Type);
            int n = types.Count;
            declaredBefore = n;

            // Pass 1: per declared local, the basic block of its first touch, whether it is touched in another
            // block, whether its first touch writes, and the ordinals of its first and last touches.
            var firstBlock = new int[n];
            var firstTouch = new int[n];
            var lastTouch = new int[n];
            var multiBlock = new bool[n];
            var firstIsWrite = new bool[n];
            Array.Fill(firstBlock, -1);
            int block = 0, ordinal = 0;
            Decode(code, (op, local, immStart, immEnd) =>
            {
                ordinal++;
                if (IsBlockBoundary(op)) { block++; return; }
                if (local < 0) return;
                int d = local - paramCount;
                if (d < 0) return;
                if (d >= n) throw new InvalidOperationException($"Wasm local {local} is not declared ({paramCount} params + {n} locals)");
                if (firstBlock[d] < 0)
                {
                    firstBlock[d] = block;
                    firstTouch[d] = ordinal;
                    firstIsWrite[d] = op != OpLocalGet;
                }
                else if (firstBlock[d] != block)
                {
                    multiBlock[d] = true;
                }
                lastTouch[d] = ordinal;
            });

            // Classify; own slots for every touched local that is not block-local, ordered by type.
            var newIndex = new int[n];
            Array.Fill(newIndex, -1);
            var blockLocal = new bool[n];
            var ownSlots = new List<int>();
            for (int d = 0; d < n; d++)
            {
                if (firstBlock[d] < 0) continue; // never touched: dropped
                if (!multiBlock[d] && firstIsWrite[d]) blockLocal[d] = true;
                else ownSlots.Add(d);
            }
            ownSlots.Sort((a, b) => types[a] != types[b] ? types[a].CompareTo(types[b]) : a.CompareTo(b));
            var outTypes = new List<byte>(ownSlots.Count + 64);
            foreach (int d in ownSlots)
            {
                newIndex[d] = paramCount + outTypes.Count;
                outTypes.Add(types[d]);
            }

            // Pass 2: linear scan, in instruction order, of the block-local locals onto pooled slots per type.
            // A slot is taken at a local's first touch and returned after its last one.
            var poolSlotType = new List<byte>();
            var freeSlots = new Dictionary<byte, Stack<int>>();
            var slotOf = new int[n];
            ordinal = 0;
            Decode(code, (op, local, immStart, immEnd) =>
            {
                ordinal++;
                if (local < 0) return;
                int d = local - paramCount;
                if (d < 0 || !blockLocal[d]) return;
                if (ordinal == firstTouch[d])
                {
                    byte t = types[d];
                    if (!freeSlots.TryGetValue(t, out var stack)) freeSlots[t] = stack = new Stack<int>();
                    if (stack.Count > 0) slotOf[d] = stack.Pop();
                    else { slotOf[d] = poolSlotType.Count; poolSlotType.Add(t); }
                }
                if (ordinal == lastTouch[d])
                    freeSlots[types[d]].Push(slotOf[d]);
            });

            // Pool slots after the own slots, grouped by type.
            var poolOrder = new List<int>();
            for (int s = 0; s < poolSlotType.Count; s++) poolOrder.Add(s);
            poolOrder.Sort((a, b) => poolSlotType[a] != poolSlotType[b] ? poolSlotType[a].CompareTo(poolSlotType[b]) : a.CompareTo(b));
            var poolIndex = new int[poolSlotType.Count];
            foreach (int s in poolOrder)
            {
                poolIndex[s] = paramCount + outTypes.Count;
                outTypes.Add(poolSlotType[s]);
            }
            for (int d = 0; d < n; d++)
                if (blockLocal[d]) newIndex[d] = poolIndex[slotOf[d]];
            declaredAfter = outTypes.Count;

            // Pass 3: rewrite the local indices.
            var output = new List<byte>(code.Length);
            int copied = 0;
            Decode(code, (op, local, immStart, immEnd) =>
            {
                if (local < paramCount) return;
                output.AddRange(new ArraySegment<byte>(code, copied, immStart - copied));
                WasmModuleBuilder.EmitU32Leb128(output, (uint)newIndex[local - paramCount]);
                copied = immEnd;
            });
            output.AddRange(new ArraySegment<byte>(code, copied, code.Length - copied));

            var outLocals = new List<WasmLocal>();
            foreach (var t in outTypes)
            {
                if (outLocals.Count > 0 && outLocals[^1].Type == t) outLocals[^1].Count++;
                else outLocals.Add(new WasmLocal { Count = 1, Type = t });
            }
            return (output.ToArray(), outLocals);
        }

        private const byte OpLocalGet = 0x20;

        private static bool IsBlockBoundary(byte op) => op switch
        {
            0x00 or 0x02 or 0x03 or 0x04 or 0x05 or 0x0B or 0x0C or 0x0D or 0x0E or 0x0F => true,
            _ => false,
        };

        /// <summary>
        /// Decodes every instruction; calls <paramref name="visit"/>(opcode, local index or -1, start of the local
        /// index immediate, end of it) for each. The opcode is the first byte (the prefix for 0xFC/0xFD/0xFE).
        /// </summary>
        internal static void Decode(byte[] b, Action<byte, int, int, int> visit)
        {
            int i = 0;
            while (i < b.Length)
            {
                int pos = i;
                byte op = b[i++];
                switch (op)
                {
                    case 0x02: case 0x03: case 0x04:
                        i = BlockType(b, i); break;
                    case 0x00: case 0x01: case 0x05: case 0x0B: case 0x0F:
                    case 0x1A: case 0x1B:
                        break;
                    case 0x0C: case 0x0D:
                    case 0x10:
                    case 0x23: case 0x24:
                        i = SkipU32(b, i); break;
                    case 0x0E:
                    {
                        uint count = ReadU32(b, ref i);
                        for (uint k = 0; k <= count; k++) i = SkipU32(b, i);
                        break;
                    }
                    case 0x11:
                        i = SkipU32(b, i); i = SkipU32(b, i); break;
                    case 0x1C:
                    {
                        uint count = ReadU32(b, ref i);
                        i += (int)count;
                        break;
                    }
                    case 0x20: case 0x21: case 0x22:
                    {
                        int immStart = i;
                        uint local = ReadU32(b, ref i);
                        visit(op, (int)local, immStart, i);
                        continue;
                    }
                    case 0x3F: case 0x40:
                        i += 1; break;
                    case 0x41: case 0x42:
                        i = SkipLeb(b, i); break;
                    case 0x43:
                        i += 4; break;
                    case 0x44:
                        i += 8; break;
                    case 0xFC:
                    {
                        uint sub = ReadU32(b, ref i);
                        if (sub <= 7) { }
                        else if (sub == 8) { i = SkipU32(b, i); i += 1; }
                        else if (sub == 9) i = SkipU32(b, i);
                        else if (sub == 10) i += 2;
                        else if (sub == 11) i += 1;
                        else throw Unknown(pos, $"0xFC {sub}");
                        break;
                    }
                    case 0xFD:
                    {
                        uint sub = ReadU32(b, ref i);
                        if (sub <= 11 || sub == 92 || sub == 93) i = MemArg(b, i);
                        else if (sub == 12 || sub == 13) i += 16;
                        else if (sub >= 21 && sub <= 34) i += 1;
                        else if (sub >= 84 && sub <= 91) { i = MemArg(b, i); i += 1; }
                        break;
                    }
                    case 0xFE:
                    {
                        uint sub = ReadU32(b, ref i);
                        if (sub == 3) i += 1;
                        else i = MemArg(b, i);
                        break;
                    }
                    default:
                        if (op >= 0x28 && op <= 0x3E) { i = MemArg(b, i); break; }
                        if (op >= 0x45 && op <= 0xC4) break;
                        throw Unknown(pos, $"0x{op:X2}");
                }
                visit(op, -1, pos, pos);
            }
        }

        private static Exception Unknown(int pos, string what) =>
            new InvalidOperationException($"Wasm local compaction: unknown instruction {what} at byte {pos}");

        private static int BlockType(byte[] b, int i)
        {
            byte t = b[i];
            if (t == 0x40 || t == 0x7F || t == 0x7E || t == 0x7D || t == 0x7C || t == 0x7B) return i + 1;
            return SkipLeb(b, i);
        }

        private static int MemArg(byte[] b, int i) => SkipU32(b, SkipU32(b, i));

        private static int SkipU32(byte[] b, int i) => SkipLeb(b, i);

        private static int SkipLeb(byte[] b, int i)
        {
            while ((b[i++] & 0x80) != 0) { }
            return i;
        }

        private static uint ReadU32(byte[] b, ref int i)
        {
            uint result = 0;
            int shift = 0;
            byte x;
            do
            {
                x = b[i++];
                result |= (uint)(x & 0x7F) << shift;
                shift += 7;
            }
            while ((x & 0x80) != 0);
            return result;
        }
    }
}
