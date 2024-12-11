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
			if (!SBSettingsSO.GlobalSettings.executeInPlayMode && !BuildPipeline.isBuildingPlayer)
			{
				Debug.Log("Skipping custom static batching, play mode execution disabled in settings");
				return;
			}
			Debug.Log("Running static batching for " + scene.name);
			GameObject[] selection = scene.GetRootGameObjects();
			List<MeshRenderer> renderers = new List<MeshRenderer>();

			List<LODGroup> sceneLodGroups = new List<LODGroup>();
			for (int i = 0; i < selection.Length; i++)
			{
				MeshRenderer[] mf2 = selection[i].GetComponentsInChildren<MeshRenderer>();
				renderers.AddRange(mf2);
				LODGroup[] l2 = selection[i].GetComponentsInChildren<LODGroup>();
				sceneLodGroups.AddRange(l2);
			}

			// Get a map from every static mesh renderer in a LOD group to an info struct containing the parent LOD group and LOD level
			Dictionary<MeshRenderer, LODInfo> lodMap = new Dictionary<MeshRenderer, LODInfo>();
			int numLodGroups = sceneLodGroups.Count;
			for (int gIdx = 0; gIdx < numLodGroups; gIdx++)
			{
				LOD[] lods = sceneLodGroups[gIdx].GetLODs();
				int numLods = lods.Length;
				for (int lIdx = 0; lIdx < numLods; lIdx++)
				{
					Renderer[] lodRenderers = lods[lIdx].renderers;
					int numlodRenderers = lodRenderers.Length;
					for (int rIdx = 0; rIdx < numlodRenderers; rIdx++)
					{
						if (lodRenderers[rIdx] == null) continue;

						MeshRenderer mrLod = (MeshRenderer)lodRenderers[rIdx];
						bool isStatic = GameObjectUtility.AreStaticEditorFlagsSet(lodRenderers[rIdx].gameObject, StaticEditorFlags.BatchingStatic);
						if (isStatic && mrLod != null)
						{
							// if mrLod was already in the dictionary, that means it also belongs to a lower lod level. Hopefully no one is insane enough to reference the same renderer in different groups!
							lodMap.TryAdd(mrLod,
								new LODInfo { 
									lodRoot = sceneLodGroups[gIdx], 
									lodCenter = sceneLodGroups[gIdx].transform.TransformPoint(sceneLodGroups[gIdx].localReferencePoint),
									lodLevel = (uint)lIdx 
								}
								);
						}
					}
				}
			}

			int numRenderers = renderers.Count;
			int rendererIdx = 0;

			ComputeShader transferVtxCompute = SBCombineMeshEditor.GetTransferVtxComputeShader();
			SBCombineMeshList combiner = new SBCombineMeshList(transferVtxCompute);
			combiner.FetchGlobalProjectSettings();
			bool allow32bitIdxBatches = combiner.settings.allow32bitIdx;
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

				if (mr.sharedMaterials.Length > mf.sharedMesh.subMeshCount)
				{
					Debug.LogError($"SLZ Static Batching: renderer with more materials than submeshes! ({AnimationUtility.CalculateTransformPath(go.transform, null)})\n" +
						"Skipping static batching for this renderer as the extra material slots will draw using the submeshes from the next object in the combined mesh buffer rather than redrawing the original submesh");
					continue;
				}

				if (!allow32bitIdxBatches && mf.sharedMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
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

			if (renderers.Count < 2)
			{
				return;
			}

			RendererData[] sortedData = RendererSort.GetSortedData(renderers, meshFilters, lodMap);


			combiner.GenerateStaticBatches(sortedData);

			for (int i = 0; i < sortedData.Length; i++)
			{
				GameObject go = sortedData[i].rendererTransform.gameObject;
				GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.BatchingStatic);
			}
		}
	}
}
