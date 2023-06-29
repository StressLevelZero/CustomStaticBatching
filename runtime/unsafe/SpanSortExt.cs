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
		public static void InsertSort(ref Span<uint> span, int length) 
		{
			fixed (uint* pData = &span.GetPinnableReference())
			{
				for (int i = 1; i < length; i++) 
				{ 
					uint element = pData[i];
					int j = i - 1;
					while (j >= 0 && pData[j] > element) 
					{
						pData[j+1] = pData[j];
						j--;
					}
					pData[j + 1] = element;
				}
			}
		}
	}
}