using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{

	/// <summary>
	/// Bit-packed information about a single channel in a vertex buffer. Maps to a single UInt, needs to be kept in sync with the compute shader
	/// used for transfering and converting vertex buffers of each mesh into the combined mesh.
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
		public byte unused;

		public override string ToString()
		{
			return string.Format("Dimension {0}, Format {1}, Offset {2}, Packed: 0x{3}", (int)dimension, (int)format, (int)offset, packedData.ToString("X8"));
		}
	}
}
