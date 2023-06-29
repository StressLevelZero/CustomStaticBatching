using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;

namespace SLZ.CustomStaticBatching
{
	/// <summary>
	/// Data used to sort renderers for static batching.
	/// Sorts by each property that can break rendering contiguous sections of the static batch in order of decreasing importance.
	/// 1. Having multiple materials, this is most important because each multi-material mesh breaks contiguous sections by definition.
	/// 2. Shader, using the instanceID as a proxy
	/// 3. Hash of the local keywords on the shader, as these represent different shader programs
	/// 4. Material (instanceID)
	/// 5. Lightmap index
	/// 6. Probes IDs (sort probes ourself using a hilbert curve? or just use the instanceID?)
	/// 7. Zone ID (ID of the batching volume the renderer is in, 0xFFFF = not in a zone)
	/// 8. Hilbert index
	/// </summary>
	public struct RendererSortItem : IComparable<RendererSortItem>
	{
		public int rendererArrayIdx;

		public ushort multiMaterial; // 0 if single material, 1 otherwise
		public ulong shaderID;
		public ulong variantHash;
		public ulong materialID;
		public ushort lightmapIdx;
		public long probeId; // Pack two int IDs for the two most important probes
		public ushort zoneID;
		public ulong hilbertIdx;

		public int CompareTo(RendererSortItem other)
		{
			if (multiMaterial != other.multiMaterial)
			{
				return multiMaterial > other.multiMaterial ? 1 : -1;
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
			if (probeId != other.probeId)
			{
				return probeId > other.probeId ? 1 : -1;
			}
			if (zoneID != other.zoneID)
			{
				return zoneID > other.zoneID ? 1 : -1;
			}
			if (hilbertIdx != other.hilbertIdx)
			{
				return hilbertIdx > other.hilbertIdx ? 1 : -1;
			}
			return 0;
		}
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetIndexUnsafe(LocalKeyword kw)
		{
#if UNITY_EDITOR
			return _localKW_Index.Invoke(kw);
#else
			return UnsafeUtility.As<LocalKeyword, LocalKeywordInternals>(ref kw).m_Index;
#endif
		}
		readonly struct LocalKeywordInternals
		{
			internal readonly LocalKeywordSpace m_SpaceInfo;

			internal readonly string m_Name;

			internal readonly uint m_Index;
		}

	}

	internal static class ShaderKWHash
	{
		/// <summary>
		/// Gets hash of a local keyword array ASSUMING YOU CALLED ReflectKWFields.GetDelegate() FIRST!
		/// Assumes the shader they belong to has less than 64 keywords, including global keywords! 
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


	public class RendererSort
	{
		public void Sort(List<MeshRenderer> meshRenderers)
		{
			
		}



		void GetUniqueMaterials(List<MeshRenderer> meshRenderers, out int[] rendererToMaterial, out Material[] uniqueMats)
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
		MaterialAndShaderID[] GetMaterialAndShaderIDs(List<MeshRenderer> meshRenderers, Material[] uniqueMats, int[] rendererToMaterial)
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

		NativeArray<UInt64> GetHilbertIdxs(List<MeshRenderer> meshRenderers)
		{
			int length = meshRenderers.Count;
			NativeArray<Vector3> positions = new NativeArray<Vector3>(length, Allocator.TempJob);
			Bounds hilbertBounds = meshRenderers[0].bounds;
			for (int i = 0; i < length; i++) 
			{
				Bounds bounds = meshRenderers[i].bounds;
				positions[i] = bounds.center;
				hilbertBounds.Encapsulate(bounds);
			}
			float maxDim = math.max(math.max(hilbertBounds.extents.x, hilbertBounds.extents.y), hilbertBounds.extents.z);
			hilbertBounds.extents = new Vector3(maxDim, maxDim, maxDim);
			NativeArray<UInt64> hilbertIdxs = HilbertIndex.GetHilbertIndices(positions, hilbertBounds, Allocator.TempJob);
			return hilbertIdxs;
		}


	}
}
