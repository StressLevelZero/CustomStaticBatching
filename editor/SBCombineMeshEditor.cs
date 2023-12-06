//#define USE_GPU_VTX_TRANSFER
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using static SLZ.CustomStaticBatching.SBCombineMeshList;
using static SLZ.CustomStaticBatching.PackedChannel;
using static UnityEngine.Mesh;
using System.Reflection;

namespace SLZ.CustomStaticBatching.Editor
{
	public static class SBCombineMeshEditor
	{

		#region EditorCompresssionSettings
		public static void FetchGlobalProjectSettings(this SBCombineMeshList cml)
		{
			CombineRendererSettings settings = SBSettingsSO.GlobalSettings.GetActiveBuildTargetSettings();
			cml.settings = settings;
		}

		public static void FetchTargetProjectSettings(this SBCombineMeshList cml, BuildTarget target)
		{
			CombineRendererSettings settings = SBSettingsSO.GlobalSettings.GetBuildTargetSettings(target);
			cml.settings = settings;
		}

		const string transferVtxGUID = "5bae5a4c97f51964dbc10d3398312270";

		public static ComputeShader GetTransferVtxComputeShader()
		{
			ComputeShader transferVtxBufferCompute;
			
			string computePath = AssetDatabase.GUIDToAssetPath(transferVtxGUID);
			if (string.IsNullOrEmpty(computePath))
			{
				throw new Exception("SLZ Static Batching: Failed to find the TransferVertexBuffer compute shader. There is no asset corresponding to the hard-coded GUID " + transferVtxGUID + ", mesh combining failed!");
			}
			transferVtxBufferCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(computePath);
			if (transferVtxBufferCompute == null)
			{
				throw new Exception("SLZ Static Batching: Failed to find the TransferVertexBuffer compute shader. Didn't find a compute shader at the path of the hard-coded GUID " + transferVtxGUID + ", mesh combining failed!");
			}
			return transferVtxBufferCompute;
		}

	
		#endregion



		static readonly ProfilerMarker profilerScaleSign = new ProfilerMarker("CustomStaticBatching.RendererScaleSign");
		static readonly ProfilerMarker profilerGetUniqueMeshes = new ProfilerMarker("CustomStaticBatching.GetUniqueMeshes");
		static readonly ProfilerMarker profilerGetMeshLayout = new ProfilerMarker("CustomStaticBatching.GetMeshLayout");
		static readonly ProfilerMarker profilerGetMeshBins = new ProfilerMarker("CustomStaticBatching.GetMeshBins");
		static readonly ProfilerMarker profilerGetCombinedLayout = new ProfilerMarker("CustomStaticBatching.GetCombinedLayout");
		static readonly ProfilerMarker profilerGetCombinedMesh = new ProfilerMarker("CustomStaticBatching.GetCombinedMesh");
		static readonly ProfilerMarker profilerComputeCopyMeshes = new ProfilerMarker("CustomStaticBatching.TransferToCombinedMesh");
		static readonly ProfilerMarker profilerAssignSBCombinedMesh = new ProfilerMarker("CustomStaticBatching.AssignSBCombinedMesh");

