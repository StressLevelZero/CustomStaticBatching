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
using UnityEngine.UIElements;
using System;


namespace SLZ.CustomStaticBatching
{
	public unsafe class TransferVtxDummyCompileGeneric
	{
		static IntPtr DummyCompileGeneric(NativeArray<byte> dummy)
		{
			IntPtr a = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<byte>(dummy);
			throw new InvalidOperationException("DummyCompileGeneric is not a real method, exists solely to force compilation of a generic method");
			return a;
		}
	}

	[BurstCompile]
	
	public unsafe struct TransferVtxBuffer : IJobParallelFor
	{
		[NativeDisableUnsafePtrRestriction]
		[ReadOnly]
		public NativeArray<byte> vertIn;

		[NativeDisableUnsafePtrRestriction]
		[ReadOnly]
		public NativeArray<byte> vertIn2;

		[NativeDisableUnsafePtrRestriction]
		[NativeDisableParallelForRestriction]
		[WriteOnly]
		public NativeArray<byte> vertOut;

		[NativeDisableUnsafePtrRestriction]
		[NativeDisableParallelForRestriction]
		[WriteOnly]
		public NativeArray<byte> vertOut2;

		[ReadOnly]
		public float4x4 ObjectToWorld;
		[ReadOnly]
		public float4x4 WorldToObject;
		[ReadOnly]
		public float4 lightmapScaleOffset;
		[ReadOnly]
		public float4 dynLightmapScaleOffset;
		[ReadOnly]
		public int4 offset_strideIn_offset2_strideIn2; // Sign of the tangent is stored in the sign of strideIn2. strideIn2 should be set to 1/-1 if there is no second input buffer.
		[ReadOnly]
		public NativeArray<PackedChannel> inPackedChannelInfo;

		[ReadOnly]
		public int4 strideOut;
		[ReadOnly]
		public NativeArray<PackedChannel> outPackedChannelInfo;
		[ReadOnly]
		public FixedList32Bytes<uint> formatToBytes;
		[ReadOnly]
		public bool normalizeNormTan;

		public void Execute(int i)
		{
			uint id = (uint)i;
			uint vertInAdr = (id * asuint(offset_strideIn_offset2_strideIn2.y));
			uint vertOutAdr = (id * asuint(strideOut.x) + asuint(offset_strideIn_offset2_strideIn2.x));
			uint vertIn2Adr = (id * asuint(abs(offset_strideIn_offset2_strideIn2.w)));
			uint vertOut2Adr = (id * asuint(strideOut.y) + asuint(offset_strideIn_offset2_strideIn2.z));

			WritePositionChannel(inPackedChannelInfo[0].packedData, outPackedChannelInfo[0].packedData, vertInAdr, vertOutAdr);
			WriteNormalChannel(inPackedChannelInfo[1].packedData, outPackedChannelInfo[1].packedData, vertInAdr, vertOutAdr, vertIn2Adr, vertOut2Adr);
			WriteTangentChannel(inPackedChannelInfo[2].packedData, outPackedChannelInfo[2].packedData, vertInAdr, vertOutAdr, vertIn2Adr, vertOut2Adr);
			WriteColorChannel(inPackedChannelInfo[3].packedData, outPackedChannelInfo[3].packedData, vertInAdr, vertOutAdr, vertIn2Adr, vertOut2Adr);

			for (int r = 4; r < 12; r++)
			{
				WriteUVChannel(inPackedChannelInfo[r].packedData, outPackedChannelInfo[r].packedData, vertInAdr, vertOutAdr, vertIn2Adr, vertOut2Adr, (r == 5), (r == 6));
			}
		}
	

