using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Jobs;
using System.Linq;
using SLZ.CustomStaticBatching;
using Unity.Collections;

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
			combiner.vertexFormatCompression[1] = SBCombineMeshList.VtxFormats.SNorm8;
			combiner.vertexFormatCompression[2] = SBCombineMeshList.VtxFormats.SNorm8;
			combiner.GenerateStaticBatches(meshRenderers.ToArray());
		}

		[MenuItem("Tools/Create Hilbert Curve")]
		public static void TestHilbert()
		{
			NativeArray<Vector3> points = new NativeArray<Vector3>(16 * 16 * 16, Allocator.TempJob);

			for (int z = 0; z < 16; z++) 
			{
				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						points[256 * z + 16 * y + x] = new Vector3(x, y, z);
					}
				}
			}
			/*
			NativeArray<ulong> sortIdxes = HilbertIndex.GetHilbertIndices(points, new Bounds(new Vector3(8, 8, 8), new Vector3(16, 16, 16)), new Vector3(1,1.0f,1), Allocator.TempJob);
			//NativeSortExtension.Sort<SortIdx>(sortIdxes);
			string message = "";
			for (int i = 0; i < 16; i++)
			{
				message += sortIdxes[i].ToString() + "\n";
			}
			Debug.Log(message);
			GameObject lineObj = new GameObject();
			LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
			lineRenderer.positionCount = points.Length;
			lineRenderer.widthMultiplier = 0.02f;
			lineRenderer.alignment = LineAlignment.View;
			lineRenderer.useWorldSpace = false;
			for (int i = 0; i < points.Length; i++)
			{
				lineRenderer.SetPosition(i, points[sortIdxes[i].arrayIdx]);
			}
			sortIdxes.Dispose();
			*/
			points.Dispose();
			
		}
	}
}
