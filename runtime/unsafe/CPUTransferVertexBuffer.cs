using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace SLZ.CustomStaticBatching
{
	public unsafe class CPUTransferVertexBuffer
	{
		[BurstCompile]
		
		public struct TransferVtxBuffer : IJobParallelFor
		{
			[NativeDisableUnsafePtrRestriction]
			[ReadOnly]
			NativeArray<byte> vertIn;
			[NativeDisableUnsafePtrRestriction]
			[NativeDisableParallelForRestriction]
			[WriteOnly]
			NativeArray<byte> vertOut;

			[ReadOnly]
			float4x4 Object2World;
			[ReadOnly]
			float4x4 World2Object;
			[ReadOnly]
			float4 lightmapScaleOffset;
			[ReadOnly]
			float4 dynLightmapScaleOffset;
			[ReadOnly]
			int4 offset_strideIn_TanSign;
			[ReadOnly]
			NativeArray<uint> inPackedChannelInfo;

			[ReadOnly]
			int4 strideOut;
			[ReadOnly]
			NativeArray<uint> outPackedChannelInfo;
			[ReadOnly]
			FixedList32Bytes<uint> formatToBytes;

			public void Execute(int i)
			{

			}
		

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		uint GetDimension(uint packedInfo)
		{
			return (packedInfo & 0x00000ffu);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		uint GetFormat(uint packedInfo)
		{
			return (packedInfo >> 8) & 0x00000ffu;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		uint GetOffset(uint packedInfo)
		{
			return (packedInfo >> 16) & 0x000000ffu;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		uint GetStream(uint packedInfo)
		{
			return (packedInfo >> 24);
		}

		// UNorm to Float -------------------------------------------------------------

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static float4 ConvertUNorm4ToFloat4(uint packedValue)
		{
			uint4 unpackedVal = new uint4(0x000000FF & packedValue, 0x000000FF & (packedValue >> 8), 0x000000FF & (packedValue >> 16), packedValue >> 24);
			float4 floatVal = (1.0f / 255.0f) * new float4(unpackedVal);
			return floatVal;
		}

		// Float To UNorm -------------------------------------------------------------

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint ConvertFloat4ToUNorm4(float4 value)
		{
			uint4 int3Val = new uint4(round(255.0f * saturate(value)));
			//int3Val = int3Val & 0x000000FFu;
			uint intVal = int3Val.x | (int3Val.y << 8) | (int3Val.z << 16) | (int3Val.w << 24);
			return intVal;
		}


		// SNorm to Float -------------------------------------------------------------

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static float4 ConvertSNorm4ToFloat4(uint packedValue)
		{
			uint4 unpackedVal = new uint4(packedValue << 24, 0xFF000000 & (packedValue << 16), 0xFF000000 & (packedValue << 8), 0xFF000000 & packedValue);
			int4 unpackedSVal = asint(unpackedVal) / 0x1000000;
			float4 floatVal = max((1.0f / 127.0f) * new float4(unpackedSVal), -1.0f);
			return floatVal;
		}

		// Float To SNorm -------------------------------------------------------------
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint ConvertFloat4ToSNorm4(float4 value)
		{
			int4 intVal = new int4(round(clamp(value, -1.0f, 1.0f) * 127.0f)) * 0x1000000; // multiply by 2^24 to shift the non-sign bits 24 bits to the right, so we have an 8 bit signed int at the end of the 32-bit int
			uint4 uintVal = asuint(intVal);
			uint composite = uintVal.x >> 24 | (uintVal.y >> 16) | (uintVal.z >> 8) | (uintVal.w);
			return composite;
		}

		// Half to Float --------------------------------------------------------------

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static float4 ConvertHalf4toFloat4(uint2 packedValue)
		{
			uint4 unpackedInt = new uint4(packedValue.x, packedValue.x >> 16, packedValue.y, packedValue.y >> 16);
			return f16tof32(unpackedInt);
		}

		// Float to Half --------------------------------------------------------------
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint2 ConvertFloat4ToHalf4(float4 value)
		{
			uint4 halfInt = f32tof16(value);
			return new uint2(halfInt.x | (halfInt.y << 16), halfInt.z | (halfInt.w << 16));
		}
		// Write a float, int, half2, short2, unorm4, or snorm4 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void Write4Bytes(uint value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			*ptr = value;
		}

		// write a float2, int2, half4 or short4 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void Write8Bytes(uint2 value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
		}

		// write a float3 or int3 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void Write12Bytes(uint3 value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
			ptr[2] = value.z;
		}

		// write a float4 or int4 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void Write16Bytes(uint4 value, uint adr,  ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
			ptr[2] = value.z;
			ptr[3] = value.w;
		}

		void WriteValue(ref NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
		{
			uint byteCount = formatToBytes[asint(format)];
			uint byteCase = byteCount * dimension;
			switch (byteCase)
			{
				case 4:
					Write4Bytes(value.x, adr, ref buffer);
					break;
				case 8:
					Write8Bytes(value.xy, adr, ref buffer);
					break;
				case 12:
					Write12Bytes(value.xyz, adr, ref buffer);
					break;
				case 16:
					Write16Bytes(value, adr, ref buffer);
					break;
			}
		}
		
		void WriteValueVtx(ref NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
		{
			uint byteCount = formatToBytes[asint(format)];
			uint byteCase = byteCount * dimension;
			switch (byteCase)
			{
				case 8:
					Write8Bytes(value.xy, adr, ref buffer);
					break;
				case 12:
					Write12Bytes(value.xyz, adr, ref buffer);
					break;
				default:
					Write12Bytes(uint3(1, 1, 1), adr, ref buffer);
					break;
			}
		}
		
		void WriteValueNorm(ref NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
		{
			uint byteCount = formatToBytes[asint(format)];
			uint byteCase = byteCount * dimension;
			switch (byteCase)
			{
				case 4:
					Write4Bytes(value.x, adr, ref buffer);
					break;
				case 8:
					Write8Bytes(value.xy, adr, ref buffer);
					break;
				case 12:
					Write12Bytes(value.xyz, adr, ref buffer);
					break;
				default:
					Write8Bytes(new uint2(0xFFFFFFFF, 0xFFFFFFFF), adr, ref buffer);
					break;
			}
		}
		
		void WriteValueTanColor(ref NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
		{
			uint byteCount = formatToBytes[asint(format)];
			uint byteCase = byteCount * dimension;
			switch (byteCase)
			{
				case 4:
					Write4Bytes(value.x, adr, ref buffer);
					break;
				case 8:
					Write8Bytes(value.xy, adr, ref buffer);
					break;
				case 16:
					Write16Bytes(value, adr, ref buffer);
					break;
			}
		}
	}

	}
}