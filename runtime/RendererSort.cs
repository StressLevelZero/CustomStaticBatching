using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Profiling;

namespace SLZ.CustomStaticBatching
{
	/// <summary>
	/// Data used to sort renderers for static batching.
	/// Sorts by each property that can break rendering contiguous sections of the static batch in order of decreasing importance.
	/// 0. 32 bit-index buffer useage. We don't want to upcast 16-bit index buffer meshes to 32 bit in order to combine 16 and 32 bit meshes, as that would dramatically increase memory usage.
	/// 1. Having multiple materials, this is very important because each multi-material mesh breaks contiguous sections by definition.
	/// 2. Active state. Assuming the vast majority of static meshes that are off will never be activated, we don't want to create holes in the buffer for meshes that will never be visible
	/// 3. TODO: Zone ID (ID of the batching volume the renderer is in, 0xFFFF = not in a zone)
	/// 4. Shader, using the instanceID as a proxy
	/// 5. Hash of the local keywords on the shader, as these represent different shader programs
	/// 6. Material, using the instanceID as a proxy
	/// 7. Lightmap index
	/// 8. Hilbert index
	/// </summary>
	public struct RendererSortItem : IComparable<RendererSortItem>
	{
		public int rendererArrayIdx;

		public ushort breakingState; // flags to bin the most important boolean properties. is multi-material in highest bit, is active in lowest
		public ushort zoneID;
		public int shaderID;
		public ulong variantHash;
		public int materialID;
		public ushort lightmapIdx;
		//public ulong probeId; // Pack two int IDs for the two most important probes // Not used for now, seems to cause issues
		public ulong hilbertIdx;
		public int LODLevel;

		//[BurstCompatible]
		public int CompareTo(RendererSortItem other)
		{
			if (breakingState != other.breakingState)
			{
				return breakingState > other.breakingState ? 1 : -1;
			}
			if (zoneID != other.zoneID)
			{
				return zoneID > other.zoneID ? 1 : -1;
			}
			if (shaderID != other.shaderID)
			{
				return shaderID > other.shaderID ? 1 : -1;
			}
			if (variantHash != other.variantHash)
			{
				return variantHash > other.variantHash ? 1 : -1;
			}
			if (materialID != other.materialID)
			{
				return materialID > other.materialID ? 1 : -1;
			}
			if (lightmapIdx != other.lightmapIdx)
			{
				return lightmapIdx > other.lightmapIdx ? 1 : -1;
			}
			//if (probeId != other.probeId)
			//{
			//	return probeId > other.probeId ? 1 : -1;
			//}

			if (LODLevel != other.LODLevel)
			{
				return LODLevel < other.LODLevel ? 1 : -1;
			}

			if (hilbertIdx != other.hilbertIdx)
			{
				return hilbertIdx > other.hilbertIdx ? 1 : -1;
			}
			return 0;
		}
	}

	public struct LODInfo
	{
		public LODGroup lodRoot;
		public Vector3 lodCenter;
		public uint lodLevel;
	}

	/// <summary>
	/// Utility class to get the internal index for local shader keywords. In editor it uses IL generation to create a method to access the internal field. 
	/// In the player it uses the UnsafeUtility to treat the LocalKeyword as a different struct with a matching layout that has m_Index exposed, 
	/// as we can't do runtime code generation with IL2CPP. It might be better to use this for both, but the IL generation method should be much less brittle.
	/// </summary>
	internal static class ReflectKWFields
	{
		static bool m_Initialized = false;
		public static bool Initialized { get { return m_Initialized; } }
		static Func<LocalKeyword, uint> _localKW_Index;

