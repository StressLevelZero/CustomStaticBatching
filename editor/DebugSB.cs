using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using SLZ.CustomStaticBatching;

namespace SLZ.CustomStaticBatching.Editor
{
	public class DebugSB
	{
		[MenuItem("Tools/Print SB Debug Message")]
		public static void PrintDebugMessage()
		{
			GameObject[] selection = Selection.gameObjects;
			List<MeshFilter> filters = new List<MeshFilter>();
			for (int i =0; i < selection.Length; i++)
			{
				MeshFilter[] mf2 = selection[i].GetComponentsInChildren<MeshFilter>();
				filters.AddRange(mf2);
			}
			List<RendererData> meshRenderers = new List<RendererData>(filters.Count);
			for (int i = 0; i < filters.Count; i++) 
			{ 
				MeshFilter mf = filters[i];
				MeshRenderer mr = filters[i].GetComponent<MeshRenderer>();
				if (mf != null && mr != null && mf.sharedMesh != null) 
				{ 
					
					int numSubMeshes = mf.sharedMesh.subMeshCount;
					uint idxCount = 0;
					for (int sm = 0; sm < numSubMeshes; sm++) 
					{
						idxCount += mf.sharedMesh.GetIndexCount(sm);
					}
					RendererData rd = new RendererData() { mesh = mf.sharedMesh, indexCount = idxCount, meshFilter = mf, meshRenderer = mr, rendererTransform = filters[i].transform };
					meshRenderers.Add(rd);
				}
			}
			ComputeShader transferVtxCompute = SBCombineMeshEditor.GetTransferVtxComputeShader();
			SBCombineMeshList combiner = new SBCombineMeshList(transferVtxCompute);
			combiner.SetCompressionFromProjectSettings();
			combiner.GenerateStaticBatches(meshRenderers.ToArray());
		}
	}




}
