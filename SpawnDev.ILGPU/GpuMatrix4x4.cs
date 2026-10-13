// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GpuMatrix4x4.cs
//
// GPU-friendly 4x4 matrix stored in column-major order for use in ILGPU kernels.
// Auto-transposes from .NET's row-major System.Numerics.Matrix4x4.
// ---------------------------------------------------------------------------------------

using System.Numerics;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// A GPU-friendly 4x4 matrix stored in column-major order (GPU convention: M * v).
    /// <para>
    /// .NET's <see cref="Matrix4x4"/> uses row-major layout (v * M convention),
    /// meaning translation lives in M41/M42/M43. This struct transposes on construction
    /// so GPU kernels can use standard column-vector multiplication (M * v).
    /// </para>
    /// <para>
    /// Use <see cref="FromMatrix4x4"/> to create from a .NET matrix, then pass directly
    /// as a kernel parameter or in a buffer. Use the static Transform methods inside kernels.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Layout (column-major):
    /// <code>
    /// | R00 R01 R02 R03 |   | x |
    /// | R10 R11 R12 R13 | * | y |
    /// | R20 R21 R22 R23 |   | z |
    /// | R30 R31 R32 R33 |   | 1 |
    /// </code>
    /// Where R03/R13/R23 hold the translation (from .NET's M41/M42/M43).
    /// </remarks>
    public readonly struct GpuMatrix4x4
    {
        // Row 0 (from .NET column 0)
        /// <summary>Row 0, column 0 (from .NET <c>M11</c>).</summary>
        public readonly float R00;
        /// <summary>Row 0, column 1 (from .NET <c>M21</c>).</summary>
        public readonly float R01;
        /// <summary>Row 0, column 2 (from .NET <c>M31</c>).</summary>
        public readonly float R02;
        /// <summary>Row 0, column 3: X translation (from .NET <c>M41</c>).</summary>
        public readonly float R03;

        // Row 1 (from .NET column 1)
        /// <summary>Row 1, column 0 (from .NET <c>M12</c>).</summary>
        public readonly float R10;
        /// <summary>Row 1, column 1 (from .NET <c>M22</c>).</summary>
        public readonly float R11;
        /// <summary>Row 1, column 2 (from .NET <c>M32</c>).</summary>
        public readonly float R12;
        /// <summary>Row 1, column 3: Y translation (from .NET <c>M42</c>).</summary>
        public readonly float R13;

        // Row 2 (from .NET column 2)
        /// <summary>Row 2, column 0 (from .NET <c>M13</c>).</summary>
        public readonly float R20;
        /// <summary>Row 2, column 1 (from .NET <c>M23</c>).</summary>
        public readonly float R21;
        /// <summary>Row 2, column 2 (from .NET <c>M33</c>).</summary>
        public readonly float R22;
        /// <summary>Row 2, column 3: Z translation (from .NET <c>M43</c>).</summary>
        public readonly float R23;

        // Row 3 (from .NET column 3)
        /// <summary>Row 3, column 0 (from .NET <c>M14</c>).</summary>
        public readonly float R30;
        /// <summary>Row 3, column 1 (from .NET <c>M24</c>).</summary>
        public readonly float R31;
        /// <summary>Row 3, column 2 (from .NET <c>M34</c>).</summary>
        public readonly float R32;
        /// <summary>Row 3, column 3 (from .NET <c>M44</c>).</summary>
        public readonly float R33;

        /// <summary>
        /// Constructs a GpuMatrix4x4 with explicit values (already in column-major/GPU order).
        /// </summary>
        public GpuMatrix4x4(
            float r00, float r01, float r02, float r03,
            float r10, float r11, float r12, float r13,
            float r20, float r21, float r22, float r23,
            float r30, float r31, float r32, float r33)
        {
            R00 = r00; R01 = r01; R02 = r02; R03 = r03;
            R10 = r10; R11 = r11; R12 = r12; R13 = r13;
            R20 = r20; R21 = r21; R22 = r22; R23 = r23;
            R30 = r30; R31 = r31; R32 = r32; R33 = r33;
        }

        /// <summary>
        /// Creates a <see cref="GpuMatrix4x4"/> from a .NET <see cref="Matrix4x4"/>.
        /// Automatically transposes from .NET's row-major (v * M) to GPU column-major (M * v).
        /// </summary>
        /// <remarks>
        /// .NET Matrix4x4 stores as:
        /// <code>
        /// | M11 M12 M13 M14 |   Row 1
        /// | M21 M22 M23 M24 |   Row 2
        /// | M31 M32 M33 M34 |   Row 3
        /// | M41 M42 M43 M44 |   Row 4 (translation here for affine transforms)
        /// </code>
        /// This method transposes it so the GPU can do M * v multiplication:
        /// GPU Row 0 = .NET Column 0 = [M11, M21, M31, M41]
        /// GPU Row 1 = .NET Column 1 = [M12, M22, M32, M42]
        /// GPU Row 2 = .NET Column 2 = [M13, M23, M33, M43]
        /// GPU Row 3 = .NET Column 3 = [M14, M24, M34, M44]
        /// </remarks>
        public static GpuMatrix4x4 FromMatrix4x4(Matrix4x4 m)
        {
            return new GpuMatrix4x4(
                m.M11, m.M21, m.M31, m.M41,  // GPU row 0 = .NET col 0
                m.M12, m.M22, m.M32, m.M42,  // GPU row 1 = .NET col 1
                m.M13, m.M23, m.M33, m.M43,  // GPU row 2 = .NET col 2
                m.M14, m.M24, m.M34, m.M44   // GPU row 3 = .NET col 3
            );
        }

        /// <summary>
        /// Creates an identity matrix.
        /// </summary>
        public static GpuMatrix4x4 Identity => new GpuMatrix4x4(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        );

        /// <summary>
        /// Transforms a 3D point by this matrix (applies rotation + translation).
        /// Returns (resultX, resultY, resultZ). Suitable for use in ILGPU kernels.
        /// </summary>
        public static void TransformPoint(
            GpuMatrix4x4 m,
            float x, float y, float z,
            out float rx, out float ry, out float rz)
        {
            rx = m.R00 * x + m.R01 * y + m.R02 * z + m.R03;
            ry = m.R10 * x + m.R11 * y + m.R12 * z + m.R13;
            rz = m.R20 * x + m.R21 * y + m.R22 * z + m.R23;
        }

        /// <summary>
        /// Transforms a 3D direction vector by this matrix (rotation only, no translation).
        /// Suitable for use in ILGPU kernels.
        /// </summary>
        public static void TransformDirection(
            GpuMatrix4x4 m,
            float x, float y, float z,
            out float rx, out float ry, out float rz)
        {
            rx = m.R00 * x + m.R01 * y + m.R02 * z;
            ry = m.R10 * x + m.R11 * y + m.R12 * z;
            rz = m.R20 * x + m.R21 * y + m.R22 * z;
        }

        /// <summary>
        /// Gets element at specified row and column (0-indexed).
        /// Not suitable for performance-critical kernel code (use fields directly).
        /// </summary>
        public float this[int row, int col]
        {
            get
            {
                int idx = row * 4 + col;
                return idx switch
                {
                    0 => R00, 1 => R01, 2 => R02, 3 => R03,
                    4 => R10, 5 => R11, 6 => R12, 7 => R13,
                    8 => R20, 9 => R21, 10 => R22, 11 => R23,
                    12 => R30, 13 => R31, 14 => R32, 15 => R33,
                    _ => 0f
                };
            }
        }
    }
}