		public static void GetDelegate()
		{
#if UNITY_EDITOR
			FieldInfo fieldInfo = typeof(LocalKeyword).GetField("m_Index", BindingFlags.NonPublic | BindingFlags.Instance);
			if (fieldInfo == null)
			{
				throw new InvalidOperationException("Can't find FieldInfo for m_Index");
			}
			_localKW_Index = CreateGetter<LocalKeyword, uint>(fieldInfo);
#endif
			m_Initialized = true;
		}

#if UNITY_EDITOR
		private static Func<T, R> CreateGetter<T, R>(FieldInfo field)
		{
			string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
			DynamicMethod getterMethod = new DynamicMethod(methodName, typeof(R), new Type[] { typeof(T) }, true);
			ILGenerator gen = getterMethod.GetILGenerator();

			gen.Emit(OpCodes.Ldarg_0);
			gen.Emit(OpCodes.Ldfld, field);
			gen.Emit(OpCodes.Ret);
			
			return (Func<T, R>)getterMethod.CreateDelegate(typeof(Func<T, R>));
		}
#endif

		public static uint GetIndex(LocalKeyword kw)
		{
			if (m_Initialized)
			{
				return GetIndexUnsafe(kw);
			}
			else
			{
				GetDelegate();
				return GetIndexUnsafe(kw);
			}
		}

		/// <summary>
		/// Gets the index of the local shader keyword, assuming GetDelegate() has alread been called.
		/// </summary>
		/// <param name="kw"></param>
		/// <returns></returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetIndexUnsafe(LocalKeyword kw)
		{
#if UNITY_EDITOR
			return _localKW_Index.Invoke(kw);
#else
			return UnsafeUtility.As<LocalKeyword, LocalKeywordInternals>(ref kw).m_Index;
#endif
		}

		/// <summary>
		/// struct that matches the fields and layout of LocalKeyword, which we can treat a LocalKeyword as to get access to m_Index 
		/// </summary>
		readonly struct LocalKeywordInternals
		{
			internal readonly LocalKeywordSpace m_SpaceInfo;

			internal readonly string m_Name;

			internal readonly uint m_Index;
		}

	}

	/// <summary>
	/// Utilities for getting the hash of a shader's keywords
	/// </summary>
	internal static class ShaderKWHash
	{
		/// <summary>
		/// Gets hash of a local keyword array ASSUMING YOU CALLED ReflectKWFields.GetDelegate() FIRST!
		/// Assumes the shader they belong to has less than 68 keywords, including global keywords! 
		/// </summary>
		/// <param name="kwArray">Array of keywords to get the hash of</param>
		/// <returns>Hash of the keyword array</returns>
		const int DefaultGlobalKWCount = 4; // STEREO_INSTANCING_ON UNITY_SINGLE_PASS_STEREO STEREO_MULTIVIEW_ON STEREO_CUBEMAP_RENDER_ON 
		public static ulong GetHashUnsafe(LocalKeyword[] kwArray)
		{
			int kwArrayLength = kwArray.Length;
			if (kwArrayLength == 0)
				return 0ul;

			ulong hash = 0ul;

			Span<ushort> kwIdx = kwArrayLength < 17 ? stackalloc ushort[kwArrayLength] : new ushort[kwArrayLength];
			int j = 0;
			for (int i = 0; i < kwArrayLength; i++)
			{
#if UNITY_2022_2_OR_NEWER
				if (!kwArray[i].isDynamic)
#endif
				{
					kwIdx[j] = (ushort)(ReflectKWFields.GetIndexUnsafe(kwArray[i]) - DefaultGlobalKWCount);
					j++;
				}
			}
			for (int i = 0; i < j; i++)
			{
				hash += 1ul<<(int)kwIdx[i];
			}
			return hash;
		}
	}

	/// <summary>
	/// Class that contains the method used to sort a list of renderers for combining in static batched meshes
	/// </summary>
	public static class RendererSort
	{
		static readonly ProfilerMarker profileSortRenderers = new ProfilerMarker("CustomStaticBatching.SortRenderers");
		static readonly ProfilerMarker profileGetRenderersSortData = new ProfilerMarker("CustomStaticBatching.GetRenderersSortData");
		static readonly ProfilerMarker profileGetRenderersHilbertIdx = new ProfilerMarker("CustomStaticBatching.GetRenderersHilbertIdx");
		static readonly ProfilerMarker profileGetMaterials = new ProfilerMarker("CustomStaticBatching.profileGetMaterials");