		/// <summary>
		/// Given a list of pre-sorted renderers, bin the renderers into groups, generate a combined mesh for each group, and assign that combined mesh to the renderers.
		/// This will ignore renderers with meshes that have unusual attibute formats in the vertex struct that don't translate well to floating point formats. 
		/// These include all integer formats and 16-bit normalized formats (16-bit could be done, I originally designed this around using a compute shader to do the 
		/// combining and didn't want to add more complexity to the kernel to handle more cases) 
		/// Assumes that the sorted list has all 32-bit index meshes sorted at the end of the array.
		/// </summary>
		/// <param name="cml">This SBCombineMeshList object</param>
		/// <param name="sortedRenderers">List of presorted renderers, assumes all 32-bit index mesh renderers are at the end</param>
		public static void GenerateStaticBatches(this SBCombineMeshList cml, RendererData[] sortedRenderers)
		{
			List<Mesh> uniqueMeshList;
			int[] renderer2Mesh;
			MeshDataArray uniqueMeshData;

			// Get the sign of each transform's scale. Needed for the sign of the bitangent post object to world transformation, which is stored in the tangent's 4th component
			profilerScaleSign.Begin();
			NativeArray<byte> rendererScaleSign = cml.GetRendererScaleSign(sortedRenderers);
			profilerScaleSign.End();

			// Get a list of the meshes used in the scene, and a mapping from the list of renderers to the list of meshes
			profilerGetUniqueMeshes.Begin();
			ParallelGetUniqueMeshes(sortedRenderers, out uniqueMeshList, out uniqueMeshData, out renderer2Mesh);
			profilerGetUniqueMeshes.End();


			// Get the layout of the vertex struct of each unique mesh, also making an array of bools for meshes that have bad vertex structs that can be rationally merged with other meshes
			NativeArray<PackedChannel> uniqueMeshLayout;
			NativeArray<byte> invalidMeshes;
			profilerGetMeshLayout.Begin();
			ParallelGetMeshLayout(uniqueMeshData, out uniqueMeshLayout, out invalidMeshes);
			profilerGetMeshLayout.End();

			// Shift all the renderers with valid meshes to the front of the array
			int validRendererLen = cml.CleanInvalidRenderers(invalidMeshes, sortedRenderers, renderer2Mesh);
			invalidMeshes.Dispose();

			// Segment the mesh list into groups that will each be a single combined mesh.
			// For 16 bit index buffers, the segments are split when the number of vertices exceeds the ushort limit.
			// For 32 bit buffers, the segments are split according to a configurable max number of vertices
			ushort[] renderer2CMeshIdx;
			List<int2> cMeshIdxRange;
			int _32bitIdxStart;
			profilerGetMeshBins.Begin();
			cml.GetCombinedMeshBins(sortedRenderers, validRendererLen, out renderer2CMeshIdx, out cMeshIdxRange, out _32bitIdxStart);
			profilerGetMeshBins.End();

			int numCombinedMeshes = cMeshIdxRange.Count;
			NativeArray<PackedChannel>[] combinedMeshLayouts = new NativeArray<PackedChannel>[numCombinedMeshes];
			Mesh[] combinedMeshes = new Mesh[numCombinedMeshes];
#if USE_GPU_VTX_TRANSFER
			AsyncMeshReadbackData[] combinedMeshReadbacks = new AsyncMeshReadbackData[numCombinedMeshes];
#endif

			for (int i = 0; i < numCombinedMeshes; i++)
			{
				profilerGetCombinedLayout.Begin();
				combinedMeshLayouts[i] = cml.GetCombinedMeshLayout(sortedRenderers, ref uniqueMeshLayout, renderer2Mesh, cMeshIdxRange[i].x, cMeshIdxRange[i].y);
				profilerGetCombinedLayout.End();
				//DebugMessage += string.Format("Combined mesh {0}: {1}\n", i, VtxStructToString(combinedMeshLayouts[i], 0));
				Mesh combinedMesh;
				profilerGetCombinedMesh.Begin();
				if (i < _32bitIdxStart)
				{
					combinedMesh = cml.GetCombinedMeshObject<ushort>(sortedRenderers, uniqueMeshData, cMeshIdxRange[i], renderer2Mesh, ref combinedMeshLayouts[i], ref rendererScaleSign, false);
				}
				else
				{
					combinedMesh = cml.GetCombinedMeshObject<int>(sortedRenderers, uniqueMeshData, cMeshIdxRange[i], renderer2Mesh, ref combinedMeshLayouts[i], ref rendererScaleSign, false);
				}
				profilerGetCombinedMesh.End();
				combinedMesh.name = "Combined Mesh (" + i + ")";
				combinedMeshes[i] = combinedMesh;
				profilerComputeCopyMeshes.Begin();
#if USE_GPU_VTX_TRANSFER
				combinedMeshReadbacks[i] = cml.ComputeCopyMeshes(ref uniqueMeshLayout, ref combinedMeshLayouts[i], ref rendererScaleSign, CombinedMesh, sortedRenderers, cMeshIdxRange[i], renderer2Mesh, ref invalidMeshes, uniqueMeshList);
#else
				cml.JobCopyMeshes(ref uniqueMeshLayout, ref combinedMeshLayouts[i], ref rendererScaleSign, combinedMesh, sortedRenderers, cMeshIdxRange[i], renderer2Mesh, uniqueMeshData);
#endif
				profilerComputeCopyMeshes.End();
				profilerAssignSBCombinedMesh.Begin();
				AssignSBCombinedMesh(combinedMesh, sortedRenderers, renderer2Mesh, cMeshIdxRange[i]);
				profilerAssignSBCombinedMesh.End();
				//combinedMeshLayouts[i].Dispose();
			}
			//Debug.Log(DebugMessage);
#if USE_GPU_VTX_TRANSFER
			for (int i = 0; i < numCombinedMeshes; i++)
			{
				combinedMeshReadbacks[i].FinishMeshReadback(combinedMeshes[i]);
			}
#endif
		
			rendererScaleSign.Dispose();
			uniqueMeshLayout.Dispose();
			//invalidMeshes.Dispose();
			uniqueMeshData.Dispose();
			for (int i = 0; i < cMeshIdxRange.Count; i++)
			{
				combinedMeshLayouts[i].Dispose();
			}
		}

