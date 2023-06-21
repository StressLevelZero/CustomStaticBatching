using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace SLZ.SLZEditorTools
{
	public class DebugSB
	{
		[MenuItem("Tools/Print SB Debug Message")]
		public static void PrintDebugMessage()
		{
			GameObject[] selection = Selection.gameObjects;
			List<RendererData> meshRenderers = new List<RendererData>(selection.Length);
			for (int i = 0; i < selection.Length; i++) 
			{ 
				MeshFilter mf = selection[i].GetComponent<MeshFilter>();
				MeshRenderer mr = selection[i].GetComponent<MeshRenderer>();
				if (mf != null && mr != null) 
				{ 
					int numSubMeshes = mf.sharedMesh.subMeshCount;
					uint idxCount = 0;
					for (int sm = 0; sm < numSubMeshes; sm++) 
					{
						idxCount += mf.sharedMesh.GetIndexCount(sm);
					}
					RendererData rd = new RendererData() { mesh = mf.sharedMesh, indexCount = idxCount, meshFilter = mf, meshRenderer = mr};
					meshRenderers.Add(rd);
				}
			}
			SBCombineMeshList combiner = new SBCombineMeshList();
			combiner.CombineMeshes(meshRenderers.ToArray());
		}
	}
}
