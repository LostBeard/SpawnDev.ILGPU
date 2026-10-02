// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 SpawnDev
//
// File: DelegateSpecialization.cs
//
// Wrapper type for passing delegates as kernel parameters via compile-time
// specialization. At dispatch time, the delegate's target method is resolved
// and inlined into the kernel — the delegate never reaches the GPU.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILGPU.Runtime
{
    /// <summary>
    /// A wrapper for passing delegate parameters to GPU kernels.
    /// The delegate is resolved at dispatch time and its target method
    /// is inlined into the kernel via compile-time specialization.
    /// The delegate itself never reaches the GPU.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type (e.g., Func&lt;int,int&gt;).</typeparam>
    /// <example>
    /// <code>
    /// static int Negate(int x) => -x;
    ///
    /// static void MapKernel(Index1D index, ArrayView&lt;int&gt; buf,
    ///     DelegateSpecialization&lt;Func&lt;int,int&gt;&gt; transform)
    /// {
    ///     buf[index] = transform.Value(buf[index]);
    /// }
    ///
    /// var kernel = accelerator.LoadAutoGroupedStreamKernel&lt;...&gt;(MapKernel);
    /// kernel(size, buffer, new DelegateSpecialization&lt;Func&lt;int,int&gt;&gt;(Negate));
    /// </code>
    /// </example>
    /// <summary>
    /// The wrapped delegate of a <see cref="DelegateSpecialization{TDelegate}"/>, for the launch path
    /// (DelegateSpecializationRouter), which reads it per launch and must not use reflection to do so.
    /// </summary>
    internal interface IDelegateSpecialization
    {
        /// <summary>The wrapped delegate (null for a default-constructed value).</summary>
        Delegate? Target { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DelegateSpecialization<TDelegate>
        : IEquatable<DelegateSpecialization<TDelegate>>, IDelegateSpecialization
        where TDelegate : Delegate
    {
        #region Instance

        /// <summary>
        /// The wrapped delegate. Only accessed at the dispatch boundary,
        /// never on the GPU.
        /// </summary>
        internal readonly TDelegate _delegate;

        /// <summary>
        /// Constructs a new delegate specialization.
        /// </summary>
        /// <param name="delegate">The delegate to specialize with.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="delegate"/> is null.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// Thrown when the delegate target is a capturing lambda or
        /// instance method (only static methods and non-capturing lambdas
        /// are supported).
        /// </exception>
        public DelegateSpecialization(TDelegate @delegate)
        {
            _delegate = @delegate
                ?? throw new ArgumentNullException(nameof(@delegate));

            // Validate: must be a static method
            var method = @delegate.Method;
            if (!method.IsStatic)
            {
                throw new NotSupportedException(
                    "Only static methods can be used as " +
                    "DelegateSpecialization targets. Use a static " +
                    "method instead of a lambda expression. " +
                    "Example: static int Negate(int x) => -x;");
            }

            // Reject multicast delegates (HasSingleTarget: GetInvocationList allocated an array per construction,
            // and callers construct one per launch)
            if (!@delegate.HasSingleTarget)
            {
                throw new NotSupportedException(
                    "Multicast delegates are not supported as " +
                    "DelegateSpecialization targets.");
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the wrapped delegate value.
        /// In kernel code, calls to this property's result are resolved
        /// and inlined at compile time.
        /// </summary>
        public TDelegate Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _delegate;
        }

        /// <inheritdoc/>
        Delegate? IDelegateSpecialization.Target => _delegate;

        #endregion

        #region IEquatable

        /// <summary>
        /// Equality is based on the delegate's target MethodInfo,
        /// which is the specialization cache key.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(DelegateSpecialization<TDelegate> other) =>
            _delegate?.Method == other._delegate?.Method;

        #endregion

        #region Object

        /// <inheritdoc/>
        public override bool Equals(object? obj) =>
            obj is DelegateSpecialization<TDelegate> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            _delegate?.Method?.GetHashCode() ?? 0;

        /// <inheritdoc/>
        public override string? ToString() =>
            _delegate?.Method?.Name ?? "(null)";

        #endregion

        #region Operators

        /// <summary>Returns true if both specializations target the same method.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(
            DelegateSpecialization<TDelegate> left,
            DelegateSpecialization<TDelegate> right) =>
            left.Equals(right);

        /// <summary>Returns true if the specializations target different methods.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(
            DelegateSpecialization<TDelegate> left,
            DelegateSpecialization<TDelegate> right) =>
            !left.Equals(right);

        #endregion
    }
}
