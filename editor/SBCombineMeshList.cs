using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
using static UnityEngine.Mesh;
namespace SLZ.SLZEditorTools
{
	
	public struct RendererData
	{
		public Mesh mesh;
		public MeshFilter meshFilter;
		public MeshRenderer meshRenderer;
		public Transform rendererTransform;
		public uint indexCount;
	}
	public class SBCombineMeshList
	{
		const int NUM_VTX_CHANNELS = 12;

		public VtxFormats[] vertexFormatCompression;
		public struct VertexStreamCompression
		{
			public readonly bool position;
			public readonly bool normal;
			public readonly bool tangent;
			public readonly bool color;
			public readonly int uvs;

			public VertexStreamCompression(bool position, bool normal, bool tangent, bool color, int uvs)
			{
				this.position = position;
				this.normal = normal;
				this.tangent = tangent;
				this.color = color;
				this.uvs = uvs;
			}
		}

		/// <summary>
		/// Bit-packed information about a single channel in a vertex buffer. Maps to a single UInt, needs to be kept in sync with the compute shader
		/// used for transfering and converting vertex buffers of each mesh into the combined mesh.
		/// </summary>

		[StructLayout(LayoutKind.Explicit, Size = 4)]
		public struct PackedChannel
		{
			[FieldOffset(0)]
			public UInt32 packedData;
			[FieldOffset(0)]
			public byte dimension;
			[FieldOffset(1)]
			public byte format;
			[FieldOffset(2)]
			public byte offset;
			[FieldOffset(3)]
			public byte unused;

			public override string ToString()
			{
				return string.Format("Dimension {0}, Format {1}, Offset {2}, Packed: 0x{3}", (int)dimension, (int)format, (int)offset, packedData.ToString("X8"));
			}
		}

		/// <summary>
		/// Supported vertex formats. This is hard-coded into the compute shader used for combining, so don't just go adding formats to this list!
		/// Only commonly used floating point formats are here. There's no logical way to choose a format for the combined mesh's channels if the 
		/// channel is integer on one mesh and floating point on another. Which format do you choose? Cast int to float or treat the bytes of the 
		/// int as a float? You can losslessly store the bytes of an int32 in a float, but it'll get garbled if the output channel is compressed
		/// to half. Also what if the int is less than 32 bits? You can't put a short or char into a half or unorm, as those will get converted to
		/// float by the GPU. I'm not supporting 16 bit normalized formats to cut down on shader complexity. Just use half precision instead.
		/// These need to be in order of increasing precision such that each format can be safely contained in the next format.
		/// </summary>
		public enum VtxFormats : byte
		{
			Invalid = 0,
			UNorm8 = 1,
			SNorm8 = 2,
			Float16 = 3,
			Float32 = 4,
		}
		
