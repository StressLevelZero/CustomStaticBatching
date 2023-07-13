using SLZ.CustomStaticBatching.Editor;
using SLZ.CustomStaticBatching;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	public class ReplaceStaticBatching : IProcessSceneWithReport
	{
		public int callbackOrder { get { return 0; } }

		public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
		{
			if (true)
			{
				Debug.Log("Running static batching");
				GameObject[] selection = scene.GetRootGameObjects();
				List<MeshRenderer> renderers = new List<MeshRenderer>();
				for (int i = 0; i < selection.Length; i++)
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

				for (int i = 0; i < sortedData.Length; i++)
				{
					GameObject go = sortedData[i].rendererTransform.gameObject;
					GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.BatchingStatic);
				}
			}
		}
	}
}