		/// <summary>
		/// Given a list of mesh renderers and a corresponding list of mesh filters, creates an array of renderer data sorted for static batching
		/// </summary>
		/// <param name="meshRenderers"> List of mesh renderers</param>
		/// <param name="filters"> List of mesh filters that correspond to each element of meshRenderers</param>
		/// <returns></returns>
		public static RendererData[] GetSortedData(List<MeshRenderer> meshRenderers, List<MeshFilter> filters, Dictionary<MeshRenderer, LODInfo> lodInfo = null)
		{
			profileSortRenderers.Begin();

			profileGetMaterials.Begin();

			bool hasLODInfo = lodInfo != null;

			// Get an array of unique materials referenced by the renderers, and a mapping from a renderer to the index of its first material in the unique array
			int[] rendererToMaterial;
			Material[] uniqueMats;
			GetUniqueMaterials(meshRenderers, out rendererToMaterial, out uniqueMats);
			profileGetMaterials.End();

			// For each unique material, get a hash of the material, a hash of the shader it uses, and a hash of the enabled keywords on the material
			MaterialAndShaderID[] matShaderIds = GetMaterialAndShaderIDs(uniqueMats, rendererToMaterial);

			profileGetRenderersHilbertIdx.Begin();

			
			NativeArray<UInt64> hilbertIdxs;
			bool hasLODs = lodInfo != null;
			int[] lodLevels;
			if (hasLODs)
			{
				NativeArray<Vector3> meshPoses = new NativeArray<Vector3>(meshRenderers.Count, Allocator.TempJob);
				GetLodLevelsAndOrigins(meshRenderers, lodInfo, ref meshPoses, out lodLevels);
				hilbertIdxs = GetHilbertIdxs(meshRenderers, ref meshPoses, lodLevels);
			}
			else
			{
				lodLevels = null;
				hilbertIdxs = GetHilbertIdxs(meshRenderers);
			}
			// Get the hilbert index of the bounds center of each mesh renderer, in the cubic bounding box that encapsulates all the static meshes to be combined.
			// TODO: Also pass batching volumes and a BVH tree to accelerate finding the appropriate batching volume for each mesh
			
			profileGetRenderersHilbertIdx.End();

			profileGetRenderersSortData.Begin();
			// Generate an array of data for each renderer that will be used to sort the renderers.
			int numRenderers = meshRenderers.Count;
			NativeArray<RendererSortItem> rendererSortItems = new NativeArray<RendererSortItem>(numRenderers, Allocator.TempJob);
			List<ReflectionProbeBlendInfo> closestProbes = new List<ReflectionProbeBlendInfo> ();
			for (int i = 0; i < numRenderers; i++)
			{
				MeshRenderer mr = meshRenderers[i];
				int materialIdx = rendererToMaterial[i];

				// I was going to sort on probe ID's, but it seems to break batching worse than not? Sorting spatially should ensure the probe indices is also mostly sorted anyways.
				// NOTE: coming back a year later, I can see what's wrong. 1) Sorting by probe hash is obviously not spatially correct. I would need to gather and hilbert sort the
				// probes as well. 2) the SRP's don't sort the probe list by weight, so probes 0 and 1 aren't necessarily the dominant probes.
				//mr.GetClosestReflectionProbes(closestProbes);
				//int numClosest = closestProbes.Count;
				//ReadOnlySpan<int> probeHash = stackalloc int[2] { 
				//	numClosest > 1 ? closestProbes[1].probe.GetHashCode() : -0x7fffffff,
				//	numClosest > 0 ? closestProbes[0].probe.GetHashCode() : -0x7fffffff}; // Assumes little-endian
				//closestProbes.Clear();
				bool isMultiMaterial = IsMultiMaterial(mr.sharedMaterials);
				ushort breakingState = (ushort)((isMultiMaterial ? 0x4000u : 0u) + (mr.gameObject.activeInHierarchy && mr.enabled ? 0u : 1u));
				rendererSortItems[i] = new RendererSortItem
				{
					rendererArrayIdx = i,
					breakingState = breakingState, // 0 if single material, 1 otherwise
					shaderID = matShaderIds[materialIdx].shaderID,
					variantHash = matShaderIds[materialIdx].keywordHash,
					materialID = matShaderIds[materialIdx].materialID,
					lightmapIdx = (ushort)mr.lightmapIndex,
					//probeId = MemoryMarshal.Cast<int, ulong>(probeHash)[0], // Pack two int IDs for the two most important probes
					zoneID = 0,
					hilbertIdx = hilbertIdxs[i],
					LODLevel = hasLODs ? Mathf.Max(lodLevels[i], 0) : 0 // lodLevels is -1 for renderers not in a group, max with 0
				};
			}
			hilbertIdxs.Dispose();
			profileGetRenderersSortData.End();

			// Sort the renderers using the Collection's package extensions for NativeArray sorting
			var sortJob = NativeSortExtension.SortJob(rendererSortItems);
			JobHandle sortJobHandle = sortJob.Schedule();
			sortJobHandle.Complete();

			// Populate an array of RendererData using the sorted items
			RendererData[] rendererData = new RendererData[numRenderers];
			for (int i = 0; i < numRenderers; i++)
			{
				int rendererIdx = rendererSortItems[i].rendererArrayIdx;
				MeshFilter filter = filters[rendererIdx];
				bool monoMaterial = (rendererSortItems[i].breakingState & 0x4000u) == 0u;
				int materialCount = meshRenderers[rendererIdx].sharedMaterials.Length;
				rendererData[i] = new RendererData
				{
					mesh = filter.sharedMesh,
					meshFilter = filter,
					meshRenderer = meshRenderers[rendererIdx],
					rendererTransform = filter.transform,
					shader = meshRenderers[rendererIdx].sharedMaterial?.shader,
					submeshCount = materialCount,
					combineDuplicateMaterials = monoMaterial && (materialCount > 1)
				};
			}
			rendererSortItems.Dispose();
			profileSortRenderers.End();
			return rendererData;
		}



