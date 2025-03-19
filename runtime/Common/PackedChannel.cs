using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using System.Data.SqlTypes;

namespace SLZ.CustomStaticBatching
{

	/// <summary>
	/// Bit-packed information about a single attribute channel in a vertex buffer. Maps to a single UInt, 
	/// Needs to be kept in sync with the Job and compute shader used for combining.
	/// </summary>
	[StructLayout(LayoutKind.Explicit, Size = 4)]
	public struct PackedChannel
	{
		[FieldOffset(0)]
		public UInt32 packedData;
		[FieldOffset(0)]
		public byte dimension;
		[FieldOffset(1)]
		public byte format;
		[FieldOffset(2)]
		public byte offset;
		[FieldOffset(3)]
		public byte stream;

		public override string ToString()
		{
			return string.Format("Dimension {0}, Format {1}, Offset {2}, Channel {3}, Packed: 0x{3}", (int)dimension, (int)format, (int)offset, packedData.ToString("X8"));
		}

		// Maximum number of vertex attributes in a mesh
		public const int NUM_VTX_CHANNELS = 12;

		public const int
			FORMAT_INVALID  = 0,
			FORMAT_UNORM8	= 1, FORMAT_UNORM8_BYTES	= 1,
			FORMAT_SNORM8	= 2, FORMAT_SNORM8_BYTES	= 1,
			FORMAT_UNORM16	= 3, FORMAT_UNORM16_BYTES	= 2,
			FORMAT_SNORM16	= 4, FORMAT_SNORM16_BYTES	= 2,
			FORMAT_HALF		= 5, FORMAT_HALF_BYTES		= 2,
			FORMAT_FLOAT	= 6, FORMAT_FLOAT_BYTES		= 4
			;

		/// <summary>
		/// Supported vertex formats. This is hard-coded into the compute shader used for combining, so don't just go adding formats to this list!
		/// Only commonly used floating point formats are here. There's no logical way to choose a format for the combined mesh's channels if the 
		/// channel is integer on one mesh and floating point on another. Which format do you choose? Cast int to float or treat the bytes of the 
		/// int as a float? You can losslessly store the bytes of an int32 in a float, but it'll get garbled if the output channel is compressed
		/// to half. Also what if the int is less than 32 bits? You can't put a short or char into a half or unorm, as those will get converted to
		/// float by the GPU. I'm not supporting 16 bit normalized formats to cut down on shader complexity. Just use half precision instead.
		/// These need to be in order of increasing precision such that each format can be safely contained in the next format.
		/// </summary>
		[Serializable]
		public enum VtxFormats : byte
		{
			[UnityEngine.HideInInspector]
			Invalid		= FORMAT_INVALID,

			UNorm8		= FORMAT_UNORM8,
			SNorm8		= FORMAT_SNORM8,
			UNorm16		= FORMAT_UNORM16,
			SNorm16		= FORMAT_SNORM16,
			Float16		= FORMAT_HALF,
			Float32		= FORMAT_FLOAT,
		}



		public const int FORMAT_COUNT = 7;
		public const int MIN_FORMAT = 1;
		public const int MAX_FORMAT = 6;

		/// <summary>
		/// Maps each VtxFormat enum to the number of bytes in that format
		/// </summary>
		public static ReadOnlySpan<byte> VtxFmtToBytes => new byte[FORMAT_COUNT] 
		{ 
			0, // invalid
			FORMAT_UNORM8_BYTES,
			FORMAT_SNORM8_BYTES,
			FORMAT_UNORM16_BYTES,
			FORMAT_SNORM16_BYTES,
			FORMAT_HALF_BYTES,
			FORMAT_FLOAT_BYTES
		};

		/// <summary>
		/// Map of each VtxFormat enum to the number of bytes in that format as a fixed-size list
		/// </summary>
		/// <returns>Map of each VtxFormat enum to the number of bytes in that format as a fixed-size list</returns>
		public static FixedList32Bytes<ushort> VtxFmtToBytesFixed()
		{
			return new FixedList32Bytes<ushort>
			{
				0, // invalid
				FORMAT_UNORM8_BYTES,
				FORMAT_SNORM8_BYTES,
				FORMAT_UNORM16_BYTES,
				FORMAT_SNORM16_BYTES,
				FORMAT_HALF_BYTES,
				FORMAT_FLOAT_BYTES
			};
		}
		/// <summary>
		/// Minimum dimension of each vertex format. Vertex structures require 4 byte alignment
		/// </summary>
		public static ReadOnlySpan<byte> VtxFmtMinDim => new byte[FORMAT_COUNT] 
		{ 
			0, // invalid
			4, // FORMAT_UNORM8	
			4, // FORMAT_SNORM8	
			2, // FORMAT_UNORM16	
			2, // FORMAT_SNORM16	
			2, // FORMAT_HALF		
			1  // FORMAT_FLOAT	
		};

		/// <summary>
		/// Maps a VtxFormats enum value to a VertexAttributeFormat enum value
		/// </summary>
		public static ReadOnlySpan<VertexAttributeFormat> ToUnityFormatLUT => new VertexAttributeFormat[FORMAT_COUNT] {
			VertexAttributeFormat.Float32,
			VertexAttributeFormat.UNorm8,
			VertexAttributeFormat.SNorm8,
			VertexAttributeFormat.UNorm16,
			VertexAttributeFormat.SNorm16,
			VertexAttributeFormat.Float16,
			VertexAttributeFormat.Float32,
		};

		public static ReadOnlySpan<byte> FromUnityFormatLUT => new byte[12] {
			(byte)VtxFormats.Float32,	//Float32,
			(byte)VtxFormats.Float16,	//Float16,
			(byte)VtxFormats.UNorm8,	//UNorm8,
			(byte)VtxFormats.SNorm8,	//SNorm8,
			(byte)VtxFormats.UNorm16,	//UNorm16,
			(byte)VtxFormats.SNorm16,	//SNorm16,
			(byte)VtxFormats.Invalid,	//UInt8,
			(byte)VtxFormats.Invalid,	//SInt8,
			(byte)VtxFormats.Invalid,	//UInt16,
			(byte)VtxFormats.Invalid,	//SInt16,
			(byte)VtxFormats.Invalid,	//UInt32,
			(byte)VtxFormats.Invalid	//SInt32
		};
	}
}
