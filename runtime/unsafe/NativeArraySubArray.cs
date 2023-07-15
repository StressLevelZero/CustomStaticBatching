using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	public static unsafe class NativeArraySubArray
	{
		public static unsafe NativeArray<T2> GetSubArrayAlias<T, T2>(NativeArray<T> array, int start, int length)
			where T : unmanaged
			where T2 : unmanaged
		{
			void* dataPointer = ((byte*)array.GetUnsafePtr()) + start * sizeof(T);
			NativeArray<T2> outp = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T2>(dataPointer, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref outp, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
			return outp;
		}


	}
}