		static void GetUniqueMaterials(List<MeshRenderer> meshRenderers, out int[] rendererToMaterial, out Material[] uniqueMats)
		{
			Dictionary<Material, int> matToIdx = new Dictionary<Material, int>();
			List<Material> uniqueMatList = new List<Material>();
			rendererToMaterial = new int[meshRenderers.Count];
			int uniqueCount = 0;
			for (int i = 0; i < meshRenderers.Count; i++)
			{
				Material mat = meshRenderers[i].sharedMaterial;
				int index = 0;
				if (matToIdx.TryGetValue(mat, out index))
				{
					rendererToMaterial[i] = index;
					continue;
				}
				rendererToMaterial[i] = uniqueCount;
				matToIdx.Add(mat, uniqueCount);
				uniqueMatList.Add(mat);
				uniqueCount++;
			}
			uniqueMats = uniqueMatList.ToArray();
		}

		struct MaterialAndShaderID
		{
			public int shaderID;
			public int materialID;
			public ulong keywordHash;
		}
		static MaterialAndShaderID[] GetMaterialAndShaderIDs(Material[] uniqueMats, int[] rendererToMaterial)
		{
			int matLength = uniqueMats.Length;
			MaterialAndShaderID[] matShaderIDs = new MaterialAndShaderID[matLength];
			if (!ReflectKWFields.Initialized) ReflectKWFields.GetDelegate(); // Initialize delegate for getting the shader keyword hash
			for (int i = 0; i < matLength; i++)
			{
				matShaderIDs[i] = new MaterialAndShaderID
				{
					shaderID = uniqueMats[i].shader.GetHashCode(),
					materialID = uniqueMats[i].GetHashCode(),
					keywordHash = ShaderKWHash.GetHashUnsafe(uniqueMats[i].enabledKeywords)
				};
			}
			return matShaderIDs;
		}