		delegate void dSetStaticBatchInfo(Renderer renderer, int firstSubMesh, int subMeshCount);
		static dSetStaticBatchInfo s_SetStaticBatchInfo;
		static dSetStaticBatchInfo SetStaticBatchInfo
		{
			get
			{
				if (s_SetStaticBatchInfo == null) 
				{ 
					MethodInfo minfo = typeof(Renderer).GetMethod("SetStaticBatchInfo", BindingFlags.Instance | BindingFlags.NonPublic);
					s_SetStaticBatchInfo = (dSetStaticBatchInfo)minfo.CreateDelegate(typeof(dSetStaticBatchInfo));
				}
				return s_SetStaticBatchInfo;
			}
		}

		public static void AssignSBCombinedMesh(Mesh combinedMesh, RendererData[] rd, int[] renderer2Mesh, int2 rendererRange)
		{

			int submeshIdx = 0;
			MeshRenderer[] mrArray = new MeshRenderer[1];
			for (int i = rendererRange.x; i < rendererRange.y; i++)
			{
				rd[i].meshFilter.sharedMesh = combinedMesh;
				mrArray[0] = rd[i].meshRenderer;
				SerializedObject so = new SerializedObject(mrArray); // Pass this an array instead of the renderer directly, otherwise every time we call this it internally allocates a 1-long array!
				
				SerializedProperty spFirst = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
				spFirst.intValue = submeshIdx;
				
				int submeshCount = rd[i].mesh.subMeshCount;
				SerializedProperty spCount = so.FindProperty("m_StaticBatchInfo.subMeshCount");
				spCount.intValue = submeshCount;
				
				so.ApplyModifiedPropertiesWithoutUndo();
				
				//SetStaticBatchInfo(rd[i].meshRenderer, submeshIdx, submeshCount);
				
				GameObject go = rd[i].rendererTransform.gameObject;
				StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
				GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.BatchingStatic);
				EditorUtility.SetDirty(go);
				submeshIdx += submeshCount;
			}
		}


		public static RendererData[] GetSortedRendererData(List<MeshRenderer> renderers, SBCombineMeshList combiner)
		{
			int numRenderers = renderers.Count;
			int rendererIdx = 0;
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

			//if (renderers.Count < 2)
			//{
			//	return new RendererData[0];
			//}

			RendererData[] sortedData = RendererSort.GetSortedData(renderers, meshFilters);
			return sortedData;
		}
	}
}