		public SBCombineMeshList(bool useProjectVtxCompression = true)
		{
			SerializedObject projectSettings = GetProjectSettingsAsset();
			vertexFormatCompression = new VtxFormats[NUM_VTX_CHANNELS];
			if (useProjectVtxCompression)
			{
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

				vertexFormatCompression[0] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Position) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[1] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Normal) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[2] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Tangent) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[3] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.Color) == 0 ? VtxFormats.Float32 : VtxFormats.UNorm8;
				vertexFormatCompression[4] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord0) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[5] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord1) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[6] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord2) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				vertexFormatCompression[7] = (vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord3) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
				for (int i = 8; i < vertexFormatCompression.Length; i++)
				{
					vertexFormatCompression[i] = VtxFormats.Float32;
				}
			}
			else
			{
				for (int i = 0; i < vertexFormatCompression.Length; i++)
				{
					vertexFormatCompression[i] = VtxFormats.Float32;
				}
			}

		}

		SerializedObject GetProjectSettingsAsset()
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


		public void CombineMeshes(RendererData[] sortedRenderers)
		{
			List<Mesh> uniqueMeshList;
			int[] renderer2Mesh;
			MeshDataArray uniqueMeshData;

			NativeArray<byte> rendererScaleSign = GetRendererScaleSign(sortedRenderers);

			ParallelGetUniqueMeshes(sortedRenderers, out uniqueMeshList, out uniqueMeshData, out renderer2Mesh);
			NativeArray<PackedChannel> uniqueMeshLayout;
			NativeArray<byte> invalidMeshes;
			ParallelGetMeshLayout(uniqueMeshData, out uniqueMeshLayout, out invalidMeshes);

			string DebugMessage = "Single Meshes\n";
			for (int i = 0; i < uniqueMeshList.Count;i++)
			{
				DebugMessage += string.Format("{0}: {1}\n", uniqueMeshList[i].name, VtxStructToString(uniqueMeshLayout, i*12));
			}

			ushort[] renderer2CMeshIdx;
			List<int2> cMeshIdxRange;
			GetCombinedMeshBins16(sortedRenderers, renderer2Mesh, invalidMeshes, out renderer2CMeshIdx, out cMeshIdxRange);

			NativeArray<PackedChannel>[] combinedMeshLayouts = new NativeArray<PackedChannel>[cMeshIdxRange.Count];
			//DebugMessage += string.Format("Combined meshes: {0}\n", cMeshIdxRange.Count);
			
			for (int i = 0; i < cMeshIdxRange.Count; i++)
			{
				combinedMeshLayouts[i] = GetCombinedMeshLayout(sortedRenderers, ref uniqueMeshLayout, ref invalidMeshes, renderer2Mesh, cMeshIdxRange[i].x, cMeshIdxRange[i].y);
				//DebugMessage += string.Format("Combined mesh {0}: {1}\n", i, VtxStructToString(combinedMeshLayouts[i], 0));
				Mesh CombinedMesh = GetCombinedMeshObject(sortedRenderers, uniqueMeshList, cMeshIdxRange[i], renderer2Mesh, ref invalidMeshes, ref combinedMeshLayouts[i], ref rendererScaleSign);
				CombinedMesh.name = "Combined Mesh (" + i + ")";
				ComputeCopyMeshes(ref uniqueMeshLayout, ref combinedMeshLayouts[i], CombinedMesh, sortedRenderers, cMeshIdxRange[i], renderer2Mesh, ref invalidMeshes, uniqueMeshList);
				AssignSBCombinedMesh(CombinedMesh, sortedRenderers, renderer2Mesh, ref invalidMeshes, cMeshIdxRange[i]);
				//combinedMeshLayouts[i].Dispose();
			}
			//Debug.Log(DebugMessage);

			
			GameObject test = new GameObject();
			test.name = "SB Test Object";
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


		/// <summary>
		/// Takes an array of pre-sorted renderers, and bins them into chunks of <65535 verticies.
		/// This is necessary for using ushort index buffers, and assumes all meshes in the list
		/// are using 16 bit index buffers already
		/// </summary>
		/// <param name="sortedRenderers"></param>
		/// <param name="renderer2CMeshIdx"></param>
		/// <param name="cMeshIdxRange"></param>
		void GetCombinedMeshBins16(RendererData[] sortedRenderers, int[] renderer2Mesh, NativeArray<byte> invalidMeshes, out ushort[] renderer2CMeshIdx, out List<int2> cMeshIdxRange)
		{
			// bin the sorted renderers into groups containing less than 2^16 verticies
			renderer2CMeshIdx = new ushort[sortedRenderers.Length];

			int vertexCount = 0;
			ushort currentMeshIdx = 0;
			int meshGroupBeginIdx = 0;
			//cMeshIdxRange = new List<int2>();
			cMeshIdxRange = new List<int2>();
			for (int rIdx = 0; rIdx < sortedRenderers.Length; rIdx++)
			{
				if (invalidMeshes[renderer2Mesh[rIdx]] == 0)
				{
					int meshVertexCount = sortedRenderers[rIdx].mesh.vertexCount;
					vertexCount += meshVertexCount;
					if (vertexCount >= 0xffff)
					{
						cMeshIdxRange.Add(new int2(meshGroupBeginIdx, rIdx));
						currentMeshIdx++;
						meshGroupBeginIdx = rIdx;
						vertexCount = meshVertexCount;
					}
					renderer2CMeshIdx[rIdx] = currentMeshIdx;
				}
				else
				{
					renderer2CMeshIdx[rIdx] = (ushort)0xffffu;
				}
			}
			if (meshGroupBeginIdx == 0 || meshGroupBeginIdx != (sortedRenderers.Length - 1))
			{
				cMeshIdxRange.Add(new int2(meshGroupBeginIdx, sortedRenderers.Length));
			}
		}

		/// <summary>
		/// Get a list of unique meshes for the given renderers, and a mapping from the index of the renderer to the index of the unique mesh.
		/// For small lists of meshes where doing a job would take longer
		/// </summary>
		/// <param name="renderers">Array of renderer structs from which to generate the list of unique meshes</param>
		/// <param name="meshList">output list of unique meshes</param>
		/// <param name="renderer2Mesh">Array that maps each index of the renderer array to an index in the unique mesh list</param>
		void SerialGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out int[] renderer2Mesh)
		{
			meshList = new List<Mesh>(renderers.Length);
			Debug.Log("Num Renderers: " +  renderers.Length);
			Dictionary<Mesh, int> meshListIndex = new Dictionary<Mesh, int>(renderers.Length);
			renderer2Mesh = new int[renderers.Length];
			for (int i = 0; i < renderers.Length; i++)
			{
				Mesh m = renderers[i].mesh;
				int index;
				if (!meshListIndex.TryGetValue(m, out index))
				{
					index = meshList.Count;
					meshList.Add(m);
					meshListIndex.Add(m, index);
				}
				renderer2Mesh[i] = index;
			}
		}

		/// <summary>
		/// Get a list of unique meshes for the given renderers, and a mapping from the index of the renderer to the index of the unique mesh.
		/// Does the serial version, but also generates the MeshDataArray needed for jobs.
		/// </summary>
		/// <param name="renderers">Array of renderer structs from which to generate the list of unique meshes</param>
		/// <param name="meshList">output list of unique meshes</param>
		/// <param name="meshDataArray">output array of readonly meshdata structs for use by the jobs system</param>
		/// <param name="renderer2Mesh">Array that maps each index of the renderer array to an index in the unique mesh list</param>
		void ParallelGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out Mesh.MeshDataArray meshDataArray, out int[] renderer2Mesh)
		{
			SerialGetUniqueMeshes(renderers, out meshList, out renderer2Mesh);
			meshDataArray = MeshUtility.AcquireReadOnlyMeshData(meshList);
		}



		static readonly byte[] VtxFmtToBytes = new byte[5] { 0, 1, 1, 2, 4 };

		/// <summary>
		/// Gets packed channel information for each of the 12 possible channels of the vertex struct for each mesh in the list.
		/// </summary>
		/// <param name="meshList">List of meshes to get the channel information of</param>
		/// <param name="meshChannels">output array of packed channel information. The index of each element divided by 12 is the index of the mesh it corresponds to</param>
		/// <param name="invalidMeshes">outupt array of flags that correspond to each mesh in the mesh list. If the value is 1, the mesh has incompatible channel formats and can't be combined</param>
		void SerialGetMeshLayout(List<Mesh> meshList, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
		{
			int numMeshes = meshList.Count;
			meshChannels = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS * numMeshes, Allocator.Temp);
			invalidMeshes = new NativeArray<byte>(numMeshes, Allocator.Temp);
			Span<byte> vtxFmtLUT = stackalloc byte[NUM_VTX_CHANNELS];
			for (int i = 0; i < NUM_VTX_CHANNELS; i++) vtxFmtLUT[i] = (byte)VtxFormats.Invalid; // 0 represents an invalid format
			vtxFmtLUT[(int)VertexAttributeFormat.Float32] = (byte)VtxFormats.Float32;
			vtxFmtLUT[(int)VertexAttributeFormat.Float16] = (byte)VtxFormats.Float16;
			vtxFmtLUT[(int)VertexAttributeFormat.SNorm8] = (byte)VtxFormats.SNorm8;
			vtxFmtLUT[(int)VertexAttributeFormat.UNorm8] = (byte)VtxFormats.UNorm8;
			for (int i = 0; i < numMeshes; i++)
			{
				int baseIdx = NUM_VTX_CHANNELS * i;
				bool meshIsInvalid = false;
				for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
				{
					bool hasChannel = meshList[i].HasVertexAttribute((VertexAttribute)channel);
					if (hasChannel)
					{
						byte channelFormat = vtxFmtLUT[(int)meshList[i].GetVertexAttributeFormat((VertexAttribute)channel)];
						bool invalid = (channelFormat == (int)VtxFormats.Invalid);
						if (invalid) Debug.LogError(meshList[i].name + " has invalid channel format for channel " + channel);
						meshIsInvalid = meshIsInvalid || invalid;

						meshChannels[baseIdx + channel] = new PackedChannel
						{
							dimension = (byte)meshList[i].GetVertexAttributeDimension((VertexAttribute)channel),
							format = channelFormat,
							offset = (byte)meshList[i].GetVertexAttributeOffset((VertexAttribute)channel)
						};
					}
				}
				invalidMeshes[i] = meshIsInvalid ? (byte)1 : (byte)0;
			}
		}


		/// <summary>
		/// Gets packed channel information for each of the 12 possible channels of the vertex struct for each mesh in the input list,
		/// doing so using parallel jobs.
		/// </summary>
		/// <param name="meshDataArray">Array of mesh data to get the channel information of</param>
		/// <param name="meshChannels">output array of packed channel information. The index of each element divided by 12 is the index of the mesh it corresponds to</param>
		/// <param name="invalidMeshes">outupt array of flags that correspond to each mesh in the mesh list. If the value is 1, the mesh has incompatible channel formats and can't be combined</param>
		void ParallelGetMeshLayout(Mesh.MeshDataArray meshDataArray, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
		{
			meshChannels = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS * meshDataArray.Length, Allocator.Persistent);
			invalidMeshes = new NativeArray<byte>(meshDataArray.Length, Allocator.TempJob);
			NativeArray<byte> vtxFmtLUT = new NativeArray<byte>(NUM_VTX_CHANNELS, Allocator.TempJob);
			for (int i = 0; i < NUM_VTX_CHANNELS; i++) vtxFmtLUT[i] = (byte)VtxFormats.Invalid; // 255 represents an invalid format
			vtxFmtLUT[(int)VertexAttributeFormat.Float32] = (byte)VtxFormats.Float32;
			vtxFmtLUT[(int)VertexAttributeFormat.Float16] = (byte)VtxFormats.Float16;
			vtxFmtLUT[(int)VertexAttributeFormat.SNorm8] = (byte)VtxFormats.SNorm8;
			vtxFmtLUT[(int)VertexAttributeFormat.UNorm8] = (byte)VtxFormats.UNorm8;

			GetMeshLayoutJob getLayout = new GetMeshLayoutJob { _meshChannels = meshChannels, _invalidMeshes = invalidMeshes, _vtxFmtLUT = vtxFmtLUT, _meshData = meshDataArray};
			JobHandle layoutHandle =  getLayout.Schedule(meshDataArray.Length, 16);
			layoutHandle.Complete();
   
			vtxFmtLUT.Dispose();
		}




		[BurstCompile]
		struct GetMeshLayoutJob : IJobParallelFor
		{
			[NativeDisableParallelForRestriction]
			[WriteOnly]
			public NativeArray<PackedChannel> _meshChannels;
			[WriteOnly]
			public NativeArray<byte> _invalidMeshes;
			[ReadOnly]
			public NativeArray<byte> _vtxFmtLUT;
			[ReadOnly]
			public Mesh.MeshDataArray _meshData;

			public void Execute(int i)
			{
				int baseIdx = NUM_VTX_CHANNELS * i;
				MeshData data = _meshData[i];
				bool meshIsInvalid = !data.HasVertexAttribute(VertexAttribute.Position) || data.indexFormat == IndexFormat.UInt32 || data.vertexBufferCount > 1;
				
				for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
				{
					bool hasChannel = data.HasVertexAttribute((VertexAttribute)channel);
					if (hasChannel)
					{
						byte channelFormat = _vtxFmtLUT[(int)data.GetVertexAttributeFormat((VertexAttribute)channel)];
						meshIsInvalid = meshIsInvalid || (channelFormat == (int)VtxFormats.Invalid);
						_meshChannels[baseIdx + channel] = new PackedChannel
						{
							dimension = (byte)data.GetVertexAttributeDimension((VertexAttribute)channel),
							format = channelFormat,
							offset = (byte)data.GetVertexAttributeOffset((VertexAttribute)channel)
						};
					}
				}
				_invalidMeshes[i] = meshIsInvalid ? (byte)1 : (byte)0;
			}
		}


		struct GetRendererNegativeScale : IJobParallelFor
		{
			[WriteOnly]
			public NativeArray<byte> isNegativeScale;
			[ReadOnly]
			public NativeArray<float3x3> object2World; 

			public void Execute(int i)
			{
				float3x3 Object2WorldNoTranslation = object2World[i];
				float determinant = math.determinant(Object2WorldNoTranslation);
				isNegativeScale[i] = determinant > 0 ? (byte)1 : (byte)0;
			}
		}

		NativeArray<byte> GetRendererScaleSign(RendererData[] rd)
		{
			NativeArray<byte> scaleSign = new NativeArray<byte>(rd.Length, Allocator.TempJob);
			NativeArray<float3x3> object2World = new NativeArray<float3x3>(rd.Length, Allocator.TempJob);
			for (int i = 0; i < rd.Length; i++) 
			{
				object2World[i] = (float3x3)((float4x4)rd[i].rendererTransform.localToWorldMatrix);
			}
			GetRendererNegativeScale scaleJob = new GetRendererNegativeScale() { isNegativeScale = scaleSign, object2World = object2World };
			JobHandle scaleJh = scaleJob.Schedule(rd.Length, 16);
			scaleJh.Complete();
			object2World.Dispose();
			return scaleSign;
		}

		struct UniqueMeshData
		{
			public int[] renderer2Mesh;
			public NativeArray<PackedChannel> meshChannels;
			public NativeArray<byte> invalidMeshes;
		}
		NativeArray<PackedChannel> GetCombinedMeshLayout(
			RendererData[] renderers,
			ref NativeArray<PackedChannel> meshChannels,
			ref NativeArray<byte> invalidMeshes,
			int[] renderer2Mesh,
			int startIdx, int endIdx)
		{

			// Create list of unique meshes and array of pointers from the sortedRenderers to the unique meshes
			int combinedCount = endIdx - startIdx;
			List<int> meshIndex = new List<int>(combinedCount);
			HashSet<int> uniqueMeshSet = new HashSet<int>(combinedCount);
			bool isLightmapped = false;
			for (int i = startIdx; i < endIdx; i++)
			{
				int index = renderer2Mesh[i];
				//Debug.Log("Renderer2Mesh: " + i + ":" + index);
				if (!uniqueMeshSet.Contains(index) && invalidMeshes[index] == 0)
				{
					meshIndex.Add(index);
					uniqueMeshSet.Add(index);

					// Determine if the combined mesh will be lightmapped.
					// Sometimes, people will use UV0 as the lightmap UV. This doesn't work with static batching as the lightmap scale/offset
					// gets baked into the lightmap UV, and UV0 is normally compressed to 16 bit which isn't enough for lightmaps.
					// Therefore, forcibly add UV1 to lightmapped combined meshes even if none of the input meshes have it.
					MeshRenderer mr = renderers[i].meshRenderer;
					isLightmapped = isLightmapped || mr.lightmapIndex < 0xFFFE;
				}
			}

			int meshIdxCount = meshIndex.Count;
			Debug.Log("Unique Mesh count in combined mesh: " +  meshIdxCount);
			NativeArray<PackedChannel> combinedFormat = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS, Allocator.TempJob);
			Span<int> minTypeLUT = stackalloc int[] { 1, 4, 4, 2, 1 };
			for (int mesh = 0; mesh < meshIdxCount; mesh++)
			{
				int meshPtr = meshIndex[mesh] * NUM_VTX_CHANNELS;
				for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
				{
					PackedChannel a = combinedFormat[channel];
					PackedChannel b = meshChannels[meshPtr + channel];
					int largestFmt = math.max((int)a.format, (int)b.format);
					largestFmt = math.min(largestFmt, (int)vertexFormatCompression[channel]);

					int maxDim = math.max((int)a.dimension, (int)b.dimension);
					int roundDim = minTypeLUT[largestFmt];
					maxDim = ((maxDim + roundDim - 1) / roundDim) * roundDim;
					combinedFormat[channel] = new PackedChannel { packedData = (uint)(maxDim | (largestFmt << 8)) };
				}
			}
			for (int renderer = startIdx; renderer < endIdx; renderer++)
			{
				int meshIdx = renderer2Mesh[renderer];
				if (invalidMeshes[meshIdx] == 0)
				{
					
				}
			}
			// Force add a lightmap UV1 that is a duplicate of UV0 if the mesh is lightmapped but the inputs don't have UV1's
			if (isLightmapped && combinedFormat[5].dimension == 0)
			{
				combinedFormat[5] = new PackedChannel() { dimension = 2, format = (byte)VtxFormats.Float32 };
			}

			uint cumulativeOffset = 0;
			for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
			{
				combinedFormat[channel] = new PackedChannel { packedData = combinedFormat[channel].packedData | (cumulativeOffset << 16) };
				cumulativeOffset = (cumulativeOffset + (uint)VtxFmtToBytes[combinedFormat[channel].format] * combinedFormat[channel].dimension);
			}
			return combinedFormat;
		}

		struct CombinedMeshSmInfo
		{
			public int[] rendererIdx;
			public int[] submeshStart;
			public int[] submeshCount;
		}
		Mesh GetCombinedMeshObject(RendererData[] rd, List<Mesh> uniqueMeshList, int2 rendererRange, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, ref NativeArray<PackedChannel> packedChannels, ref NativeArray<byte> rendererScaleSign)
		{
			// Get the total number of vertices, submeshes, and valid renderers that make up this combined mesh
			int vertexCount = 0;
			int submeshCount = 0;
			int numRenderers = 0;
			int[] validRendererIdx = new int[rendererRange.y - rendererRange.x];
			// Iterate once over the range of renderers, counting the number of valid renderers, and their total verticies and submeshes.
			for (int i = rendererRange.x; i < rendererRange.y; i++)
			{

				if (invalidMeshes[renderer2Mesh[i]] == 0)
				{
					Mesh tempMesh = rd[i].mesh;
					vertexCount += rd[i].mesh.vertexCount;
					submeshCount += rd[i].mesh.subMeshCount;
					validRendererIdx[numRenderers] = i;
					numRenderers++;
				}
			}
			//Debug.Log("Combined mesh vertex count " + vertexCount);
			Span<VertexAttributeFormat> formatLUT = stackalloc VertexAttributeFormat[5] { 
				VertexAttributeFormat.UNorm8, 
				VertexAttributeFormat.UNorm8,
				VertexAttributeFormat.SNorm8,
				VertexAttributeFormat.Float16,
				VertexAttributeFormat.Float32,
			};
			List<VertexAttributeDescriptor> vertexAttributes = new List<VertexAttributeDescriptor>(NUM_VTX_CHANNELS);
			for (int i = 0; i < NUM_VTX_CHANNELS; i++)
			{
				if (packedChannels[i].dimension != 0)
				{
					vertexAttributes.Add(new VertexAttributeDescriptor((VertexAttribute)i, formatLUT[packedChannels[i].format], packedChannels[i].dimension, 0));
				}
			}
			Mesh combinedMesh = new Mesh();
			combinedMesh.SetVertexBufferParams(vertexCount, vertexAttributes.ToArray());


			SubMeshDescriptor[] subMeshDescriptors = new SubMeshDescriptor[submeshCount];
			CombinedMeshSmInfo combinedSmInfo = new CombinedMeshSmInfo()
			{
				rendererIdx = new int[numRenderers],
				submeshStart = new int[numRenderers],
				submeshCount = new int[numRenderers],
			};
			int meshPointer = 0;
			int smPointer = 0;
			int idxCount = 0;
			int vtxPointer = 0;

			bool initializeBounds = true;
			Bounds totalBounds = new Bounds();

			//int reverseWindingPtr = 0;
			//NativeArray<int2> ReverseWindingIdxRanges = new NativeArray<int2>(submeshCount, Allocator.TempJob);

			bool[] negativeScale = new bool[numRenderers];

			// Iterate again over the renderers, this time getting
			for (int i = 0; i < numRenderers; i++)
			{
				int rIdx = validRendererIdx[i];
				int meshIdx = renderer2Mesh[rIdx];

				int smCount = rd[rIdx].mesh.subMeshCount;
				int firstSubMesh = smPointer;
				Bounds bounds = rd[rIdx].meshRenderer.bounds;
				if (initializeBounds)
				{
					totalBounds = bounds;
					initializeBounds = false;
				}
				totalBounds.Encapsulate(bounds);
				Matrix4x4 Object2World = rd[rIdx].rendererTransform.localToWorldMatrix;

				for (int sm = 0; sm < smCount; sm++)
				{
					SubMeshDescriptor smd = rd[rIdx].mesh.GetSubMesh(sm);
					SubMeshDescriptor smd2 = new SubMeshDescriptor()
					{
						baseVertex = 0,
						firstVertex = smd.firstVertex + vtxPointer,
						bounds = smd.bounds,
						indexCount = smd.indexCount,
						indexStart = idxCount,
						vertexCount = smd.vertexCount,
						topology = smd.topology,

					};
					Debug.Log("Submesh " + smPointer + " index start: " + smd2.indexStart + " bounds: " + smd2.bounds);
					idxCount += smd.indexCount;
					subMeshDescriptors[smPointer] = smd2;
				   
					smPointer++;
				}
				combinedSmInfo.rendererIdx[meshPointer] = rIdx;
				combinedSmInfo.submeshStart[meshPointer] = firstSubMesh;
				combinedSmInfo.submeshCount[meshPointer] = smPointer - firstSubMesh;
				meshPointer++;
				vtxPointer += rd[rIdx].mesh.vertexCount;
			}
			combinedMesh.SetIndexBufferParams(idxCount, IndexFormat.UInt16);
			combinedMesh.bounds = totalBounds;

			NativeArray<ushort> indexBuffer = new NativeArray<ushort>(idxCount, Allocator.TempJob);
			NativeArray<int3> indexStartCountOffset = new NativeArray<int3>(numRenderers, Allocator.TempJob);

			int idxPointer = 0;
			List<ushort> indices = new List<ushort>();

			for (int i = 0; i < numRenderers; i++)
			{
				int rendererIdx = combinedSmInfo.rendererIdx[i];

				int meshIdx = renderer2Mesh[combinedSmInfo.rendererIdx[i]];
				Mesh tmesh = uniqueMeshList[meshIdx];
				int firstSubMesh = combinedSmInfo.submeshStart[i];
				int totalIdxCount = 0;
			   
				for (int sm = 0; sm < combinedSmInfo.submeshCount[i]; sm++)
				{
					tmesh.GetIndices(indices, sm);
					int numIdx = (int)tmesh.GetIndexCount(sm);
					NativeArray<ushort>.Copy(indices.ToArray(), 0, indexBuffer, idxPointer, numIdx);
					
					idxPointer += numIdx;
					totalIdxCount += numIdx;
				}
				indexStartCountOffset[i] = new int3(subMeshDescriptors[firstSubMesh].indexStart, totalIdxCount, subMeshDescriptors[firstSubMesh].firstVertex);
			}
			Debug.Log("Reindex job start, count, offset: " + indexStartCountOffset[indexStartCountOffset.Length - 1].ToString());
			OffsetIndexBuffer16 offsetIdxJob = new OffsetIndexBuffer16 { indices = indexBuffer, indexStartCountOffset = indexStartCountOffset };
			JobHandle jobHandle = offsetIdxJob.Schedule(numRenderers, 1);
			jobHandle.Complete();
			combinedMesh.SetIndexBufferData<ushort>(indexBuffer, 0, 0, idxCount, MeshUpdateFlags.DontRecalculateBounds);
			combinedMesh.SetSubMeshes(subMeshDescriptors, MeshUpdateFlags.DontRecalculateBounds);


			indexBuffer.Dispose();
			indexStartCountOffset.Dispose();
			
			return combinedMesh;
		}

		[BurstCompile]
		struct OffsetIndexBuffer16 : IJobParallelFor
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<ushort> indices;
			[ReadOnly]
			public NativeArray<int3> indexStartCountOffset;

			public void Execute(int i)
			{
				int3 idxDat = indexStartCountOffset[i];
				int idxStart = idxDat.x;
				int idxEnd = idxStart + idxDat.y; 
				ushort offset = (ushort)idxDat.z;
				for (int idx = idxStart; idx < idxEnd; idx++)
				{
					indices[idx] += offset;
				}
			}
		}

		[BurstCompile]
		struct RewindIndexBuffer16 : IJobParallelFor
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<ushort> indices;
			[ReadOnly]
			public NativeArray<int2> indexStartCount;
			[ReadOnly]
			public NativeArray<byte> isQuadTopology;

			public void Execute(int i)
			{
				bool isQuad = isQuadTopology[i] == 1;
				int indexStart = indexStartCount[i].x;
				int indexEnd = indexStartCount[i].y + indexStart;
				if (isQuad)
				{
					for (int idx = indexStart; idx < indexEnd; idx += 4)
					{
						ushort temp = indices[idx];
						indices[idx] = indices[idx + 3];
						indices[idx + 3] = temp;
						temp = indices[idx + 1];
						indices[idx + 1] = indices[idx + 2];
						indices[idx + 2] = temp;
					}
				}
				else
				{
					for (int idx = indexStart; idx < indexEnd; idx += 3)
					{
						ushort temp = indices[idx];
						indices[idx] = indices[idx + 2];
						indices[idx + 2] = temp;
					}
				}
			}
		}

		const string transferVtxGUID = "5bae5a4c97f51964dbc10d3398312270";
		static ComputeShader transferVtxBufferCompute;
		static int propMeshInBuffer = Shader.PropertyToID("MeshInBuffer");
		static int propMeshOutBuffer = Shader.PropertyToID("MeshOutBuffer");
		static int propVertIn = Shader.PropertyToID("vertIn");
		static int propVertOut = Shader.PropertyToID("vertOut");

		const int meshInBufferSize = 208;
		struct MeshInBuffer
		{
			public Matrix4x4 ObjectToWorld; // 4x4x4 = 64 bytes
			public Matrix4x4 WorldToObject; // 128
			public float4 lightmapScaleOffset; // 144
			public int4 offset_strideIn_TanSign; // 160
			// + float 4x3 (48) = 208 bytes
		}
		const int meshOutBufferSize = 64;

		void ComputeCopyMeshes(ref NativeArray<PackedChannel> meshPackedChannels, ref NativeArray<PackedChannel> combinedPackedChannels, Mesh combinedMesh,
			RendererData[] rd, int2 rendererRange, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, List<Mesh> meshList)
		{
			combinedMesh.UploadMeshData(false);
			ComputeShader meshCopy;
			if (transferVtxBufferCompute == null)
			{
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
			}

			meshCopy = transferVtxBufferCompute;
			ComputeBuffer meshInSettings = new ComputeBuffer(meshInBufferSize / 4, 4, ComputeBufferType.Constant);
			ComputeBuffer meshOutSettings = new ComputeBuffer(meshOutBufferSize / 4, 4, ComputeBufferType.Constant);
			List<MeshInBuffer> meshInBuffer = new List<MeshInBuffer>(1);
			meshInBuffer.Add(new MeshInBuffer());
			List<int4> meshOutBuffer = new List<int4>(1);
			int combinedStride = combinedMesh.GetVertexBufferStride(0);
			meshOutBuffer.Add(new int4(combinedStride, 0, 0, 0));
			Debug.Log("Combined Mesh Vertex Count = " + combinedMesh.vertexCount);
			Debug.Log("Combined Mesh Index Count = " + combinedMesh.GetIndexCount(0));
			Debug.Log("Combined Mesh Stride = " + combinedStride);
			GraphicsBuffer combinedMeshBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, combinedMesh.vertexCount, combinedStride);
			CommandBuffer cmd = new CommandBuffer();
			cmd.SetComputeBufferParam(meshCopy, 0, propVertOut, combinedMeshBuffer);
			cmd.SetBufferData(meshOutSettings, meshOutBuffer, 0, 0, 1);
			cmd.SetBufferData(meshOutSettings, combinedPackedChannels, 0, 4, NUM_VTX_CHANNELS);
			cmd.SetComputeConstantBufferParam(meshCopy, propMeshOutBuffer, meshOutSettings, 0, meshOutBufferSize);
			int combinedMeshCopyIndex = 0;
			int numMeshesCopied = 0;
			bool hasLightmap = combinedPackedChannels[5].dimension > 0;

			for (int renderer = rendererRange.x; renderer < rendererRange.y; renderer++)
			{

				int meshIdx = renderer2Mesh[renderer];
				if (invalidMeshes[meshIdx] == 0)
				{
					MeshRenderer mr = rd[renderer].meshRenderer;

					Matrix4x4 Object2World = rd[renderer].rendererTransform.localToWorldMatrix;
					Matrix4x4 World2Object = rd[renderer].rendererTransform.worldToLocalMatrix;
					int stride = meshList[meshIdx].GetVertexBufferStride(0);
					

					Debug.Log("single Mesh Stride = " + stride);
					int tanSign = (int)math.sign(
						Object2World.m00 * (Object2World.m11 * Object2World.m22 - Object2World.m12 * Object2World.m21) +
						Object2World.m01 * (Object2World.m10 * Object2World.m22 - Object2World.m12 * Object2World.m20) +
						Object2World.m02 * (Object2World.m10 * Object2World.m21 - Object2World.m11 * Object2World.m20)
						);
					meshInBuffer[0] = new MeshInBuffer { 
						ObjectToWorld = Object2World, 
						WorldToObject = World2Object, 
						lightmapScaleOffset = new float4(mr.lightmapScaleOffset), 
						offset_strideIn_TanSign = new int4(combinedMeshCopyIndex, stride, tanSign, 0) };
					combinedMeshCopyIndex += combinedStride * meshList[meshIdx].vertexCount;
					cmd.SetBufferData(meshInSettings, meshInBuffer, 0, 0, 1);

					// Handle lightmapped meshes where uv0 is being used as the lightmap UV. Set UV1's packed data to be UV0's so it just copies UV0 to UV1
					if (hasLightmap && meshPackedChannels[NUM_VTX_CHANNELS * meshIdx + 5].dimension == 0)
					{
						PackedChannel[] meshPackedChannels2 = new PackedChannel[NUM_VTX_CHANNELS];
						NativeArray<PackedChannel>.Copy(meshPackedChannels, NUM_VTX_CHANNELS * meshIdx, meshPackedChannels2, 0, 12);
						meshPackedChannels2[5] = meshPackedChannels2[4];
						cmd.SetBufferData(meshInSettings, meshPackedChannels2, 0, 40, NUM_VTX_CHANNELS);
					}
					else
					{
						cmd.SetBufferData(meshInSettings, meshPackedChannels, NUM_VTX_CHANNELS * meshIdx, 40, NUM_VTX_CHANNELS);
					}
				  
					GraphicsBuffer singleMeshBuffer = meshList[meshIdx].GetVertexBuffer(0);
					cmd.SetComputeBufferParam(meshCopy, 0, propVertIn, singleMeshBuffer);
					cmd.SetComputeConstantBufferParam(meshCopy, propMeshInBuffer, meshInSettings, 0, meshInBufferSize);
					cmd.DispatchCompute(meshCopy, 0, (meshList[meshIdx].vertexCount + 31) / 32, 1, 1);
					numMeshesCopied++;
				}
			}
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Dispose();
			Debug.Log("Copied " + numMeshesCopied + " meshes");
			//NativeArray<int> bufferBytes1 = new NativeArray<int>(64 / 4, Allocator.Persistent);
			//AsyncGPUReadbackRequest request1 = AsyncGPUReadback.RequestIntoNativeArray<int>(ref bufferBytes1, meshOutSettings);
			//request1.forcePlayerLoopUpdate = true;
			//request1.WaitForCompletion();
			//string buffermsg = "Mesh Out Buffer: \n";
			//for (int i = 0; i < bufferBytes1.Length; i++)
			//{
			//	buffermsg += bufferBytes1[i] + ", ";
			//}
			//Debug.Log(buffermsg);
			//bufferBytes1.Dispose();

			meshInSettings.Dispose();
			meshOutSettings.Dispose();
			int numBytes = combinedMesh.GetVertexBufferStride(0) * combinedMesh.vertexCount;

			NativeArray<byte> bufferBytes = new NativeArray<byte>(numBytes, Allocator.Persistent);
			AsyncGPUReadbackRequest request = AsyncGPUReadback.RequestIntoNativeArray<byte>(ref bufferBytes, combinedMeshBuffer);
			request.forcePlayerLoopUpdate = true;
			request.WaitForCompletion();
			combinedMeshBuffer.Dispose();
			combinedMesh.SetVertexBufferData<byte>(bufferBytes, 0, 0, numBytes, 0, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
			//string message = "Vertex Data: \n";
			//int cstride = combinedMesh.GetVertexBufferStride(0);
			//byte[] tempArray = new byte[16];
			//for (int i = 0; i < combinedMesh.vertexCount; i++)
			//{
			//	
			//	NativeArray<byte>.Copy(bufferBytes, cstride * i, tempArray, 0, 12);
			//	Span<byte> posBytes = MemoryMarshal.CreateSpan<byte>(ref tempArray[0], 12);
			//	ReadOnlySpan<Vector3> pos = MemoryMarshal.Cast<byte, Vector3>(posBytes);
			//	message += string.Format("    Position: {0}, {1}, {2}\n", pos[0].x, pos[0].y, pos[0].z);
			//
			//
			//    NativeArray<byte>.Copy(bufferBytes, cstride * i + 12, tempArray, 0, 8);
			//    ReadOnlySpan<byte> normBytes = MemoryMarshal.CreateSpan<byte>(ref tempArray[0], 8);
			//    ReadOnlySpan<half4> norm = MemoryMarshal.Cast<byte, half4>(normBytes);
			//    message += string.Format("    Normal: {0}, {1}, {2}, {3}\n", norm[0].x, norm[0].y, norm[0].z, norm[0].w);
			//
			//    NativeArray<byte>.Copy(bufferBytes, cstride * i + 20, tempArray, 0, 8);
			//    ReadOnlySpan<byte> tanBytes = MemoryMarshal.CreateSpan<byte>(ref tempArray[0], 8);
			//    ReadOnlySpan<half4> tan = MemoryMarshal.Cast<byte, half4>(tanBytes);
			//    message += string.Format("    Tangent: {0}, {1}, {2}, {3}\n", tan[0].x, tan[0].y, tan[0].z, tan[0].w);
			//
			//    NativeArray<byte>.Copy(bufferBytes, cstride * i + 28, tempArray, 0, 4);
			//    ReadOnlySpan<byte> uv0Bytes = MemoryMarshal.CreateSpan<byte>(ref tempArray[0], 4);
			//    ReadOnlySpan<half2> uv0 = MemoryMarshal.Cast<byte, half2>(uv0Bytes);
			//    message += string.Format("    UV0: {0}, {1}\n", uv0[0].x, uv0[0].y);
			//
			//    //NativeArray<byte>.Copy(bufferBytes, cstride * i + 32, tempArray, 0, 8);
			//    //ReadOnlySpan<byte> uv1Bytes = MemoryMarshal.CreateSpan<byte>(ref tempArray[0], 8);
			//    //ReadOnlySpan<float2> uv1 = MemoryMarshal.Cast<byte, float2>(uv1Bytes);
			//    //message += string.Format("    UV1: {0}, {1}\n", uv1[0].x, uv1[0].y);
			//}
			//Debug.Log(message);
			bufferBytes.Dispose();
			combinedMesh.UploadMeshData(true);
			//AssetDatabase.CreateAsset(combinedMesh, "Assets/_TestCombinedObject.asset");

		}

		void AssignSBCombinedMesh(Mesh combinedMesh, RendererData[] rd, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, int2 rendererRange)
		{
			int submeshIdx = 0;
			for (int i = rendererRange.x; i < rendererRange.y; i++) 
			{ 
				if (invalidMeshes[renderer2Mesh[i]] == 0)
				{
					rd[i].meshFilter.sharedMesh = combinedMesh;
					SerializedObject so = new SerializedObject(rd[i].meshRenderer);
					
					SerializedProperty spFirst = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
					spFirst.intValue = submeshIdx;

					int submeshCount = rd[i].mesh.subMeshCount;
					SerializedProperty spCount = so.FindProperty("m_StaticBatchInfo.subMeshCount");
					spCount.intValue = submeshCount;

					so.ApplyModifiedProperties();
					GameObject go = rd[i].rendererTransform.gameObject;
					StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
					GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.BatchingStatic);
					submeshIdx += submeshCount;
				}
			}
		}

		public static string VtxStructToString(NativeArray<PackedChannel> packedChannels, int startIdx)
		{
			string outp = "";
			if (packedChannels[startIdx].dimension != 0) outp += string.Format("\n    Position: {0}\n", packedChannels[startIdx].ToString());
			if (packedChannels[startIdx + 1].dimension != 0) outp += string.Format("    Normal:   {0}\n", packedChannels[startIdx + 1].ToString());
			if (packedChannels[startIdx + 2].dimension != 0) outp += string.Format("    Tangent:  {0}\n", packedChannels[startIdx + 2].ToString());
			if (packedChannels[startIdx + 3].dimension != 0) outp += string.Format("    Color:    {0}\n", packedChannels[startIdx + 3].ToString());
			if (packedChannels[startIdx + 4].dimension != 0) outp += string.Format("    UV0:      {0}\n", packedChannels[startIdx + 4].ToString());
			if (packedChannels[startIdx + 5].dimension != 0) outp += string.Format("    UV1: {0}\n", packedChannels[startIdx + 5].ToString());
			if (packedChannels[startIdx + 6].dimension != 0) outp += string.Format("    UV2: {0}\n", packedChannels[startIdx + 6].ToString());
			if (packedChannels[startIdx + 7].dimension != 0) outp += string.Format("    UV3: {0}\n", packedChannels[startIdx + 7].ToString());
			if (packedChannels[startIdx + 8].dimension != 0) outp += string.Format("    UV4: {0}\n", packedChannels[startIdx + 8].ToString());
			if (packedChannels[startIdx + 9].dimension != 0) outp += string.Format("    UV5: {0}\n", packedChannels[startIdx + 9].ToString());
			if (packedChannels[startIdx + 10].dimension != 0) outp += string.Format("    UV6: {0}\n", packedChannels[startIdx + 10].ToString());
			if (packedChannels[startIdx + 11].dimension != 0) outp += string.Format("    UV7: {0}\n", packedChannels[startIdx + 11].ToString());
			return outp;
		}

	}
}