		static bool IsMultiMaterial(Material[] materials)
		{
			int numMats = materials.Length;
			Material m0 = materials[0];
			for (int mIdx = 1; mIdx < numMats; mIdx++)
			{
				if (materials[mIdx] != m0)
				{
					return true;
				}
			}
			return false;
		}

		static void GetLodLevelsAndOrigins(List<MeshRenderer> meshRenderers, Dictionary<MeshRenderer, LODInfo> lodInfo, ref NativeArray<Vector3> positions, out int[] lodLevels)
		{
			int numRenderers = meshRenderers.Count;
			lodLevels = new int[numRenderers];
			
			for (int i = 0; i < numRenderers; i++)
			{
				if (lodInfo.TryGetValue(meshRenderers[i], out LODInfo lod))
				{
					lodLevels[i] = (int)lod.lodLevel;
					positions[i] = lod.lodCenter;
				}
				else
				{
					lodLevels[i] = -1;
				}
			}
		}

		static NativeArray<UInt64> GetHilbertIdxs(List<MeshRenderer> meshRenderers)
		{
			int length = meshRenderers.Count;
			NativeArray<Vector3> positions = new NativeArray<Vector3>(length, Allocator.TempJob);
			Bounds hilbertBounds = meshRenderers[0].bounds;
			// Use lod origin as the renderer location rather than the bounds center if in a LOD Group. This ensures that members of
			// the same LOD group always get sorted together by LOD level

			for (int i = 0; i < length; i++)
			{
				Bounds bounds = meshRenderers[i].bounds;
				positions[i] = bounds.center;
				hilbertBounds.Encapsulate(bounds);
			}
			float maxDim = math.max(math.max(hilbertBounds.extents.x, hilbertBounds.extents.y), hilbertBounds.extents.z);
			Vector3 minExtent = hilbertBounds.center - hilbertBounds.extents;
			hilbertBounds.extents = new Vector3(maxDim, maxDim, maxDim);
			hilbertBounds.center = minExtent + hilbertBounds.extents;
			NativeArray<UInt64> hilbertIdxs = HilbertIndex.GetHilbertIndices(positions, hilbertBounds, Allocator.TempJob);
			positions.Dispose();
			return hilbertIdxs;
		}

		static NativeArray<UInt64> GetHilbertIdxs(List<MeshRenderer> meshRenderers, ref NativeArray<Vector3> positions, int[] lodLevels)
		{
			int length = meshRenderers.Count;
			Bounds hilbertBounds = meshRenderers[0].bounds;
			// Use lod origin as the renderer location rather than the bounds center if in a LOD Group. This ensures that members of
			// the same LOD group always get sorted together by LOD level

			for (int i = 0; i < length; i++)
			{
				Bounds bounds = meshRenderers[i].bounds;
				if (lodLevels[i] == -1) // -1 means no lod group. It is assumed that GetLodLevelsAndOrigins was called before and filled the positions for lod renderers
				{
					positions[i] = bounds.center;
				}
				hilbertBounds.Encapsulate(bounds);
			}
			float maxDim = math.max(math.max(hilbertBounds.extents.x, hilbertBounds.extents.y), hilbertBounds.extents.z);
			Vector3 minExtent = hilbertBounds.center - hilbertBounds.extents;
			hilbertBounds.extents = new Vector3(maxDim, maxDim, maxDim);
			hilbertBounds.center = minExtent + hilbertBounds.extents;
			NativeArray<UInt64> hilbertIdxs = HilbertIndex.GetHilbertIndices(positions, hilbertBounds, Allocator.TempJob);
			positions.Dispose();
			return hilbertIdxs;
		}


	}
}
