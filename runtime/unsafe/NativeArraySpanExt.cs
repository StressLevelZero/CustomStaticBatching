using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	public static class CSBNativeArraySpanExt
	{
		public unsafe static void Copy<T>(Span<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) where T : unmanaged
		{
			fixed (T* pData = &src.GetPinnableReference())
			{
				//AtomicSafetyHandle.CheckWriteAndThrow(dst.m_Safety);
				UnsafeUtility.MemCpy((byte*)dst.GetUnsafePtr<T>() + dstIndex * UnsafeUtility.SizeOf<T>(), (byte*)(void*)pData + srcIndex * UnsafeUtility.SizeOf<T>(), length * UnsafeUtility.SizeOf<T>());
			}
		}

		public unsafe static void Copy<T>(ReadOnlySpan<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) where T : unmanaged
		{
			fixed (T* pData = &src.GetPinnableReference())
			{
				//AtomicSafetyHandle.CheckWriteAndThrow(dst.m_Safety);
				UnsafeUtility.MemCpy((byte*)dst.GetUnsafePtr<T>() + dstIndex * UnsafeUtility.SizeOf<T>(), (byte*)(void*)pData + srcIndex * UnsafeUtility.SizeOf<T>(), length * UnsafeUtility.SizeOf<T>());
			}
		}

        public unsafe static void Copy<T>(NativeArray<T> src, int srcIndex, Span<T> dst, int dstIndex, int length) where T : unmanaged
        {
            fixed (T* pData = &dst.GetPinnableReference())
            {
                //AtomicSafetyHandle.CheckWriteAndThrow(dst.m_Safety);
                UnsafeUtility.MemCpy((byte*)(void*)pData + dstIndex * UnsafeUtility.SizeOf<T>(), (byte*)src.GetUnsafePtr<T>() + srcIndex * UnsafeUtility.SizeOf<T>(), length * UnsafeUtility.SizeOf<T>());
            }
        }
    }
}