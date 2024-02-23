using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SLZ.CustomStaticBatching
{
    public unsafe static class NativeArrayClear
    {
        unsafe public static void Clear<T>(ref NativeArray<T> array, long count, long start = 0) where T : struct
        {	
            UnsafeUtility.MemClear(
                (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(array) + start * UnsafeUtility.SizeOf<T>(),
                count * UnsafeUtility.SizeOf<T>()
                );
        }
    }
}
