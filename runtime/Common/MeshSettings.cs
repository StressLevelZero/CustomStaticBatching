using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	[Serializable]
	public struct CombineRendererSettings
	{
		public PackedChannel.VtxFormats[] vtxFormatCompression;
		public bool allow32bitIdx;
		public bool splitMultiMaterialMeshes;

		public CombineRendererSettings(bool initialize)
		{
			vtxFormatCompression = new PackedChannel.VtxFormats[PackedChannel.NUM_VTX_CHANNELS];
			if (initialize)
			{
				for (int i = 0; i < PackedChannel.NUM_VTX_CHANNELS; i++)
				{
					vtxFormatCompression[i] = PackedChannel.VtxFormats.Float32;
				}
			}
			allow32bitIdx = true;
			splitMultiMaterialMeshes = false;
		}
	}
}
