using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Unity.Mathematics;
using System.Text;
using Unity.VisualScripting.Antlr3.Runtime.Collections;


#if USING_NEWBLOOD_LIGHTING_INTERNALS
using NewBlood;
#endif

namespace SLZ.CustomStaticBatching.Editor
{
	public class GetDynLMMeshes
	{
		public static bool GetDynamicLightMapMeshes(RendererData[] rendererData, out List<Mesh> uvMeshes, out int[] rIdxToDynIdx, out Mesh.MeshDataArray dynUvMeshDataArray, out ScriptableObject scriptableLightingData)
		{
#if USING_NEWBLOOD_LIGHTING_INTERNALS
			int numRenderers = rendererData.Length;
			//List<Object> dynLMObjs = new List<Object>(numRenderers);
			int numDynamic = 0;
			for (int rIdx = 0; rIdx < numRenderers; rIdx++)
			{
				if (rendererData[rIdx].dynLightmapIdx != 0xFFFF)
				{
					numDynamic += 1;
				}
			}
			if (numDynamic == 0)
			{
				uvMeshes = null;
				rIdxToDynIdx = null;
				scriptableLightingData = null;
				dynUvMeshDataArray = (default);
				return false;
			}
			MeshRenderer[] dynLMObjs = new MeshRenderer[numDynamic];
			int[] dynIdxToRIdx = new int[numDynamic];
			
			for (int rIdx = 0, dIdx = 0; rIdx < numRenderers; rIdx++)
			{
				MeshRenderer mr = rendererData[rIdx].meshRenderer;
				if (mr.realtimeLightmapIndex != 0xFFFF)
				{
					dynLMObjs[dIdx] = mr;
					dynIdxToRIdx[dIdx] = rIdx;
					dIdx += 1;
					if (dIdx == numDynamic) break;
				}
			}

			// Extremely jank. I cannot use SceneObjectIdentifier.GetSceneObjectIdentifiersSlow to get a list of global object and prefab IDs for each
			// renderer. During scene build, we are working on a copy of the scene and thus the global ID's in the scene copy no longer match the ids
			// stored in the lighting data asset. However, no two renderer's dynamic lightmap scale and offset should be the same, so we can literally
			// use those as the unique identifiers (assuming you don't do something like make a script to copy the scale offset of LODs to occupy the
			// same space)

			Dictionary<float4, int> scaleOffsetToIndex = new Dictionary<float4, int>(numDynamic);
			
			for (int dIdx = 0; dIdx < numDynamic; dIdx++)
			{

				float4 scaleOffset = dynLMObjs[dIdx].realtimeLightmapScaleOffset + new Vector4(dynLMObjs[dIdx].realtimeLightmapIndex, 0, 0, 0);
				scaleOffsetToIndex.Add(scaleOffset, dIdx);
			}

			ScriptableLightingData lightData = ScriptableObject.CreateInstance<ScriptableLightingData>();
			scriptableLightingData = lightData;
			lightData.Read(Lightmapping.lightingDataAsset);
			ScriptableLightingData.RendererData[] lmRendererData = lightData.lightmappedRendererData;
			int numData = lmRendererData.Length;
			int numFound = 0;
			StringBuilder logLms = new StringBuilder("Lightmapped object IDs found in scene: \n");

			uvMeshes = new List<Mesh>(new Mesh[numDynamic]);
			int numUVMeshes = 0;
			for (int lIdx = 0; lIdx < numData; lIdx++)
			{
				int dIdx = 0;
				float4 scaleOffset = lmRendererData[lIdx].lightmapSTDynamic + new Vector4(lmRendererData[lIdx].lightmapIndexDynamic, 0, 0, 0);
				//logLms.AppendLine(scaleOffset.ToString());
				if (scaleOffsetToIndex.TryGetValue(scaleOffset, out dIdx))
				{
					ScriptableLightingData.RendererData rData = lmRendererData[lIdx];
					uvMeshes[dIdx] = rData.uvMesh;
					if (rData.uvMesh != null)
					{
						numUVMeshes++;
					}
				}
			}


			
			rIdxToDynIdx = new int[numRenderers];
			for (int i = 0;  i < numRenderers; i++)
			{
				rIdxToDynIdx[i] = -1;
			}
			int numRemoved = 0;

			// We need to clean out null meshes from the list, as sometimes dynamic lightmapped objects don't get a uvMesh (or there's a collision using our scale-offset as an ID)
			for (int dIdx = 0; dIdx < numDynamic; dIdx++)
			{
				if (uvMeshes[dIdx] == null)
				{
					uvMeshes.RemoveAt(dIdx);
					dIdx -= 1;
					numRemoved += 1;
				}
				else
				{
					int originalDIdx = dIdx + numRemoved;
					int rIdx = dynIdxToRIdx[originalDIdx];
					rIdxToDynIdx[rIdx] = dIdx;
				}
			}

			dynUvMeshDataArray = MeshUtility.AcquireReadOnlyMeshData(uvMeshes);
			//Debug.Log(logLms.ToString());
			//Debug.Log($"SLZ Static Batching: {numDynamic} dynamic lightmapped objects with {numFound} found in lighting data asset");
			return true;
#else
			uvMeshes = null;
			rIdxToDynIdx = null;
			scriptableLightingData = null;
			dynUvMeshDataArray = (default);
			return false;
#endif
		}

#if USING_NEWBLOOD_LIGHTING_INTERNALS
		static uint4 SceneObjectIdentifierToUint4(in SceneObjectIdentifier id)
		{
			ulong prefabID = unchecked((ulong)id.targetPrefab);
			ulong sceneID = unchecked((ulong)id.targetObject);
			return new uint4((uint)(sceneID & 0xFFFFFFFFu), (uint)(sceneID >> 32), (uint)(prefabID & 0xFFFFFFFFu), (uint)(prefabID >> 32));
		}
#endif
	}
}