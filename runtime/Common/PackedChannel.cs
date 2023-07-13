using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

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
			Invalid = 0,
			UNorm8 = 1,
			SNorm8 = 2,
			Float16 = 3,
			Float32 = 4,
		}

		/// <summary>
		/// Maps each VtxFormat enum to the number of bytes in that format
		/// </summary>
		public static ReadOnlySpan<byte> VtxFmtToBytes => new byte[5] { 0, 1, 1, 2, 4 };

		/// <summary>
		/// Maps a VtxFormats enum value to a VertexAttributeFormat enum value
		/// </summary>
		public static ReadOnlySpan<VertexAttributeFormat> ToUnityFormatLUT => new VertexAttributeFormat[5] {
			VertexAttributeFormat.Float32,
			VertexAttributeFormat.UNorm8,
			VertexAttributeFormat.SNorm8,
			VertexAttributeFormat.Float16,
			VertexAttributeFormat.Float32,
		};


	}
}
