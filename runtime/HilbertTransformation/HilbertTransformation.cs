using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using System;
using System.Runtime.CompilerServices;
using Unity.Jobs;

namespace SLZ.CustomStaticBatching
{
	public struct SortIdx : IComparable<SortIdx>
	{
		public UInt64 hilbertIdx;
		public int arrayIdx;

		public int CompareTo(SortIdx other) 
		{
			if (this.hilbertIdx > other.hilbertIdx) return 1;
			else if (this.hilbertIdx < other.hilbertIdx) return -1;
			else return 0;
		}
	}
	public static class HilbertIndex
	{

		[BurstCompile]
		struct HilbertIndexJob : IJobParallelFor
		{
			[WriteOnly]
			public NativeArray<UInt64> indices;
			[ReadOnly]
			public NativeArray<Vector3> positions;
			[ReadOnly]
			public double3 boundExtent;
			[ReadOnly]
			public double3 boundCenter;
			[ReadOnly]
			public double3 scale;

			public void Execute(int i)
			{
				double3 position = (double3)(float3)positions[i];

				position = (scale * (position - boundCenter) + boundExtent) / (2.0 * boundExtent);
				position = math.clamp(position, 0.0, 1.0);
				uint3 intPos = (uint3)((position * 2097152.0d)); // 2 ^ 21
				intPos = intPos & ((2 << 21) - 1);
				indices[i] = GetIndex(intPos.xyz);
			}
		}

		/// <summary>
		/// Given an unsigned 21-bit integer coordinate of a point within some bounding volume, return the point's distance along a 21-fold hilbert curve.
		/// Based on the AxestoTranspose function found in the appendix of John Skilling; Programming the Hilbert curve. AIP Conference Proceedings 21 April 2004; 707 (1): 381–387. https://doi.org/10.1063/1.1751381
		/// </summary>
		/// <param name="boundingCoord">3d unsigned integer coordinate of the point inside of a defined bounding volume.</param>
		/// <returns>The Hilbert index</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UInt64 GetIndex(uint3 boundingCoord)
		{
			int bits = 21;
			uint highestBit = 1U << (bits - 1);
			for (uint nextBit = highestBit; nextBit > 1; nextBit >>= 1)
			{
				uint allBitsSet = nextBit - 1;
				for (int axis = 0; axis < 3; axis++)
				{
					if ((boundingCoord[axis] & nextBit) != 0)
					{
						boundingCoord[0] ^= allBitsSet;
					}
					else
					{
						uint t = (boundingCoord[0] ^ boundingCoord[axis]) & allBitsSet;
						boundingCoord[0] ^= t;
						boundingCoord[axis] ^= t;
					}
				}
			}

			for (int axis = 1; axis < 3; axis++)
			{
				boundingCoord[axis] ^= boundingCoord[axis - 1];
			}
			uint t2 = 0;
			for (uint nextBit = highestBit; nextBit > 1; nextBit >>= 1)
			{
				if ((boundingCoord[3 - 1] & nextBit) != 0)
					t2 ^= nextBit - 1;
			}
			for (int axis = 0; axis < 3; axis++)
			{
				boundingCoord[axis] ^= t2;
			}
			
			return Interleave63Bits(boundingCoord.zyx);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static UInt64 Interleave63Bits(uint3 value)
		{
			Span<UInt64> longVal = stackalloc UInt64[3];
			for (int i = 0; i < 3; i++)
			{
				longVal[i] = value[i] & 0x1fffff; 
				longVal[i] = (longVal[i] | longVal[i] << 32) & 0x1f00000000ffff;
				longVal[i] = (longVal[i] | longVal[i] << 16) & 0x1f0000ff0000ff;
				longVal[i] = (longVal[i] | longVal[i] << 8) & 0x100f00f00f00f00f; 
				longVal[i] = (longVal[i] | longVal[i] << 4) & 0x10c30c30c30c30c3;
				longVal[i] = (longVal[i] | longVal[i] << 2) & 0x1249249249249249;
			}
			return longVal[0] | longVal[1] << 1 | longVal[2] << 2;
		}

		public static NativeArray<UInt64> GetHilbertIndices(NativeArray<Vector3> positions, Bounds bounds, Allocator allocator)
		{
			return GetHilbertIndices(positions, bounds, Vector3.one, allocator);
		}
		public static NativeArray<UInt64> GetHilbertIndices(NativeArray<Vector3> positions, Bounds bounds, Vector3 scale, Allocator allocator)
		{
			NativeArray<UInt64> indices = new NativeArray<UInt64>(positions.Length, allocator);
			HilbertIndexJob indexJob = new HilbertIndexJob()
			{
				indices = indices,
				positions = positions,
				boundExtent = (double3)(float3)bounds.extents,
				boundCenter = (double3)(float3)bounds.center,
				scale = (double3)math.saturate((float3)scale)
			};
			JobHandle indexJobHandle = indexJob.Schedule(positions.Length, 8);
			indexJobHandle.Complete();
			return indices;
		}
	}
}
