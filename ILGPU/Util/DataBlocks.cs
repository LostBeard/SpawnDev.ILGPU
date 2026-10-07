// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: DataBlocks.tt/DataBlocks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

// disable: max_line_length
#pragma warning disable IDE0047 // Remove unnecessary parentheses

namespace ILGPU.Util
{
    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2> : IEquatable<DataBlock<T1, T2>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2)
        {
            Item1 = param1;
            Item2 = param2;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2) ToValueTuple() =>
            (Item1, Item2);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2)(DataBlock<T1, T2> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2>((T1, T2) valueTuple) =>
            new DataBlock<T1, T2>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2> left, DataBlock<T1, T2> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2> left, DataBlock<T1, T2> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3> : IEquatable<DataBlock<T1, T2, T3>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3) ToValueTuple() =>
            (Item1, Item2, Item3);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3)(DataBlock<T1, T2, T3> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3>((T1, T2, T3) valueTuple) =>
            new DataBlock<T1, T2, T3>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3> left, DataBlock<T1, T2, T3> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3> left, DataBlock<T1, T2, T3> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4> : IEquatable<DataBlock<T1, T2, T3, T4>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4) ToValueTuple() =>
            (Item1, Item2, Item3, Item4);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4)(DataBlock<T1, T2, T3, T4> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4>((T1, T2, T3, T4) valueTuple) =>
            new DataBlock<T1, T2, T3, T4>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4> left, DataBlock<T1, T2, T3, T4> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4> left, DataBlock<T1, T2, T3, T4> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5> : IEquatable<DataBlock<T1, T2, T3, T4, T5>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5)(DataBlock<T1, T2, T3, T4, T5> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5>((T1, T2, T3, T4, T5) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5> left, DataBlock<T1, T2, T3, T4, T5> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5> left, DataBlock<T1, T2, T3, T4, T5> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6)(DataBlock<T1, T2, T3, T4, T5, T6> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6>((T1, T2, T3, T4, T5, T6) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6> left, DataBlock<T1, T2, T3, T4, T5, T6> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6> left, DataBlock<T1, T2, T3, T4, T5, T6> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7)(DataBlock<T1, T2, T3, T4, T5, T6, T7> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7>((T1, T2, T3, T4, T5, T6, T7) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7> left, DataBlock<T1, T2, T3, T4, T5, T6, T7> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7> left, DataBlock<T1, T2, T3, T4, T5, T6, T7> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8>((T1, T2, T3, T4, T5, T6, T7, T8) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9>((T1, T2, T3, T4, T5, T6, T7, T8, T9) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    /// <typeparam name="T11">The element type of the 11-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
        where T11 : unmanaged, IEquatable<T11>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
            Item11 = param11;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
            Item11 = valueTuple.Item11;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T11 Item11 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10) && Item11.Equals(other.Item11);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode() ^ Item11.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ", " + Item11.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    /// <typeparam name="T11">The element type of the 11-th element.</typeparam>
    /// <typeparam name="T12">The element type of the 12-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
        where T11 : unmanaged, IEquatable<T11>
        where T12 : unmanaged, IEquatable<T12>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
            Item11 = param11;
            Item12 = param12;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
            Item11 = valueTuple.Item11;
            Item12 = valueTuple.Item12;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T11 Item11 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T12 Item12 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11, Item12);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10) && Item11.Equals(other.Item11) && Item12.Equals(other.Item12);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode() ^ Item11.GetHashCode() ^ Item12.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ", " + Item11.ToString() + ", " + Item12.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    /// <typeparam name="T11">The element type of the 11-th element.</typeparam>
    /// <typeparam name="T12">The element type of the 12-th element.</typeparam>
    /// <typeparam name="T13">The element type of the 13-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
        where T11 : unmanaged, IEquatable<T11>
        where T12 : unmanaged, IEquatable<T12>
        where T13 : unmanaged, IEquatable<T13>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
            Item11 = param11;
            Item12 = param12;
            Item13 = param13;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
            Item11 = valueTuple.Item11;
            Item12 = valueTuple.Item12;
            Item13 = valueTuple.Item13;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T11 Item11 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T12 Item12 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T13 Item13 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11, Item12, Item13);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10) && Item11.Equals(other.Item11) && Item12.Equals(other.Item12) && Item13.Equals(other.Item13);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode() ^ Item11.GetHashCode() ^ Item12.GetHashCode() ^ Item13.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ", " + Item11.ToString() + ", " + Item12.ToString() + ", " + Item13.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    /// <typeparam name="T11">The element type of the 11-th element.</typeparam>
    /// <typeparam name="T12">The element type of the 12-th element.</typeparam>
    /// <typeparam name="T13">The element type of the 13-th element.</typeparam>
    /// <typeparam name="T14">The element type of the 14-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
        where T11 : unmanaged, IEquatable<T11>
        where T12 : unmanaged, IEquatable<T12>
        where T13 : unmanaged, IEquatable<T13>
        where T14 : unmanaged, IEquatable<T14>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
            Item11 = param11;
            Item12 = param12;
            Item13 = param13;
            Item14 = param14;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
            Item11 = valueTuple.Item11;
            Item12 = valueTuple.Item12;
            Item13 = valueTuple.Item13;
            Item14 = valueTuple.Item14;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T11 Item11 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T12 Item12 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T13 Item13 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T14 Item14 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11, Item12, Item13, Item14);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10) && Item11.Equals(other.Item11) && Item12.Equals(other.Item12) && Item13.Equals(other.Item13) && Item14.Equals(other.Item14);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode() ^ Item11.GetHashCode() ^ Item12.GetHashCode() ^ Item13.GetHashCode() ^ Item14.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ", " + Item11.ToString() + ", " + Item12.ToString() + ", " + Item13.ToString() + ", " + Item14.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> right) =>
            !(left == right);

        #endregion
    }

    /// <summary>
    /// Represents a tuple consisting of multiple elements.
    /// </summary>
    /// <typeparam name="T1">The element type of the 1st element.</typeparam>
    /// <typeparam name="T2">The element type of the 2nd element.</typeparam>
    /// <typeparam name="T3">The element type of the 3-th element.</typeparam>
    /// <typeparam name="T4">The element type of the 4-th element.</typeparam>
    /// <typeparam name="T5">The element type of the 5-th element.</typeparam>
    /// <typeparam name="T6">The element type of the 6-th element.</typeparam>
    /// <typeparam name="T7">The element type of the 7-th element.</typeparam>
    /// <typeparam name="T8">The element type of the 8-th element.</typeparam>
    /// <typeparam name="T9">The element type of the 9-th element.</typeparam>
    /// <typeparam name="T10">The element type of the 10-th element.</typeparam>
    /// <typeparam name="T11">The element type of the 11-th element.</typeparam>
    /// <typeparam name="T12">The element type of the 12-th element.</typeparam>
    /// <typeparam name="T13">The element type of the 13-th element.</typeparam>
    /// <typeparam name="T14">The element type of the 14-th element.</typeparam>
    /// <typeparam name="T15">The element type of the 15-th element.</typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public struct DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IEquatable<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>
        where T1 : unmanaged, IEquatable<T1>
        where T2 : unmanaged, IEquatable<T2>
        where T3 : unmanaged, IEquatable<T3>
        where T4 : unmanaged, IEquatable<T4>
        where T5 : unmanaged, IEquatable<T5>
        where T6 : unmanaged, IEquatable<T6>
        where T7 : unmanaged, IEquatable<T7>
        where T8 : unmanaged, IEquatable<T8>
        where T9 : unmanaged, IEquatable<T9>
        where T10 : unmanaged, IEquatable<T10>
        where T11 : unmanaged, IEquatable<T11>
        where T12 : unmanaged, IEquatable<T12>
        where T13 : unmanaged, IEquatable<T13>
        where T14 : unmanaged, IEquatable<T14>
        where T15 : unmanaged, IEquatable<T15>
    {
        #region Constants

        /// <summary>
        /// Represents the native size of a single element.
        /// </summary>
        // A property, not a static field: no static constructor on generic device structs (ArrayView<T>'s note, 2026-10-07).
        public static int ElementSize => Interop.SizeOf<DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>>();

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock(T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14, T15 param15)
        {
            Item1 = param1;
            Item2 = param2;
            Item3 = param3;
            Item4 = param4;
            Item5 = param5;
            Item6 = param6;
            Item7 = param7;
            Item8 = param8;
            Item9 = param9;
            Item10 = param10;
            Item11 = param11;
            Item12 = param12;
            Item13 = param13;
            Item14 = param14;
            Item15 = param15;
        }

        /// <summary>
        /// Constructs a new data block.
        /// </summary>
        public DataBlock((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15) valueTuple)
        {
            Item1 = valueTuple.Item1;
            Item2 = valueTuple.Item2;
            Item3 = valueTuple.Item3;
            Item4 = valueTuple.Item4;
            Item5 = valueTuple.Item5;
            Item6 = valueTuple.Item6;
            Item7 = valueTuple.Item7;
            Item8 = valueTuple.Item8;
            Item9 = valueTuple.Item9;
            Item10 = valueTuple.Item10;
            Item11 = valueTuple.Item11;
            Item12 = valueTuple.Item12;
            Item13 = valueTuple.Item13;
            Item14 = valueTuple.Item14;
            Item15 = valueTuple.Item15;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T1 Item1 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T2 Item2 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T3 Item3 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T4 Item4 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T5 Item5 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T6 Item6 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T7 Item7 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T8 Item8 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T9 Item9 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T10 Item10 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T11 Item11 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T12 Item12 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T13 Item13 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T14 Item14 { get; set; }

        /// <summary>
        /// Returns the i-th element.
        /// </summary>
        public T15 Item15 { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Returns a value tuple that stores all items.
        /// </summary>
        /// <returns>The constructed value tuple.</returns>
        public (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15) ToValueTuple() =>
            (Item1, Item2, Item3, Item4, Item5, Item6, Item7, Item8, Item9, Item10, Item11, Item12, Item13, Item14, Item15);

        /// <summary cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> other) =>
            Item1.Equals(other.Item1) && Item2.Equals(other.Item2) && Item3.Equals(other.Item3) && Item4.Equals(other.Item4) && Item5.Equals(other.Item5) && Item6.Equals(other.Item6) && Item7.Equals(other.Item7) && Item8.Equals(other.Item8) && Item9.Equals(other.Item9) && Item10.Equals(other.Item10) && Item11.Equals(other.Item11) && Item12.Equals(other.Item12) && Item13.Equals(other.Item13) && Item14.Equals(other.Item14) && Item15.Equals(other.Item15);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current one.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current one.</returns>
        public override bool Equals(object? obj) =>
            obj is DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> other && Equals(other);

        /// <summary>
        /// Returns the combined hash code of all items.
        /// </summary>
        /// <returns>The hash code of all items.</returns>
        public override int GetHashCode() => 
            Item1.GetHashCode() ^ Item2.GetHashCode() ^ Item3.GetHashCode() ^ Item4.GetHashCode() ^ Item5.GetHashCode() ^ Item6.GetHashCode() ^ Item7.GetHashCode() ^ Item8.GetHashCode() ^ Item9.GetHashCode() ^ Item10.GetHashCode() ^ Item11.GetHashCode() ^ Item12.GetHashCode() ^ Item13.GetHashCode() ^ Item14.GetHashCode() ^ Item15.GetHashCode();

        /// <summary>
        /// Returns the string representation of this view.
        /// </summary>
        /// <returns>The string representation of this view.</returns>
        public override string ToString() =>
            "(" + Item1.ToString() + ", " + Item2.ToString() + ", " + Item3.ToString() + ", " + Item4.ToString() + ", " + Item5.ToString() + ", " + Item6.ToString() + ", " + Item7.ToString() + ", " + Item8.ToString() + ", " + Item9.ToString() + ", " + Item10.ToString() + ", " + Item11.ToString() + ", " + Item12.ToString() + ", " + Item13.ToString() + ", " + Item14.ToString() + ", " + Item15.ToString() + ")";

        #endregion

        #region Operators

        /// <summary>
        /// Converts the given data block into its tuple representation.
        /// </summary>
        /// <param name="dataBlock">The data block to convert.</param>
        public static implicit operator (T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15)(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> dataBlock) =>
            dataBlock.ToValueTuple();

        /// <summary>
        /// Converts the given tuple into its data block representation.
        /// </summary>
        /// <param name="valueTuple">The value tuple to convert.</param>
        public static implicit operator DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>((T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15) valueTuple) =>
            new DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(valueTuple);

        /// <summary>
        /// Returns true if both data blocks represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block represent the same elements.</returns>
        public static bool operator ==(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> right) =>
            left.Equals(right);

        /// <summary>
        /// Returns true if both data blocks do not represent the same elements.
        /// </summary>
        /// <param name="left">The left block.</param>
        /// <param name="right">The right block.</param>
        /// <returns>True, if both data block do not represent the same elements.</returns>
        public static bool operator !=(DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> left, DataBlock<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> right) =>
            !(left == right);

        #endregion
    }

}

#pragma warning restore IDE0047 // Remove unnecessary parentheses