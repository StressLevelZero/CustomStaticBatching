using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	public unsafe static class SpanSortExt
	{
		public static void Sort<T>(ref Span<T> span) where T : unmanaged, IComparable<T>
		{
			fixed (T* pData = &span.GetPinnableReference())
			{
				NativeArray<T> nativeCast = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((void*)pData, span.Length, Allocator.None);
				nativeCast.Sort();
			}
		}
	}
}