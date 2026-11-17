using System;
using System.Collections.Generic;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// The buffer ranges a linear memory still holds after the dispatch that used them (serialized dispatch path).
    /// </summary>
    /// <remarks>
    /// A dispatch copies each argument buffer into the linear memory, runs, and copies it back out; a small
    /// launch over a big buffer paid that for every launch (a 64-thread kernel over a 32 MB buffer: 12.6 ms,
    /// SpawnDev.Codecs' VP9 wavefront 81x / 221x slower than WebGPU, 2026-11-17). Instead the copy-out is
    /// deferred: the range stays here, marked dirty, and the next dispatch that uses the same buffer at the same
    /// place skips both its copy-in and the previous copy-out. The bytes go back to the buffer's
    /// SharedArrayBuffer when anything needs them there: a host read or write of the buffer, a device copy, a
    /// dispatch that lays the memory out differently, the pipeline starting (its worker reads the buffers), and
    /// SynchronizeAsync. Only the serialized path keeps ranges here; the pipeline worker keeps its own for one
    /// pipeline (WorkerPool.cs), and this set is written back and emptied when a pipeline starts.
    /// The .NET side is single-threaded and a memory's dispatch window is serialized by its gate, so no lock.
    /// </remarks>
    internal sealed class WasmResidency
    {
        private sealed class Item
        {
            public WasmMemoryBuffer Buffer = null!;
            public int Source, Offset, Length, Hwc;
            public bool Dirty;
        }

        private readonly Func<SharedArrayBuffer?> _memory;
        private readonly List<Item> _items = new();

        public WasmResidency(Func<SharedArrayBuffer?> memory) => _memory = memory;

        /// <summary>Ranges currently held (diagnostic).</summary>
        public int Count => _items.Count;

        /// <summary>Copy-ins skipped because the range was still in place (diagnostic, process-wide).</summary>
        public static long Hits;
        /// <summary>Deferred copy-outs performed (diagnostic, process-wide).</summary>
        public static long WriteBacks;

        private void WriteBack(Item it)
        {
            if (!it.Dirty) return;
            it.Dirty = false;
            // A host write that reached the buffer while the range was here wins, as the immediate copy-out
            // skipped such a buffer (HostWriteCounter check, the 2026-05-03 copy-out race).
            if (it.Buffer.IsReleased || it.Buffer.HostWriteCounter != it.Hwc)
                return;
            var memory = _memory();
            if (memory == null) return;
            using var src = new Uint8Array(memory, it.Offset, it.Length);
            using var dst = new Uint8Array(it.Buffer.SharedBuffer, it.Source, it.Length);
            dst.JSRef!.CallVoid("set", src);
            WriteBacks++;
        }

        /// <summary>
        /// Writes <paramref name="buffer"/>'s range back to its SharedArrayBuffer if it is dirty here; with
        /// <paramref name="drop"/> also forgets it (the caller is about to change the SharedArrayBuffer).
        /// </summary>
        public void Flush(WasmMemoryBuffer buffer, bool drop)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it.Buffer != buffer) continue;
                WriteBack(it);
                if (drop)
                {
                    _items.RemoveAt(i);
                    buffer.Residency = null;
                }
            }
        }

        /// <summary>Forgets <paramref name="buffer"/> without writing it back (the buffer is being released).</summary>
        public void Forget(WasmMemoryBuffer buffer)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i].Buffer == buffer) _items.RemoveAt(i);
            buffer.Residency = null;
        }

        /// <summary>Writes every dirty range back; with <paramref name="drop"/> also forgets them all.</summary>
        public void FlushAll(bool drop)
        {
            foreach (var it in _items)
                WriteBack(it);
            if (!drop) return;
            foreach (var it in _items)
                it.Buffer.Residency = null;
            _items.Clear();
        }

        /// <summary>
        /// Start of a serialized dispatch: which of its ranges are already in place (copy-in skipped), after
        /// writing back and forgetting every held range it does not reuse at the same place (its zeroing and
        /// copy-ins may overwrite them). A buffer in <paramref name="forceMiss"/> (read from a host-write
        /// snapshot) is never a hit.
        /// </summary>
        public bool[] BeginDispatch(
            IReadOnlyList<WasmMemoryBuffer> buffers, IReadOnlyList<(int Source, int Offset, int Length)> ranges,
            ISet<int>? forceMiss)
        {
            var hit = new bool[buffers.Count];
            for (int k = _items.Count - 1; k >= 0; k--)
            {
                var it = _items[k];
                int match = -1;
                for (int i = 0; i < buffers.Count; i++)
                {
                    if (buffers[i] != it.Buffer) continue;
                    var r = ranges[i];
                    if (r.Source == it.Source && r.Offset == it.Offset && r.Length == it.Length
                        && it.Buffer.HostWriteCounter == it.Hwc && !(forceMiss?.Contains(i) ?? false))
                        match = i;
                    break;
                }
                if (match >= 0)
                {
                    hit[match] = true;
                    Hits++;
                    continue;
                }
                WriteBack(it);
                it.Buffer.Residency = null;
                _items.RemoveAt(k);
            }
            return hit;
        }

        /// <summary>
        /// End of a serialized dispatch: its ranges are held here, dirty, instead of being copied out. A buffer
        /// in <paramref name="exclude"/> is not held (its copy-out was skipped for a host write or a snapshot
        /// read, as before).
        /// </summary>
        public void EndDispatch(
            IReadOnlyList<WasmMemoryBuffer> buffers, IReadOnlyList<(int Source, int Offset, int Length)> ranges,
            IReadOnlyList<int> hwcAtCopyIn, ISet<int> exclude)
        {
            for (int i = 0; i < buffers.Count; i++)
            {
                var buf = buffers[i];
                var r = ranges[i];
                // forget whatever was held for this buffer (a hit is replaced by its new, dirty state)
                for (int k = _items.Count - 1; k >= 0; k--)
                    if (_items[k].Buffer == buf) _items.RemoveAt(k);
                buf.Residency = null;
                if (exclude.Contains(i) || r.Length <= 0 || buf.IsReleased)
                    continue;
                _items.Add(new Item { Buffer = buf, Source = r.Source, Offset = r.Offset, Length = r.Length, Hwc = hwcAtCopyIn[i], Dirty = true });
                buf.Residency = this;
            }
        }
    }
}
