using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

using System.Runtime.InteropServices;
using static UnityEngine.Mesh;
using Unity.Profiling;
using System.Runtime.CompilerServices;
using UnityEditor.PackageManager.Requests;


namespace SLZ.CustomStaticBatching
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
		public const int NUM_VTX_CHANNELS = 12;

		public VtxFormats[] vertexFormatCompression;

		public ComputeShader transferVtxBufferCompute;

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



		public SBCombineMeshList(ComputeShader transferVtxComputeShader)
		{
			vertexFormatCompression = new VtxFormats[NUM_VTX_CHANNELS];
			for (int i = 0; i < vertexFormatCompression.Length; i++)
			{
				vertexFormatCompression[i] = VtxFormats.Float32;
			}
			transferVtxBufferCompute = transferVtxComputeShader;
		}


		/// <summary>
		/// Takes an array of pre-sorted renderers, and bins them into chunks of <65535 verticies.
		/// This is necessary for using ushort index buffers, and assumes all meshes in the list
		/// are using 16 bit index buffers already
		/// </summary>
		/// <param name="sortedRenderers"></param>
		/// <param name="renderer2CMeshIdx"></param>
		/// <param name="cMeshIdxRange"></param>
		public void GetCombinedMeshBins16(RendererData[] sortedRenderers, int[] renderer2Mesh, NativeArray<byte> invalidMeshes, out ushort[] renderer2CMeshIdx, out List<int2> cMeshIdxRange)
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
		public void SerialGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out int[] renderer2Mesh)
		{
			meshList = new List<Mesh>(renderers.Length);
			Debug.Log("Num Renderers: " + renderers.Length);
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
		public void ParallelGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out Mesh.MeshDataArray meshDataArray, out int[] renderer2Mesh)
		{
			SerialGetUniqueMeshes(renderers, out meshList, out renderer2Mesh);
#if UNITY_EDITOR
			meshDataArray = MeshUtility.AcquireReadOnlyMeshData(meshList);
#else
			meshDataArray = Mesh.AcquireReadOnlyMeshData(meshList);
#endif
		}



		static readonly byte[] VtxFmtToBytes = new byte[5] { 0, 1, 1, 2, 4 };

		/// <summary>
		/// Gets packed channel information for each of the 12 possible channels of the vertex struct for each mesh in the list.
		/// </summary>
		/// <param name="meshList">List of meshes to get the channel information of</param>
		/// <param name="meshChannels">output array of packed channel information. The index of each element divided by 12 is the index of the mesh it corresponds to</param>
		/// <param name="invalidMeshes">outupt array of flags that correspond to each mesh in the mesh list. If the value is 1, the mesh has incompatible channel formats and can't be combined</param>
		internal void SerialGetMeshLayout(List<Mesh> meshList, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
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
		internal void ParallelGetMeshLayout(Mesh.MeshDataArray meshDataArray, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
		{
			meshChannels = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS * meshDataArray.Length, Allocator.Persistent);
			invalidMeshes = new NativeArray<byte>(meshDataArray.Length, Allocator.TempJob);
			NativeArray<byte> vtxFmtLUT = new NativeArray<byte>(NUM_VTX_CHANNELS, Allocator.TempJob);
			for (int i = 0; i < NUM_VTX_CHANNELS; i++) vtxFmtLUT[i] = (byte)VtxFormats.Invalid; // 255 represents an invalid format
			vtxFmtLUT[(int)VertexAttributeFormat.Float32] = (byte)VtxFormats.Float32;
			vtxFmtLUT[(int)VertexAttributeFormat.Float16] = (byte)VtxFormats.Float16;
			vtxFmtLUT[(int)VertexAttributeFormat.SNorm8] = (byte)VtxFormats.SNorm8;
			vtxFmtLUT[(int)VertexAttributeFormat.UNorm8] = (byte)VtxFormats.UNorm8;

			GetMeshLayoutJob getLayout = new GetMeshLayoutJob { _meshChannels = meshChannels, _invalidMeshes = invalidMeshes, _vtxFmtLUT = vtxFmtLUT, _meshData = meshDataArray };
			JobHandle layoutHandle = getLayout.Schedule(meshDataArray.Length, 16);
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

		internal NativeArray<byte> GetRendererScaleSign(RendererData[] rd)
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
		internal NativeArray<PackedChannel> GetCombinedMeshLayout(
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
			bool isDynamicLightmapped = false;
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

					//#if UNITY_EDITOR
					//isLightmapped = isLightmapped || (GameObjectUtility.AreStaticEditorFlagsSet(mr.gameObject, StaticEditorFlags.ContributeGI) && mr.receiveGI == ReceiveGI.Lightmaps);
					//#else
					isLightmapped = isLightmapped || (mr.lightmapIndex < 0xFFFE && mr.lightmapIndex > 0);
					if (isDynamicLightmapped)
					{
						Debug.Log("Is Lightmapped " + mr.lightmapIndex);
					}
					//#endif
					isDynamicLightmapped = isDynamicLightmapped || (mr.realtimeLightmapIndex < 0xFFFE && mr.realtimeLightmapIndex > 0);
					if (isDynamicLightmapped)
					{
						Debug.Log("Is Dynamic Lightmapped " + mr.realtimeLightmapIndex);
					}
				}
			}

			int meshIdxCount = meshIndex.Count;
			//Debug.Log("Unique Mesh count in combined mesh: " +  meshIdxCount);
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
			// Add a lightmap UV1 if one or more of the input renderers are either static or dynamic lightmapped but none of the inputs have UV1's
			if ((isLightmapped || isDynamicLightmapped) && combinedFormat[5].dimension == 0)
			{
				combinedFormat[5] = new PackedChannel() { dimension = 2, format = (byte)vertexFormatCompression[5] };
			}
			// Add a dynamic lightmap UV2 if there are dynamic lightmapped renderers in the input, but none of the inputs have UV2's
			if (isDynamicLightmapped && combinedFormat[6].dimension == 0)
			{
				
				combinedFormat[6] = new PackedChannel() { dimension = 2, format = (byte)vertexFormatCompression[6] };
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
		internal Mesh GetCombinedMeshObject(RendererData[] rd, List<Mesh> uniqueMeshList, int2 rendererRange, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, ref NativeArray<PackedChannel> packedChannels, ref NativeArray<byte> rendererScaleSign)
		{
			// Get the total number of vertices, submeshes, and valid renderers that make up this combined mesh
			int vertexCount = 0;
			int submeshCount = 0;
			int rendererCount = 0;
			int[] validRendererIdx = new int[rendererRange.y - rendererRange.x];
			// Iterate once over the range of renderers, counting the number of valid renderers, and their total verticies and submeshes.
			for (int i = rendererRange.x; i < rendererRange.y; i++)
			{

				if (invalidMeshes[renderer2Mesh[i]] == 0)
				{
					Mesh tempMesh = rd[i].mesh;
					vertexCount += rd[i].mesh.vertexCount;
					submeshCount += rd[i].mesh.subMeshCount;
					validRendererIdx[rendererCount] = i;
					rendererCount++;
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
				rendererIdx = new int[rendererCount],
				submeshStart = new int[rendererCount],
				submeshCount = new int[rendererCount],
			};
			int meshPointer = 0;
			int smPointer = 0;
			int idxCount = 0;
			int vtxPointer = 0;

			bool initializeBounds = true;
			Bounds totalBounds = new Bounds();

			//int reverseWindingPtr = 0;
			//NativeArray<int2> ReverseWindingIdxRanges = new NativeArray<int2>(submeshCount, Allocator.TempJob);

			bool[] negativeScale = new bool[rendererCount];

			NativeArray<Bounds> submeshBounds = new NativeArray<Bounds>(submeshCount, Allocator.TempJob);
			NativeArray<float4x4> rendererObject2World = new NativeArray<float4x4>(rendererCount, Allocator.TempJob);
			NativeArray<ushort> submesh2Renderer = new NativeArray<ushort>(submeshCount, Allocator.TempJob);
			// Iterate again over the renderers, this time getting the submesh descriptors of all the meshes and calculating the bounds
			for (int i = 0; i < rendererCount; i++)
			{
				int rIdx = validRendererIdx[i];
				int meshIdx = renderer2Mesh[rIdx];

				int smCount = rd[rIdx].mesh.subMeshCount;
				int firstSubMesh = smPointer;
				Bounds bounds = rd[rIdx].meshRenderer.bounds;
				rendererObject2World[i] = rd[rIdx].rendererTransform.localToWorldMatrix;

				if (initializeBounds)
				{
					totalBounds = bounds;
					initializeBounds = false;
				}
				totalBounds.Encapsulate(bounds);


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

					submesh2Renderer[smPointer] = (ushort)i;
					submeshBounds[smPointer] = smd.bounds;
					//Debug.Log("Submesh " + smPointer + " index start: " + smd2.indexStart + " bounds: " + smd2.bounds);
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

			TransformSubmeshBounds transformSubmeshBounds = new TransformSubmeshBounds() { bounds = submeshBounds, obj2World = rendererObject2World, obj2WorldIdx = submesh2Renderer };
			JobHandle transformBoundsHandle = transformSubmeshBounds.Schedule(submeshCount, 16);
			transformBoundsHandle.Complete();

			for (int i = 0; i < submeshCount; i++)
			{

				subMeshDescriptors[i] = new SubMeshDescriptor()
				{
					baseVertex = subMeshDescriptors[i].baseVertex,
					firstVertex = subMeshDescriptors[i].firstVertex,
					bounds = submeshBounds[i],
					indexCount = subMeshDescriptors[i].indexCount,
					indexStart = subMeshDescriptors[i].indexStart,
					vertexCount = subMeshDescriptors[i].vertexCount,
					topology = subMeshDescriptors[i].topology,
				};
			}
			submeshBounds.Dispose();
			rendererObject2World.Dispose();
			submesh2Renderer.Dispose();

			NativeArray<ushort> indexBuffer = new NativeArray<ushort>(idxCount, Allocator.TempJob);
			NativeArray<int4> indexStartCountOffsetFlip = new NativeArray<int4>(submeshCount, Allocator.TempJob);

			int idxPointer = 0;
			int smPointer2 = 0;
			List<ushort> indices = new List<ushort>();
			Span<int> topologyCount = stackalloc int[5] { 0, 0, 0, 0, 0 };
			topologyCount[(int)MeshTopology.Triangles] = 3;
			topologyCount[(int)MeshTopology.Quads] = 4;

			for (int i = 0; i < rendererCount; i++)
			{
				int rendererIdx = combinedSmInfo.rendererIdx[i];

				int meshIdx = renderer2Mesh[combinedSmInfo.rendererIdx[i]];
				Mesh tmesh = uniqueMeshList[meshIdx];
				int firstSubMesh = combinedSmInfo.submeshStart[i];
				int totalIdxCount = 0;

				for (int sm = 0; sm < combinedSmInfo.submeshCount[i]; sm++)
				{
					tmesh.GetIndices(indices, sm);
					int totalSm = firstSubMesh + sm;
					int numIdx = (int)subMeshDescriptors[totalSm].indexCount;

					NativeArray<ushort>.Copy(CSBListExt.GetInternalArray(indices), 0, indexBuffer, idxPointer, numIdx);

					int sign = rendererScaleSign[rendererIdx] == 0 ? 1 : 0;
					int topo = topologyCount[(int)subMeshDescriptors[smPointer2].topology];
					topo *= sign;
					indexStartCountOffsetFlip[smPointer2] = new int4(idxPointer, numIdx, subMeshDescriptors[firstSubMesh].firstVertex, topo);

					idxPointer += numIdx;
					totalIdxCount += numIdx;
					smPointer2++;
				}
			}
			OffsetFlipIndexBuffer16 offsetIdxJob = new OffsetFlipIndexBuffer16 { indices = indexBuffer, indexStartCountOffset = indexStartCountOffsetFlip };
			JobHandle jobHandle = offsetIdxJob.Schedule(submeshCount, 1);
			jobHandle.Complete();
			combinedMesh.SetIndexBufferData<ushort>(indexBuffer, 0, 0, idxCount, MeshUpdateFlags.DontRecalculateBounds);
			combinedMesh.SetSubMeshes(subMeshDescriptors, MeshUpdateFlags.DontRecalculateBounds);


			indexBuffer.Dispose();
			indexStartCountOffsetFlip.Dispose();

			return combinedMesh;
		}

		[BurstCompile]
		struct OffsetFlipIndexBuffer16 : IJobParallelFor
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<ushort> indices;
			[ReadOnly]
			public NativeArray<int4> indexStartCountOffset;

			public void Execute(int i)
			{
				int4 idxDat = indexStartCountOffset[i];
				int idxStart = idxDat.x;
				int idxEnd = idxStart + idxDat.y;
				ushort offset = (ushort)idxDat.z;
				if (idxDat.w == 0)
				{
					for (int idx = idxStart; idx < idxEnd; idx++)
					{
						indices[idx] += offset;
					}
				}
				else if (idxDat.w == 3)
				{
					for (int idx = idxStart; idx < idxEnd; idx += 3)
					{
						indices[idx] += offset;
						indices[idx + 1] += offset;
						indices[idx + 2] += offset;
						ushort temp = indices[idx];
						indices[idx] = indices[idx + 2];
						indices[idx + 2] = temp;
					}
				}
				else if (idxDat.w == 4)
				{
					for (int idx = idxStart; idx < idxEnd; idx += 4)
					{
						indices[idx] += offset;
						indices[idx + 1] += offset;
						indices[idx + 2] += offset;
						indices[idx + 3] += offset;
						ushort temp = indices[idx];
						indices[idx] = indices[idx + 3];
						indices[idx + 3] = temp;
						temp = indices[idx + 1];
						indices[idx + 1] = indices[idx + 2];
						indices[idx + 2] = temp;
					}
				}
			}
		}

		[BurstCompile]
		struct TransformSubmeshBounds : IJobParallelFor
		{
			public NativeArray<Bounds> bounds;
			[ReadOnly]
			public NativeArray<float4x4> obj2World;
			[ReadOnly]
			public NativeArray<ushort> obj2WorldIdx;

			public void Execute(int i)
			{
				float4x4 T = obj2World[obj2WorldIdx[i]];
				float4 center = new float4((float3)bounds[i].center, 1);
				center = math.mul(T, center);
				float3 extents = bounds[i].extents;
				float3x3 T2 = new float3x3(math.abs(T.c0.xyz), math.abs(T.c1.xyz), math.abs(T.c2.xyz));
				extents = math.mul(T2, math.abs(extents));
				bounds[i] = new Bounds(center.xyz, 2 * extents);
			}
		}

		static int propMeshInBuffer = Shader.PropertyToID("MeshInBuffer");
		static int propMeshOutBuffer = Shader.PropertyToID("MeshOutBuffer");
		static int propVertIn = Shader.PropertyToID("vertIn");
		static int propVertOut = Shader.PropertyToID("vertOut");

		const int meshInBufferSize = 224;
		struct MeshInBuffer
		{
			public Matrix4x4 ObjectToWorld; // 4x4x4 = 64 bytes
			public Matrix4x4 WorldToObject; // 128
			public float4 lightmapScaleOffset; // 144
			public float4 dynLightmapScaleOffset; // 160
			public int4 offset_strideIn_TanSign; // 176
			// + float 4x3 (48) = 224 bytes
		}
		const int meshOutBufferSize = 64;

		internal struct AsyncMeshReadbackData
		{
			public AsyncGPUReadbackRequest request;
			public GraphicsBuffer gpuBuffer;
			public NativeArray<byte> cpuBuffer;

			public void FinishMeshReadback(Mesh combinedMesh)
			{
				request.WaitForCompletion();
				gpuBuffer.Dispose();
				combinedMesh.SetVertexBufferData<byte>(cpuBuffer, 0, 0, cpuBuffer.Length, 0, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
				cpuBuffer.Dispose();
			}
		}

		internal AsyncMeshReadbackData ComputeCopyMeshes(ref NativeArray<PackedChannel> meshPackedChannels, ref NativeArray<PackedChannel> combinedPackedChannels, ref NativeArray<byte> rendererScaleSign, Mesh combinedMesh,
			RendererData[] rd, int2 rendererRange, int[] renderer2Mesh, ref NativeArray<byte> invalidMeshes, List<Mesh> meshList)
		{
			// Figure out what lightmaps are potentially present in the combined mesh.
			// If either UV1 or UV2 are in the combined mesh, but not in an input mesh,
			// then we need to instruct the shader to copy the previous UV channel with a
			// dimension > 0 to that channel
			bool hasLightmap = combinedPackedChannels[5].dimension > 0;
			bool hasDynLightmap = combinedPackedChannels[6].dimension > 0; 


			ComputeShader meshCopy = transferVtxBufferCompute;
			ComputeBuffer meshInSettings = new ComputeBuffer(meshInBufferSize / 4, 4, ComputeBufferType.Constant);
			ComputeBuffer meshOutSettings = new ComputeBuffer(meshOutBufferSize / 4, 4, ComputeBufferType.Constant);
		

			Span<int4> meshOutBuffer = stackalloc int4[1];
			int combinedStride = combinedMesh.GetVertexBufferStride(0);
			meshOutBuffer[0] = new int4(combinedStride, 0, 0, 0);
			GraphicsBuffer combinedMeshBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, combinedMesh.vertexCount, combinedStride);

			CommandBuffer cmd = new CommandBuffer();
			cmd.SetComputeBufferParam(meshCopy, 0, propVertOut, combinedMeshBuffer);
			CSBBufferExt.CmdSetFromSpan(cmd, meshOutSettings, meshOutBuffer, 0, 0, 1);
			cmd.SetBufferData(meshOutSettings, combinedPackedChannels, 0, 4, NUM_VTX_CHANNELS);
			cmd.SetComputeConstantBufferParam(meshCopy, propMeshOutBuffer, meshOutSettings, 0, meshOutBufferSize);
			cmd.SetComputeConstantBufferParam(meshCopy, propMeshInBuffer, meshInSettings, 0, meshInBufferSize);
			int combinedMeshCopyIndex = 0;
			int numMeshesCopied = 0;
			
			GraphicsBuffer[] meshBuffers = new GraphicsBuffer[rendererRange.y - rendererRange.x];
			Span<MeshInBuffer> meshInBuffer = stackalloc MeshInBuffer[1];
			Span<PackedChannel> meshPackedChannels2 = stackalloc PackedChannel[NUM_VTX_CHANNELS];
			for (int renderer = rendererRange.x; renderer < rendererRange.y; renderer++)
			{

				int meshIdx = renderer2Mesh[renderer];
				if (invalidMeshes[meshIdx] == 0)
				{
					int stride = meshList[meshIdx].GetVertexBufferStride(0);
					//Debug.Log("single Mesh Stride = " + stride);
					int tanSign = rendererScaleSign[renderer] > 0 ? 1 : -1;
					meshInBuffer[0] = new MeshInBuffer
						{
							ObjectToWorld = rd[renderer].rendererTransform.localToWorldMatrix,
							WorldToObject = rd[renderer].rendererTransform.worldToLocalMatrix,
							lightmapScaleOffset = new float4(rd[renderer].meshRenderer.lightmapScaleOffset),
							dynLightmapScaleOffset = new float4(rd[renderer].meshRenderer.realtimeLightmapScaleOffset),
							offset_strideIn_TanSign = new int4(combinedMeshCopyIndex, stride, tanSign, 0)
						};
					combinedMeshCopyIndex += combinedStride * meshList[meshIdx].vertexCount;
					CSBBufferExt.CmdSetFromSpan(cmd, meshInSettings, meshInBuffer, 0, 0, 1);

					// Handle lightmapped meshes where uv0 is being used as the lightmap UV. Set UV1's packed data to be UV0's so it just copies UV0 to UV1
					// Also handle the dynamic lightmap. If UV2 is missing and there's no UV2 in the output, its using UV1 
					bool missingLM = hasLightmap && meshPackedChannels[NUM_VTX_CHANNELS * meshIdx + 5].dimension == 0;
					bool missingDynLM = hasDynLightmap && meshPackedChannels[NUM_VTX_CHANNELS * meshIdx + 6].dimension == 0;
					
					if (missingLM || missingDynLM)
					{
						CSBNativeArraySpanExt.Copy(meshPackedChannels, NUM_VTX_CHANNELS * meshIdx, meshPackedChannels2, 0, 12);
						if (missingLM)
						{
							meshPackedChannels2[5] = meshPackedChannels2[4];
						}
						if (missingDynLM)
						{
							meshPackedChannels2[6] = meshPackedChannels2[5].dimension == 0 ? meshPackedChannels2[4] : meshPackedChannels2[5];
						}
						CSBBufferExt.CmdSetFromSpan(cmd, meshInSettings, meshPackedChannels2, 0, 44, NUM_VTX_CHANNELS);
					}
					else
					{
						cmd.SetBufferData(meshInSettings, meshPackedChannels, NUM_VTX_CHANNELS * meshIdx, 44, NUM_VTX_CHANNELS);
					}
					Mesh singleMesh = meshList[meshIdx];
					singleMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
					meshBuffers[numMeshesCopied] = singleMesh.GetVertexBuffer(0);
					cmd.SetComputeBufferParam(meshCopy, 0, propVertIn, meshBuffers[numMeshesCopied]);
					cmd.DispatchCompute(meshCopy, 0, (meshList[meshIdx].vertexCount + 31) / 32, 1, 1);
					numMeshesCopied++;
				}
			}
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Dispose();
			for (int i = 0; i < numMeshesCopied; i++)
			{
				meshBuffers[i].Dispose();
			}

			meshInSettings.Dispose();
			meshOutSettings.Dispose();
			int numBytes = combinedMesh.GetVertexBufferStride(0) * combinedMesh.vertexCount;
			NativeArray<byte> bufferBytes = new NativeArray<byte>(numBytes, Allocator.Persistent);
			AsyncGPUReadbackRequest request = AsyncGPUReadback.RequestIntoNativeArray<byte>(ref bufferBytes, combinedMeshBuffer);
			request.forcePlayerLoopUpdate = true;

			AsyncMeshReadbackData readbackInfo = new AsyncMeshReadbackData() { request = request, gpuBuffer = combinedMeshBuffer, cpuBuffer = bufferBytes };

			return readbackInfo;
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