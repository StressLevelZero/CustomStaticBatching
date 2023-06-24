using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using static SLZ.CustomStaticBatching.SBCombineMeshList;
using static UnityEngine.Mesh;

namespace SLZ.CustomStaticBatching.Editor
{
	public static class SBCombineMeshEditor
	{
		public static void SetCompressionFromProjectSettings(this SBCombineMeshList cml)
		{
			SerializedObject projectSettings = GetProjectSettingsAsset();
			cml.vertexFormatCompression = new VtxFormats[NUM_VTX_CHANNELS];
			
			int vertexCompressionFlags = 0;
			if (projectSettings == null)
			{
				Debug.LogError("Custom Static Batching: Could not find ProjectSettings.asset, will assume all channels are uncompressed");
			}
			else
			{
				SerializedProperty vertexCompression = projectSettings.FindProperty("VertexChannelCompressionMask");
				if (vertexCompression == null)
				{
					Debug.LogError("Custom Static Batching: Could not find VertexChannelCompressionMask in ProjectSettings.asset, will assume all channels are uncompressed");
				}
				else
				{
					vertexCompressionFlags = vertexCompression.intValue;
				}
			}

			cml.vertexFormatCompression[0] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Position) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[1] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Normal) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[2] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Tangent) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[3] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Color) == 0 ? VtxFormats.Float32 : VtxFormats.UNorm8;
			cml.vertexFormatCompression[4] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord0) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[5] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord1) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[6] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord2) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			cml.vertexFormatCompression[7] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord3) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			for (int i = 8; i < cml.vertexFormatCompression.Length; i++)
			{
				cml.vertexFormatCompression[i] = VtxFormats.Float32;
			}
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

		static SerializedObject GetProjectSettingsAsset()
		{
			const string projectSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";
			UnityEngine.Object projSettingsObj = AssetDatabase.LoadMainAssetAtPath(projectSettingsAssetPath);
			if (projSettingsObj == null)
			{
				return null;
			}
			else
			{
				SerializedObject projectSettings = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(projectSettingsAssetPath));
				return projectSettings;
			}
		}

		public static void AssignSBCombinedMesh(Mesh combinedMesh, RendererData[] rd, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, int2 rendererRange)
		{

			int submeshIdx = 0;
			MeshRenderer[] mrArray = new MeshRenderer[1];
			for (int i = rendererRange.x; i < rendererRange.y; i++)
			{
				if (invalidMeshes[renderer2Mesh[i]] == 0)
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

					GameObject go = rd[i].rendererTransform.gameObject;
					StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
					GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.BatchingStatic);
					submeshIdx += submeshCount;
				}
			}
		}

		static readonly ProfilerMarker profilerScaleSign = new ProfilerMarker("CustomStaticBatching.RendererScaleSign");
		static readonly ProfilerMarker profilerGetUniqueMeshes = new ProfilerMarker("CustomStaticBatching.GetUniqueMeshes");
		static readonly ProfilerMarker profilerGetMeshLayout = new ProfilerMarker("CustomStaticBatching.GetMeshLayout");
		static readonly ProfilerMarker profilerGetMeshBins = new ProfilerMarker("CustomStaticBatching.GetMeshBins");
		static readonly ProfilerMarker profilerGetCombinedLayout = new ProfilerMarker("CustomStaticBatching.GetCombinedLayout");
		static readonly ProfilerMarker profilerGetCombinedMesh = new ProfilerMarker("CustomStaticBatching.GetCombinedMesh");
		static readonly ProfilerMarker profilerComputeCopyMeshes = new ProfilerMarker("CustomStaticBatching.ComputeCopyMeshes");
		static readonly ProfilerMarker profilerAssignSBCombinedMesh = new ProfilerMarker("CustomStaticBatching.AssignSBCombinedMesh");
		public static void GenerateStaticBatches(this SBCombineMeshList cml, RendererData[] sortedRenderers)
		{
			List<Mesh> uniqueMeshList;
			int[] renderer2Mesh;
			MeshDataArray uniqueMeshData;
			profilerScaleSign.Begin();
			NativeArray<byte> rendererScaleSign = cml.GetRendererScaleSign(sortedRenderers);
			profilerScaleSign.End();

			profilerGetUniqueMeshes.Begin();
			cml.ParallelGetUniqueMeshes(sortedRenderers, out uniqueMeshList, out uniqueMeshData, out renderer2Mesh);
			profilerGetUniqueMeshes.End();
			NativeArray<PackedChannel> uniqueMeshLayout;
			NativeArray<byte> invalidMeshes;
			profilerGetMeshLayout.Begin();
			cml.ParallelGetMeshLayout(uniqueMeshData, out uniqueMeshLayout, out invalidMeshes);
			profilerGetMeshLayout.End();

			//string DebugMessage = "Single Meshes\n";
			//for (int i = 0; i < uniqueMeshList.Count;i++)
			//{
			//	DebugMessage += string.Format("{0}: {1}\n", uniqueMeshList[i].name, VtxStructToString(uniqueMeshLayout, i*12));
			//}

			ushort[] renderer2CMeshIdx;
			List<int2> cMeshIdxRange;
			profilerGetMeshBins.Begin();
			cml.GetCombinedMeshBins16(sortedRenderers, renderer2Mesh, invalidMeshes, out renderer2CMeshIdx, out cMeshIdxRange);
			profilerGetMeshBins.End();
			int numCombinedMeshes = cMeshIdxRange.Count;
			NativeArray<PackedChannel>[] combinedMeshLayouts = new NativeArray<PackedChannel>[numCombinedMeshes];
			Mesh[] combinedMeshes = new Mesh[numCombinedMeshes];
			AsyncMeshReadbackData[] combinedMeshReadbacks = new AsyncMeshReadbackData[numCombinedMeshes];

			for (int i = 0; i < numCombinedMeshes; i++)
			{
				profilerGetCombinedLayout.Begin();
				combinedMeshLayouts[i] = cml.GetCombinedMeshLayout(sortedRenderers, ref uniqueMeshLayout, ref invalidMeshes, renderer2Mesh, cMeshIdxRange[i].x, cMeshIdxRange[i].y);
				profilerGetCombinedLayout.End();
				//DebugMessage += string.Format("Combined mesh {0}: {1}\n", i, VtxStructToString(combinedMeshLayouts[i], 0));
				profilerGetCombinedMesh.Begin();
				Mesh CombinedMesh = cml.GetCombinedMeshObject(sortedRenderers, uniqueMeshList, cMeshIdxRange[i], renderer2Mesh, ref invalidMeshes, ref combinedMeshLayouts[i], ref rendererScaleSign);
				profilerGetCombinedMesh.End();
				CombinedMesh.name = "Combined Mesh (" + i + ")";
				combinedMeshes[i] = CombinedMesh;
				profilerComputeCopyMeshes.Begin();
				combinedMeshReadbacks[i] = cml.ComputeCopyMeshes(ref uniqueMeshLayout, ref combinedMeshLayouts[i], ref rendererScaleSign, CombinedMesh, sortedRenderers, cMeshIdxRange[i], renderer2Mesh, ref invalidMeshes, uniqueMeshList);
				profilerComputeCopyMeshes.End();
				profilerAssignSBCombinedMesh.Begin();
				AssignSBCombinedMesh(CombinedMesh, sortedRenderers, renderer2Mesh, ref invalidMeshes, cMeshIdxRange[i]);
				profilerAssignSBCombinedMesh.End();
				//combinedMeshLayouts[i].Dispose();
			}
			//Debug.Log(DebugMessage);
			for (int i = 0; i < numCombinedMeshes; i++)
			{
				combinedMeshReadbacks[i].FinishMeshReadback(combinedMeshes[i]);
			}

		
			//var mf1 = test.AddComponent<MeshFilter>();
			//mf1.sharedMesh = CombinedMesh;
			//var mr1 = test.AddComponent<MeshRenderer>();
			rendererScaleSign.Dispose();
			uniqueMeshLayout.Dispose();
			invalidMeshes.Dispose();
			uniqueMeshData.Dispose();
			for (int i = 0; i < cMeshIdxRange.Count; i++)
			{
				combinedMeshLayouts[i].Dispose();
			}
		}
	}
}
