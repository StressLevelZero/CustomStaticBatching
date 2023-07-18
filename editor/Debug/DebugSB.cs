#if DEBUG_CUSTOM_STATIC_BATCHING
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
			List<MeshRenderer> renderers = new List<MeshRenderer>();
			for (int i =0; i < selection.Length; i++)
			{
				MeshRenderer[] mf2 = selection[i].GetComponentsInChildren<MeshRenderer>();
				renderers.AddRange(mf2);
			}
			int numRenderers = renderers.Count;
			int rendererIdx = 0;

			List<MeshFilter> meshFilters = new List<MeshFilter>(numRenderers);
			for (int i = 0; i < numRenderers; i++)
			{
				MeshRenderer mr = renderers[i];
				GameObject go = mr.gameObject;
				if (!GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.BatchingStatic))
				{
					continue;
				}

				MeshFilter mf = go.GetComponent<MeshFilter>();

				if (mf == null || mf.sharedMesh == null)
				{
					continue;
				}
				

				if (mf.sharedMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
				{
					continue;
				}

				if (mr.sharedMaterials.Length == 0 || mr.sharedMaterials[0] == null)
				{
					continue;
				}

				renderers[rendererIdx] = mr;
				meshFilters.Add(mf);
				rendererIdx++;
			}
			if (numRenderers != rendererIdx) renderers.RemoveRange(rendererIdx, numRenderers - rendererIdx);
			renderers.TrimExcess();
			meshFilters.TrimExcess();

			RendererData[] sortedData = RendererSort.GetSortedData(renderers, meshFilters);

			ComputeShader transferVtxCompute = SBCombineMeshEditor.GetTransferVtxComputeShader();
			SBCombineMeshList combiner = new SBCombineMeshList(transferVtxCompute);
			combiner.FetchGlobalProjectSettings();
			combiner.GenerateStaticBatches(sortedData);
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
#endif