using System;
using System.Buffers;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.Mvvm.SourceGenerators.Helpers;

/// <summary>
/// A pooled array builder.
/// </summary>
/// <typeparam name="T">The type of items to create arrays of.</typeparam>
internal ref partial struct PooledArrayBuilder<T>
{
    /// <summary>
    /// Array rented from the array pool and used to back <see cref="span"/>.
    /// </summary>
    private T[]? arrayToReturnToPool;

    /// <summary>
    /// The span to write into.
    /// </summary>
    private Span<T> span;

    /// <summary>
    /// The position at which to write the next <typeparamref name="T"/> value.
    /// </summary>
    private int position;

    /// <summary>
    /// Creates a new <see cref="PooledArrayBuilder{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity for the underlying buffer.</param>
    public PooledArrayBuilder(int initialCapacity = 128)
    {
        this.span = this.arrayToReturnToPool = ArrayPool<T>.Shared.Rent(initialCapacity);
        this.position = 0;
    }

    /// <summary>
    /// Creates a new <see cref="PooledArrayBuilder{T}"/> instance with the specified parameters.
    /// </summary>
    /// <param name="initialSpan">The initial scratch buffer to write into.</param>
    public PooledArrayBuilder(Span<T> initialSpan)
    {
        this.span = initialSpan;
        this.arrayToReturnToPool = null;
        this.position = 0;
    }

    /// <inheritdoc cref="ImmutableArray{T}.Builder.Add(T)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        Span<T> span = this.span;
        int position = this.position;
        
        if ((uint)position < (uint)span.Length)
        {
            span[position] = item;

            this.position = position + 1;
        }
        else
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            static void AddWithResize(ref PooledArrayBuilder<T> builder, T item)
            {
                builder.Grow();
                builder.Add(item);
            }

            AddWithResize(ref this, item);
        }
    }

    /// <summary>
    /// Removes the last item in the current builder.
    /// </summary>
    public void RemoveLast()
    {
        int position = this.position;

        this.span[position - 1] = default!;

        this.position = position - 1;
    }

    /// <summary>
    /// Gets an <see cref="ImmutableArray{T}"/> instance with the values from the current builder.
    /// </summary>
    /// <returns>An <see cref="ImmutableArray{T}"/> instance with the values from the current builder.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImmutableArray<T> ToImmutableArray()
    {
        T[] array = this.span.Slice(0, this.position).ToArray();

        return Unsafe.As<T[], ImmutableArray<T>>(ref array);
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        T[]? arrayToReturnToPool = this.arrayToReturnToPool;

        if (arrayToReturnToPool is not null)
        {
            this.arrayToReturnToPool = null;

            // Only clear the used range (needed to avoid rooting reference types)
            this.span.Slice(0, this.position).Clear();

            ArrayPool<T>.Shared.Return(arrayToReturnToPool);
        }
    }

    /// <summary>
    /// Expands the underlying pooled buffer for this builder instance.
    /// </summary>
    private void Grow()
    {
        const int ArrayMaxLength = 0x7FFFFFC7;

        int nextCapacity = this.span.Length != 0 ? this.span.Length * 2 : 4;

        if ((uint)nextCapacity > ArrayMaxLength)
        {
            nextCapacity = Math.Max(Math.Max(this.span.Length + 1, ArrayMaxLength), this.span.Length);
        }

        T[] array = ArrayPool<T>.Shared.Rent(nextCapacity);

        this.span.Slice(0, this.position).CopyTo(array);
        this.span.Slice(0, this.position).Clear();

        T[]? arrayToReturnToPool = this.arrayToReturnToPool;

        this.span = this.arrayToReturnToPool = array;

        if (arrayToReturnToPool is not null)
        {
            ArrayPool<T>.Shared.Return(arrayToReturnToPool);
        }
    }
}
