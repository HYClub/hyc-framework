using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// blittable 二进制写入：目标 Span 顺序拷入，零分配零反射零 unsafe。
    /// </summary>
    public ref struct BlitWriter
    {
        private Span<byte> _span;
        private int _pos;

        private BlitWriter(Span<byte> destination)
        {
            _span = destination;
            _pos = 0;
        }

        public static BlitWriter Create(Span<byte> destination) => new BlitWriter(destination);

        /// <summary>T 的二进制字节数。</summary>
        public static int SizeOf<T>() where T : unmanaged => UnsafeUtility.SizeOf<T>();

        /// <summary>已写入字节数。</summary>
        public int WrittenBytes => _pos;

        /// <summary>
        /// 写 T 的二进制字节。经 <see cref="MemoryMarshal.Cast{TFrom,TTo}(Span{TFrom})"/> 取字节区上的 T 视图直写，
        /// 不对 Span 做 fixed（Unity 2022.3 编译器对 span-fixed 报 CS0193，框架内先例一律只 fix 托管数组）。
        /// </summary>
        public void Write<T>(in T value) where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>();
            var destination = MemoryMarshal.Cast<byte, T>(_span.Slice(_pos, size));
            destination[0] = value;
            _pos += size;
        }
    }

    /// <summary>
    /// blittable 二进制读取：与 <see cref="BlitWriter"/> 写入顺序对应，逐字段还原。
    /// </summary>
    public ref struct BlitReader
    {
        private ReadOnlySpan<byte> _span;
        private int _pos;

        private BlitReader(ReadOnlySpan<byte> source)
        {
            _span = source;
            _pos = 0;
        }

        public static BlitReader Create(ReadOnlySpan<byte> source) => new BlitReader(source);

        /// <summary>读 T：与 <see cref="BlitWriter.Write{T}(in T)"/> 同一套 Cast 视图，无 fixed 无 unsafe。</summary>
        public T Read<T>() where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>();
            var source = MemoryMarshal.Cast<byte, T>(_span.Slice(_pos, size));
            T value = source[0];
            _pos += size;
            return value;
        }
    }
}
