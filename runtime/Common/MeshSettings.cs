using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	[Serializable]
	public struct CombineRendererSettings
	{
		public PackedChannel.VtxFormats[] GetVertexFormats()
		{
			PackedChannel.VtxFormats[] outp = new PackedChannel.VtxFormats[serializedVtxFormats.Length];
			for (int i = 0; i < serializedVtxFormats.Length; i++)
			{
				outp[i] = (PackedChannel.VtxFormats)serializedVtxFormats[i];
			}
			return outp;
		}

		public bool normalizeNormalTangent;
		public bool roundVertexPositions;
		public double vertexRoundingSize;

		public byte[] serializedVtxFormats;

		public bool allow32bitIdx;
		public int maxCombined32Idx;
		public bool splitMultiMaterialMeshes;
		public bool[] altStream;

		public CombineRendererSettings(bool initialize)
		{
			normalizeNormalTangent = false;
			roundVertexPositions = false;
			vertexRoundingSize = 0.002;
			serializedVtxFormats = new byte[PackedChannel.NUM_VTX_CHANNELS];
			if (initialize)
			{
				for (int i = 0; i < PackedChannel.NUM_VTX_CHANNELS; i++)
				{
					serializedVtxFormats[i] = (byte)PackedChannel.VtxFormats.Float32;
				}
			}
			altStream = new bool[PackedChannel.NUM_VTX_CHANNELS];
			allow32bitIdx = true;
			splitMultiMaterialMeshes = false;
			maxCombined32Idx = 1 << 23;
		}
	}

	public interface ICSBIndexer
	{

	}
}
