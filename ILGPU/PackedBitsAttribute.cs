// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: PackedBitsAttribute.cs
//
// Marks a value type as a SUB-BYTE packed element type: an ArrayView<T> of it allocates and
// addresses the value at the declared bit width instead of one whole byte per element. Used by
// the 4-bit types (Int4 / UInt4 / Float4E2M1), where two elements share a byte (8 per u32). The
// per-element value still occupies a 1-byte CLR struct in host memory (value in the low nibble);
// only the DEVICE buffer is packed (ceil(N * Bits / 8) bytes) and the kernel load/store
// nibble-addresses it. Without this attribute a type is treated as whole-byte (Bits = sizeof*8).
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU
{
    /// <summary>
    /// Declares that an <see cref="ArrayView{T}"/> of the annotated value type packs its elements
    /// at <see cref="Bits"/> bits each (sub-byte), rather than one whole byte per element. Only
    /// 4 is supported today (the 4-bit Int4 / UInt4 / Float4E2M1 types, two per byte).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class PackedBitsAttribute : Attribute
    {
        /// <summary>
        /// Constructs the attribute with the given packed bit width.
        /// </summary>
        /// <param name="bits">The number of bits per element (currently 4).</param>
        public PackedBitsAttribute(int bits)
        {
            Bits = bits;
        }

        /// <summary>
        /// The number of bits per logical element.
        /// </summary>
        public int Bits { get; }
    }
}