		const int
		FORMAT_FLOAT = 4, // 0 - float
		FORMAT_HALF = 3, // 1 - half
		FORMAT_SNORM = 2, // 2 - snorm
		FORMAT_UNORM = 1; // 3 - unorm

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint GetDimension(uint packedInfo)
		{
			return (packedInfo & 0x00000ffu);
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint GetFormat(uint packedInfo)
		{
			return (packedInfo >> 8) & 0x00000ffu;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint GetOffset(uint packedInfo)
		{
			return (packedInfo >> 16) & 0x000000ffu;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint GetStream(uint packedInfo)
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
		static void Write4Bytes(uint value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			*ptr = value;
		}

		// write a float2, int2, half4 or short4 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void Write8Bytes(uint2 value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
		}

		// write a float3 or int3 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void Write12Bytes(uint3 value, uint adr, ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
			ptr[2] = value.z;
		}

		// write a float4 or int4 to the buffer
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void Write16Bytes(uint4 value, uint adr,  ref NativeArray<byte> buffer)
		{
			uint* ptr = (uint*)((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer) + adr);
			ptr[0] = value.x;
			ptr[1] = value.y;
			ptr[2] = value.z;
			ptr[3] = value.w;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void WriteValue(NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void WriteValueVtx(NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void WriteValueNorm(NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void WriteValueTanColor(NativeArray<byte> buffer, uint adr, uint format, uint dimension, uint4 value)
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static float4 ConvertRawToFloat(uint4 value, uint format)
		{
			format = clamp(format, FORMAT_UNORM, FORMAT_FLOAT);
			switch (format)
			{
				case FORMAT_UNORM: // unorm
					return ConvertUNorm4ToFloat4(value.x);
					break;
				case FORMAT_SNORM: // snorm
					return ConvertSNorm4ToFloat4(value.x);
					break;
				case FORMAT_HALF: // half
					return ConvertHalf4toFloat4(value.xy);
					break;
				case FORMAT_FLOAT: // float
					return asfloat(value);
					break;
				default:
					return float4(0, 0, 0, 0);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static uint4 ConvertFloatToRaw(float4 value, uint format)
		{
			format = clamp(format, FORMAT_UNORM, FORMAT_FLOAT);
			switch (format)
			{
				case FORMAT_FLOAT: // float
					return asuint(value);
					break;
				case FORMAT_HALF: // half
					return uint4(ConvertFloat4ToHalf4(value), 0, 0);
					break;
				case FORMAT_SNORM: // snorm
					return uint4(ConvertFloat4ToSNorm4(value), 0, 0, 0);
					break;
				case FORMAT_UNORM: // unorm
					return uint4(ConvertFloat4ToUNorm4(value), 0, 0, 0);
					break;
				default:
					return uint4(0, 0, 0, 0);
			}
		}


		static uint4 Load4(NativeArray<byte> array, uint address)
		{
			byte* baseAddress = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(array);
			int alignedAddress = (int)(address >> 2); // divide by 4 to get the 4 byte aligned address
			byte* ptr = baseAddress + (alignedAddress << 2);


			int arrayLength = array.Length >> 2;
			int remainingSize = arrayLength - alignedAddress;
			
			if (remainingSize >= 4)
			{
				uint4* uPtr = (uint4*)ptr;
				uint4 outp = *uPtr;
				return outp;
			}

			switch (remainingSize)
			{
				case 3:
					uint3* uPtr3 = (uint3*)ptr;
					uint4 outp3 = new uint4(uPtr3->x, uPtr3->y, uPtr3->z, 0u);
					return outp3;
				case 2:
					uint2* uPtr2 = (uint2*)ptr;
					uint4 outp2 = new uint4(uPtr2->x, uPtr2->y, 0u, 0u);
					return outp2;
				case 1:
					uint* uPtr1 = (uint*)ptr;
					uint4 outp1 = new uint4(*uPtr1, 0u, 0u, 0u);
					return outp1;
				default:
					return new uint4(1, 2, 3, 4);
			}
		}


		void WritePositionChannel(uint inPackedInfo, uint outPackedInfo, uint vertInAdr, uint vertOutAdr)
		{
			uint address = vertInAdr; // The vertex position should always be at the start of the struct, don't bother adding the offset
			uint4 rawData = Load4(vertIn, address);
			uint inFmt = GetFormat(inPackedInfo);
			float4 position = ConvertRawToFloat(rawData, inFmt);
			position.w = 1;
			position = mul(ObjectToWorld, position);
			rawData = asuint(position);
			uint outAddress = vertOutAdr; // The vertex position should always be at the start of the struct, don't bother adding the offset
			WriteValueVtx(vertOut, outAddress, FORMAT_FLOAT, 3, rawData);
		}

		void WriteNormalChannel(uint inPackedInfo, uint outPackedInfo, uint vertInAdr, uint vertOutAdr, uint vertInAdr2, uint vertOutAdr2)
		{

			if (GetDimension(inPackedInfo) > 0u) // output mesh guaranteed to have the channel if an input mesh has it
			{
				bool altIn = false;// GetStream(inPackedInfo) > 0;
				uint address = (altIn ? vertInAdr2 : vertInAdr) + GetOffset(inPackedInfo);
				uint4 rawData = Load4(altIn ? vertIn2 : vertIn, address);
				uint inFmt = GetFormat(inPackedInfo);

				float3 normal = ConvertRawToFloat(rawData, inFmt).xyz; // normal is always 3 components, if not wtf are you doing?

				// Transform to world space using the appropriate method for normals, but don't bother normalizing the normal. The shader is going to do that anyways, and this ensures the rendering behavior of the combined mesh matches the indiviudal mesh 
				normal = mul(normal, (float3x3)WorldToObject);

				uint outFmt = GetFormat(outPackedInfo);
				if (outFmt == FORMAT_SNORM || normalizeNormTan)
				{
					normal = normalize(normal);
				}
				outFmt = clamp(outFmt, FORMAT_SNORM, FORMAT_FLOAT);
				rawData = ConvertFloatToRaw(float4(normal.xyz, 0), outFmt);
				bool altOut = GetStream(outPackedInfo) > 0;
				uint outAddress = (altOut ? vertOutAdr2 : vertOutAdr) + GetOffset(outPackedInfo);
				uint outDimension = GetDimension(outPackedInfo);
				// uint inByteEnum = clamp(GetByteCountEnum(inPackedInfo), BYTECOUNT_3, BYTECOUNT_12); //always 3 components, can be snorm to float
				
				WriteValueNorm(altOut ? vertOut2 : vertOut, outAddress, outFmt, outDimension, rawData);
			}
		}

		void WriteTangentChannel(uint inPackedInfo, uint outPackedInfo, uint vertInAdr, uint vertOutAdr, uint vertInAdr2, uint vertOutAdr2)
		{
			if (GetDimension(inPackedInfo) > 0u) // output mesh guaranteed to have the channel if an input mesh has it
			{
				bool altIn = GetStream(inPackedInfo) > 0;
				uint address = (altIn ? vertInAdr2 : vertInAdr) + GetOffset(inPackedInfo);
				uint4 rawData = Load4(altIn ? vertIn2 : vertIn, address);
				uint inFmt = GetFormat(inPackedInfo);

				float4 tangent = ConvertRawToFloat(rawData, inFmt); // tangent is always 4 components, if not wtf are you doing?

				// Transform to world space, and flip the direction if the mesh is scaled negatively. 
				tangent.xyz = mul((float3x3)ObjectToWorld, tangent.xyz);
				tangent.w *= offset_strideIn_offset2_strideIn2.w < 0 ? -1 : 1; // sign of tanget stored in sign of strideIn2

				uint outFmt = GetFormat(outPackedInfo);
				outFmt = clamp(outFmt, FORMAT_SNORM, FORMAT_FLOAT);
				if (outFmt == FORMAT_SNORM || normalizeNormTan)
				{
					tangent.xyz = normalize(tangent.xyz);
				}
				rawData = ConvertFloatToRaw(tangent, outFmt);
				bool altOut = GetStream(outPackedInfo) > 0;
				uint outAddress = (altOut ? vertOutAdr2 : vertOutAdr) + GetOffset(outPackedInfo);
				//uint inByteEnum = clamp(GetByteCountEnum(inPackedInfo), BYTECOUNT_4, BYTECOUNT_16); //always 4 components, can be snorm to float
				WriteValueTanColor(altOut ? vertOut2 : vertOut, outAddress, outFmt, 4, rawData);
			}
		}

		void WriteColorChannel(uint inPackedInfo, uint outPackedInfo, uint vertInAdr, uint vertOutAdr, uint vertInAdr2, uint vertOutAdr2)
		{
			if ((outPackedInfo & 0x0000000fu) > 0u) // input mesh not guaranteed to have color even if the output does, so we have to initialize the color to 1,1,1,1 if the input is missing the attribute
			{
				float4 color;
				uint4 rawData;
				uint inDimension = GetDimension(inPackedInfo);
				if (inDimension > 0u)
				{
					bool altIn = GetStream(inPackedInfo) > 0;
					uint address = (altIn ? vertInAdr2 : vertInAdr) + GetOffset(inPackedInfo);
					rawData = Load4(altIn ? vertIn2 : vertIn, address);
					uint inFmt = GetFormat(inPackedInfo);
					color = ConvertRawToFloat(rawData, inFmt);
				}
				else
				{
					color = float4(1, 1, 1, 1);
				}

				uint outFmt = GetFormat(outPackedInfo);
				rawData = ConvertFloatToRaw(color, outFmt);
				bool altOut = GetStream(outPackedInfo) > 0;
				uint outAddress = (altOut ? vertOutAdr2 : vertOutAdr) + GetOffset(outPackedInfo);
				uint outDimension = GetDimension(outPackedInfo);
				//uint inByteEnum = GetByteCountEnum(inPackedInfo);
				WriteValueTanColor(altOut ? vertOut2 : vertOut, outAddress, outFmt, outDimension, rawData);
			}
		}

		void WriteUVChannel(uint inPackedInfo, uint outPackedInfo, uint vertInAdr, uint vertOutAdr, uint vertInAdr2, uint vertOutAdr2, bool lmScaleOffset, bool dynLmScaleOffset)
		{
			if (GetDimension(inPackedInfo) > 0u) // output mesh guaranteed to have the channel if an input mesh has it
			{
				bool altIn = GetStream(inPackedInfo) > 0;
				uint address = (altIn ? vertInAdr2 : vertInAdr) + GetOffset(inPackedInfo);
				uint4 rawData = Load4(altIn ? vertIn2 : vertIn, address);
				uint inFmt = GetFormat(inPackedInfo);

				float4 UV = ConvertRawToFloat(rawData, inFmt);
				if (lmScaleOffset)
					UV.xy = UV.xy * lightmapScaleOffset.xy + lightmapScaleOffset.zw;
				if (lmScaleOffset)
					UV.xy = UV.xy * dynLightmapScaleOffset.xy + dynLightmapScaleOffset.zw;
				uint outFmt = GetFormat(outPackedInfo);
				rawData = ConvertFloatToRaw(UV, outFmt);
				bool altOut = GetStream(outPackedInfo) > 0;
				uint outAddress = (altOut ? vertOutAdr2 : vertOutAdr) + GetOffset(outPackedInfo);
				uint inDimension = GetDimension(inPackedInfo);
				//uint inByteEnum = GetByteCountEnum(inPackedInfo);
				WriteValue(altOut ? vertOut2 : vertOut, outAddress, outFmt, inDimension, rawData);
			}
		}
	}
}