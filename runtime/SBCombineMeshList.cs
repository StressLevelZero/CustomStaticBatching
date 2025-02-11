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
using static UnityEngine.Mesh;
using UnityEngine.Assertions;
using static SLZ.CustomStaticBatching.PackedChannel;

namespace SLZ.CustomStaticBatching
{
    
    public struct RendererData
    {
        public Mesh mesh;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Transform rendererTransform;
        public Shader shader;
        public bool monomaterial;
    }


    public class SBCombineMeshList
    {

        CombineRendererSettings crs;
        public CombineRendererSettings settings { get => crs; set => crs = value; }
        public ComputeShader transferVtxBufferCompute;




        public SBCombineMeshList(ComputeShader transferVtxComputeShader)
        {
            crs = new CombineRendererSettings(true);
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
        public void GetCombinedMeshBins16(RendererData[] sortedRenderers, int renderersLength, int[] renderer2Mesh, out ushort[] renderer2CMeshIdx, out List<int2> cMeshIdxRange)
        {
            // bin the sorted renderers into groups containing less than 2^16 verticies
            renderer2CMeshIdx = new ushort[renderersLength];

            int vertexCount = 0;
            ushort currentMeshIdx = 0;
            int meshGroupBeginIdx = 0;
            //cMeshIdxRange = new List<int2>();
            cMeshIdxRange = new List<int2>();
            if (renderersLength == 0) return;
            bool monoMaterial = sortedRenderers[0].monomaterial;
            Shader currShader = sortedRenderers[0].shader;
            
            for (int rIdx = 0; rIdx < renderersLength; rIdx++)
            {
            
                int meshVertexCount = sortedRenderers[rIdx].mesh.vertexCount;
                vertexCount += meshVertexCount;
                if (vertexCount >= 0xffff || monoMaterial != sortedRenderers[rIdx].monomaterial || (monoMaterial && (currShader != sortedRenderers[rIdx].shader)))
                {
                    cMeshIdxRange.Add(new int2(meshGroupBeginIdx, rIdx));
                    currentMeshIdx++;
                    meshGroupBeginIdx = rIdx;
                    vertexCount = meshVertexCount;
                    monoMaterial = sortedRenderers[rIdx].monomaterial;
                    currShader = sortedRenderers[rIdx].shader;
                }
                renderer2CMeshIdx[rIdx] = currentMeshIdx;
            
            }
            if (meshGroupBeginIdx == 0 || meshGroupBeginIdx < (renderersLength - 1))
            {
                cMeshIdxRange.Add(new int2(meshGroupBeginIdx, renderersLength));
            }
        }

        /// <summary>
        /// Takes an array of pre-sorted renderers, and bins them into chunks of <65535 verticies, until it hits the first mesh that needs a 32 bit index buffer.
        /// After that point it bins into max32Verts sized bins. Assumes that the renderer array has been sorted such that 32 bit index meshes are all at the end of the array.
        /// </summary>
        /// <param name="sortedRenderers">Sorted list of renderer data</param>
        /// <param name="renderersLength">Range of valid renderers in the sortedRenderers array, starting from 0</param>
        /// <param name="max32Verts">The maximum number of vertices that can be in a 32-bit index buffer combined mesh. Since a 32-bit index buffer can represent trillions of vertices, its a good idea to arbitrarily put a cap on how large the combined mesh can be</param>
        /// <param name="renderer2CMeshIdx">Output array that maps each item in the renderer list to the index of the combined mesh it will be a part of</param>
        /// <param name="cMeshIdxRange">Output list of [start, end) index ranges in the sorted renderer list, where each item represents a group of renderers that will be combined</param>
        /// <param name="largeIdxBinStart">Output index in cMeshIdxRange where 32-bit index meshes start</param>
        public void GetCombinedMeshBins(
            RendererData[] sortedRenderers, 
            int renderersLength, 
            out ushort[] renderer2CMeshIdx, 
            out List<int2> cMeshIdxRange, 
            out int largeIdxBinStart)
        {
            // bin the sorted renderers into groups containing less than 2^16 verticies
            renderer2CMeshIdx = new ushort[renderersLength];

            int vertexCount = 0;
            ushort currentMeshIdx = 0;
            int meshGroupBeginIdx = 0;
            //cMeshIdxRange = new List<int2>();
            cMeshIdxRange = new List<int2>();

            bool monoMaterial = sortedRenderers[0].monomaterial;
            Shader currShader = sortedRenderers[0].shader;

            if (renderersLength == 0)
            {
                largeIdxBinStart = 0; 
                return;
            }
            int rIdx = 0;
            for (; rIdx < renderersLength; rIdx++)
            {
                Mesh m = sortedRenderers[rIdx].mesh;
                if (m.indexFormat == IndexFormat.UInt32)
                {
                    break;
                }
                int meshVertexCount = m.vertexCount;
                vertexCount += meshVertexCount;
                if (vertexCount >= 0xffff || monoMaterial != sortedRenderers[rIdx].monomaterial || (monoMaterial && (currShader != sortedRenderers[rIdx].shader)))
                {
                    cMeshIdxRange.Add(new int2(meshGroupBeginIdx, rIdx));
                    currentMeshIdx++;
                    meshGroupBeginIdx = rIdx;
                    vertexCount = meshVertexCount;
                    monoMaterial = sortedRenderers[rIdx].monomaterial;
                    currShader = sortedRenderers[rIdx].shader;
                }
                renderer2CMeshIdx[rIdx] = currentMeshIdx;

            }
            // if the loop ended in the middle of filling a bin, add a bin from the end of the last bin to the last renderer index before the loop ended
            if ((meshGroupBeginIdx == 0 && rIdx > 0) || meshGroupBeginIdx < rIdx - 1)
            {
                cMeshIdxRange.Add(new int2(meshGroupBeginIdx, rIdx));
                currentMeshIdx++;
            }
            meshGroupBeginIdx = rIdx;
            vertexCount = 0;
            largeIdxBinStart = cMeshIdxRange.Count;
            int max32Vtx = crs.maxCombined32Idx;
            if (crs.allow32bitIdx)
            {
                int largeIdxStart = rIdx;
            
                for (; rIdx < renderersLength; rIdx++)
                {
                    Mesh m = sortedRenderers[rIdx].mesh;

                    int meshVertexCount = m.vertexCount;
                    vertexCount += meshVertexCount;
                    if (vertexCount > max32Vtx || monoMaterial != sortedRenderers[rIdx].monomaterial || (monoMaterial && (currShader != sortedRenderers[rIdx].shader)))
                    {
                        cMeshIdxRange.Add(new int2(meshGroupBeginIdx, rIdx));
                        currentMeshIdx++;
                        meshGroupBeginIdx = rIdx;
                        vertexCount = meshVertexCount;
                        monoMaterial = sortedRenderers[rIdx].monomaterial;
                        currShader = sortedRenderers[rIdx].shader;
                    }
                    renderer2CMeshIdx[rIdx] = currentMeshIdx;
                }

                if ((meshGroupBeginIdx == largeIdxStart && rIdx > largeIdxStart) || meshGroupBeginIdx < renderersLength - 1)
                {
                    cMeshIdxRange.Add(new int2(meshGroupBeginIdx, renderersLength));
                }
            }
        }

        /// <summary>
        /// Get a list of unique meshes for the given renderers, and a mapping from the index of the renderer to the index of the unique mesh.
        /// For small lists of meshes where doing a job would take longer
        /// </summary>
        /// <param name="renderers">Array of renderer structs from which to generate the list of unique meshes</param>
        /// <param name="meshList">output list of unique meshes</param>
        /// <param name="renderer2Mesh">Array that maps each index of the renderer array to an index in the unique mesh list</param>
        public static void SerialGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out int[] renderer2Mesh)
        {
            meshList = new List<Mesh>(renderers.Length);
            //Debug.Log("Num Renderers: " + renderers.Length);
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
        public static void ParallelGetUniqueMeshes(RendererData[] renderers, out List<Mesh> meshList, out Mesh.MeshDataArray meshDataArray, out int[] renderer2Mesh)
        {
            SerialGetUniqueMeshes(renderers, out meshList, out renderer2Mesh);
#if UNITY_EDITOR
            meshDataArray = MeshUtility.AcquireReadOnlyMeshData(meshList);
#else
            meshDataArray = Mesh.AcquireReadOnlyMeshData(meshList);
#endif
        }





        /// <summary>
        /// Gets packed channel information for each of the 12 possible channels of the vertex struct for each mesh in the input list.
        /// Also determines if the mesh's layout is compatible for merging, and marks bad meshes with strange vertex 
        /// attribute formats that can't losslessly be converted to floating point (like integer formats)
        /// </summary>
        /// <param name="meshList">List of meshes to get the channel information of</param>
        /// <param name="meshChannels">output array of packed channel information. The index of each element divided by 12 is the index of the mesh it corresponds to</param>
        /// <param name="invalidMeshes">outupt array of flags that correspond to each mesh in the mesh list. If the value is 1, the mesh has incompatible channel formats and can't be combined</param>
        public static void SerialGetMeshLayout(List<Mesh> meshList, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
        {
            int numMeshes = meshList.Count;
            meshChannels = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS * numMeshes, Allocator.Temp);
            invalidMeshes = new NativeArray<byte>(numMeshes, Allocator.Temp);
            Span<byte> vtxFmtLUT = stackalloc byte[16]; // currently there are 12 VertexAttributeFormat enum values from 0 to 11. If this ever changes, this could break!
            for (int i = 0; i < 16; i++) vtxFmtLUT[i] = (byte)VtxFormats.Invalid; // 0 represents an invalid format
            vtxFmtLUT[(int)VertexAttributeFormat.Float32] = (byte)VtxFormats.Float32;
            vtxFmtLUT[(int)VertexAttributeFormat.Float16] = (byte)VtxFormats.Float16;
            vtxFmtLUT[(int)VertexAttributeFormat.SNorm8] = (byte)VtxFormats.SNorm8;
            vtxFmtLUT[(int)VertexAttributeFormat.UNorm8] = (byte)VtxFormats.UNorm8;
            for (int i = 0; i < numMeshes; i++)
            {
                int baseIdx = NUM_VTX_CHANNELS * i;
                Mesh data = meshList[i];
                bool meshIsInvalid = !data.HasVertexAttribute(VertexAttribute.Position) || data.vertexBufferCount > 2; // Only support 2 streams for now

                for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
                {
                    bool hasAttribute = data.HasVertexAttribute((VertexAttribute)channel);
                    VertexAttribute attribute = (VertexAttribute)channel;
                    if (hasAttribute)
                    {
                        byte channelFormat = vtxFmtLUT[(int)data.GetVertexAttributeFormat(attribute)];
                        meshIsInvalid = meshIsInvalid || (channelFormat == (int)VtxFormats.Invalid);
                        meshChannels[baseIdx + channel] = new PackedChannel
                        {
                            dimension = (byte)data.GetVertexAttributeDimension(attribute),
                            format = channelFormat,
                            offset = (byte)data.GetVertexAttributeOffset(attribute),
                            stream = (byte)data.GetVertexAttributeStream(attribute)
                        };
                    }
                }
                invalidMeshes[i] = meshIsInvalid ? (byte)1 : (byte)0;
            }
        }


        /// <summary>
        /// Gets packed channel information for each of the 12 possible channels of the vertex struct for each mesh in the input list,
        /// doing so using parallel jobs. Also determines if the mesh's layout is compatible for merging, and flags bad meshes with more than 2 vertex streams 
        /// that contain vertex attributes with strange formats that can't losslessly be converted to floating point (like integer formats)
        /// </summary>
        /// <param name="meshDataArray">Array of mesh data to get the channel information of</param>
        /// <param name="meshChannels">output array of packed channel information. The index of each element divided by 12 is the index of the mesh it corresponds to</param>
        /// <param name="invalidMeshes">outupt array of flags that correspond to each mesh in the mesh list. If the value is 1, the mesh has incompatible channel formats and can't be combined</param>
        public static void ParallelGetMeshLayout(Mesh.MeshDataArray meshDataArray, out NativeArray<PackedChannel> meshChannels, out NativeArray<byte> invalidMeshes)
        {
            meshChannels = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS * meshDataArray.Length, Allocator.Persistent);
            invalidMeshes = new NativeArray<byte>(meshDataArray.Length, Allocator.TempJob);
            NativeArray<byte> vtxFmtLUT = new NativeArray<byte>(16, Allocator.TempJob); // currently there are 12 VertexAttributeFormat enum values from 0 to 11. If this ever changes, this could break!
            for (int i = 0; i < 16; i++) vtxFmtLUT[i] = (byte)VtxFormats.Invalid; // 255 represents an invalid format
            vtxFmtLUT[(int)VertexAttributeFormat.Float32] = (byte)VtxFormats.Float32;
            vtxFmtLUT[(int)VertexAttributeFormat.Float16] = (byte)VtxFormats.Float16;
            vtxFmtLUT[(int)VertexAttributeFormat.SNorm8] = (byte)VtxFormats.SNorm8;
            vtxFmtLUT[(int)VertexAttributeFormat.UNorm8] = (byte)VtxFormats.UNorm8;

            GetMeshLayoutJob getLayout = new GetMeshLayoutJob { _meshChannels = meshChannels, _invalidMeshes = invalidMeshes, _vtxFmtLUT = vtxFmtLUT, _meshData = meshDataArray };
            JobHandle layoutHandle = getLayout.Schedule(meshDataArray.Length, 16);
            layoutHandle.Complete();

            vtxFmtLUT.Dispose();
        }

        /// <summary>
        /// Gets the vertex struct layout of an array of meshes, and populates an array of flags that indicate if a mesh has an 
        /// incompatible vertex attribute format or more than 2 vertex streams
        /// </summary>
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
                bool meshIsInvalid = !data.HasVertexAttribute(VertexAttribute.Position) || data.vertexBufferCount > 2; // Only support 2 streams for now

                for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
                {
                    bool hasAttribute = data.HasVertexAttribute((VertexAttribute)channel);
                    VertexAttribute attribute = (VertexAttribute)channel;
                    if (hasAttribute)
                    {
                        byte channelFormat = _vtxFmtLUT[(int)data.GetVertexAttributeFormat(attribute)];
                        meshIsInvalid = meshIsInvalid || (channelFormat == (int)VtxFormats.Invalid);
                        _meshChannels[baseIdx + channel] = new PackedChannel
                        {
                            dimension = (byte)data.GetVertexAttributeDimension(attribute),
                            format = channelFormat,
                            offset = (byte)data.GetVertexAttributeOffset(attribute),
                            stream = (byte)data.GetVertexAttributeStream(attribute)
                        };
                    }
                }
                _invalidMeshes[i] = meshIsInvalid ? (byte)1 : (byte)0;
            }
        }

        /// <summary>
        /// Cleans the renderer array of renderers whose mesh that can't be combined other meshes, moving all the valid meshes to the front of the array.
        /// Does not resize the array, instead returns the number of vaild renderers which should be used in place of renderers.length
        /// </summary>
        /// <param name="invalidMeshes"></param>
        /// <param name="renderers"></param>
        /// <param name="renderer2Mesh"></param>
        /// <returns>The number of valid meshes in the array</returns>
        public int CleanInvalidRenderers(NativeArray<byte> invalidMeshes, RendererData[] renderers, int[] renderer2Mesh)
        {
            int numRenderers = renderers.Length;
            int p = 0;
            for (int i = 0; i < numRenderers; i++)
            {
                if (invalidMeshes[renderer2Mesh[i]] == 0)
                {
                    renderers[p] = renderers[i];
                    renderer2Mesh[p] = renderer2Mesh[i];
                    p++;
                }
            }
            return p;
        }


        /// <summary>
        /// Gets the sign of the scale of each renderer. Used to determine if the winding order of a renderer needs to be flipped in the combined mesh,
        /// and to set the sign of the tangent's 4th component for each vertex
        /// </summary>
        /// <param name="rd"></param>
        /// <returns></returns>
        public NativeArray<byte> GetRendererScaleSign(RendererData[] rd)
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

        [BurstCompile]
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

        struct UniqueMeshData
        {
            public int[] renderer2Mesh;
            public NativeArray<PackedChannel> meshChannels;
            public NativeArray<byte> invalidMeshes;
        }
        public NativeArray<PackedChannel> GetCombinedMeshLayout(
            RendererData[] renderers,
            ref NativeArray<PackedChannel> meshChannels,
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
                if (!uniqueMeshSet.Contains(index))
                {
                    meshIndex.Add(index);
                    uniqueMeshSet.Add(index);

                    // Determine if the combined mesh will be lightmapped.
                    // Sometimes, people will use UV0 as the lightmap UV. This doesn't work with static batching as the lightmap scale/offset
                    // gets baked into the lightmap UV, and UV0 is normally compressed to 16 bit which isn't enough for lightmaps.
                    // Therefore, forcibly add UV1 to lightmapped combined meshes even if none of the input meshes have it.
                    MeshRenderer mr = renderers[i].meshRenderer;

                    #if UNITY_EDITOR
                    isLightmapped = isLightmapped || (GameObjectUtility.AreStaticEditorFlagsSet(mr.gameObject, StaticEditorFlags.ContributeGI) && mr.receiveGI == ReceiveGI.Lightmaps);
                    #else
                    isLightmapped = isLightmapped || (mr.lightmapIndex < 0xFFFE && mr.lightmapIndex > 0);
                    #endif
                    isDynamicLightmapped = isDynamicLightmapped || (mr.realtimeLightmapIndex < 0xFFFE && mr.realtimeLightmapIndex > 0);
                }
            }

            int meshIdxCount = meshIndex.Count;
            //Debug.Log("Unique Mesh count in combined mesh: " +  meshIdxCount);
            NativeArray<PackedChannel> combinedFormat = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS, Allocator.TempJob);
            Span<int> minTypeLUT = stackalloc int[] { 1, 4, 4, 2, 1 };
            Span<bool> useAltStream = stackalloc bool[12];
            crs.altStream.CopyTo(useAltStream);
            int altStreamFlag = 1 << 24;
            for (int mesh = 0; mesh < meshIdxCount; mesh++)
            {
                int meshPtr = meshIndex[mesh] * NUM_VTX_CHANNELS;
                for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
                {
                    PackedChannel a = combinedFormat[channel];
                    PackedChannel b = meshChannels[meshPtr + channel];
                    int largestFmt = math.max((int)a.format, (int)b.format);
                    largestFmt = math.min(largestFmt, (int)crs.serializedVtxFormats[channel]);

                    int maxDim = math.max((int)a.dimension, (int)b.dimension);
                    int roundDim = minTypeLUT[largestFmt];
                    maxDim = ((maxDim + roundDim - 1) / roundDim) * roundDim;
                    int stream = useAltStream[channel] ? altStreamFlag : 0;
                    combinedFormat[channel] = new PackedChannel { packedData = (uint)(maxDim | (largestFmt << 8) | stream) };
                }
            }

            // Add a lightmap UV1 if one or more of the input renderers are either static or dynamic lightmapped but none of the inputs have UV1's
            if ((isLightmapped || isDynamicLightmapped) && combinedFormat[5].dimension == 0)
            {
                combinedFormat[5] = new PackedChannel() { dimension = 2, format = crs.serializedVtxFormats[5], stream = combinedFormat[5].stream };
            }
            // Add a dynamic lightmap UV2 if there are dynamic lightmapped renderers in the input, but none of the inputs have UV2's
            if (isDynamicLightmapped && combinedFormat[6].dimension == 0)
            {
                
                combinedFormat[6] = new PackedChannel() { dimension = 2, format = crs.serializedVtxFormats[6], stream = combinedFormat[6].stream };
            }

            uint cumulativeOffset = 0;
            uint cumulativeOffset2 = 0;
            ReadOnlySpan<byte> vtxFmtToBytes = PackedChannel.VtxFmtToBytes;
            for (int channel = 0; channel < NUM_VTX_CHANNELS; channel++)
            {
                
                if (useAltStream[channel])
                {
                    combinedFormat[channel] = new PackedChannel { packedData = combinedFormat[channel].packedData | (cumulativeOffset2 << 16) };
                    cumulativeOffset2 = (cumulativeOffset2 + (uint)vtxFmtToBytes[combinedFormat[channel].format] * combinedFormat[channel].dimension);
                }
                else
                {
                    combinedFormat[channel] = new PackedChannel { packedData = combinedFormat[channel].packedData | (cumulativeOffset << 16) };
                    cumulativeOffset = (cumulativeOffset + (uint)vtxFmtToBytes[combinedFormat[channel].format] * combinedFormat[channel].dimension);
                }
            }
            return combinedFormat;
        }

        struct CombinedMeshSmInfo
        {
            public int[] rendererIdx;
            public int[] submeshStart;
            public int[] submeshCount;
        }

        public static VertexAttributeDescriptor[] VtxAttrDescFromPacked(NativeArray<PackedChannel> packedChannels)
        {
            List<VertexAttributeDescriptor> vertexAttributes = new List<VertexAttributeDescriptor>(NUM_VTX_CHANNELS);
            ReadOnlySpan<VertexAttributeFormat> formatLUT = PackedChannel.ToUnityFormatLUT;
            for (int i = 0; i < NUM_VTX_CHANNELS; i++)
            {
                if (packedChannels[i].dimension != 0)
                {
                    vertexAttributes.Add(new VertexAttributeDescriptor((VertexAttribute)i, formatLUT[packedChannels[i].format], packedChannels[i].dimension, packedChannels[i].stream));
                }
            }
            return vertexAttributes.ToArray();
        }

        /// <summary>
        /// Generates a Unity Mesh for a list of renderers. Generates the combined index buffer, sets the submesh descriptors, 
        /// calculates the worldspace bounds of each submesh, and flips the winding order for negatively scaled meshes.
        /// </summary>
        /// <param name="rd">Sorted array of renderers</param>
        /// <param name="uniqueMeshList">List of unique meshes used by the renderers</param>
        /// <param name="rendererRange">Range of indices in rd that will be combined in the output mesh</param>
        /// <param name="renderer2Mesh">A mapping from each index of rd to a mesh in uniqueMeshList</param>
        /// <param name="packedChannels">An array of dimension NUM_VTX_CHANNELS (currently 12) that contains a description of the vertex attribute format and dimension of each possible channel in the output mesh</param>
        /// <param name="rendererScaleSign">An array of bytes that indicates if the renderer of the corresponding index has been scaled negatively. If so, the winding order of the indices in the combined mesh must be reversed</param>
        /// <returns></returns>
        public Mesh GetCombinedMeshObject<T>(RendererData[] rd, MeshDataArray uniqueMeshList, int2 rendererRange, int[] renderer2Mesh, ref NativeArray<PackedChannel> packedChannels, ref NativeArray<byte> rendererScaleSign, bool highPidxBuffer)
        where T : unmanaged
        {
            Assert.IsTrue(typeof(T) == typeof(int) || typeof(T) == typeof(ushort), "GetCombinedMeshObject can only use ushort and int types!");
            // Are we using a high-precision index buffer?
            bool highPIdxBuffer = typeof(T) == typeof(int);

            // Get the total number of vertices, submeshes, and valid renderers that make up this combined mesh
            int vertexCount = 0;
            int submeshCount = 0;
            int rendererCount = rendererRange.y - rendererRange.x;
            int[] validRendererIdx = new int[rendererRange.y - rendererRange.x];

            bool[] isMonoMaterial = new bool[rendererCount];

            // Iterate once over the range of renderers, counting the total verticies and submeshes.
            
            for (int rangeIdx = rendererRange.x, rendererIdx = 0; rangeIdx < rendererRange.y; rangeIdx++)
            {
				bool ismm = IsMonoMaterial(rd[rangeIdx].meshRenderer);
				isMonoMaterial[rendererIdx] = ismm;
				vertexCount += rd[rangeIdx].mesh.vertexCount;
				submeshCount += ismm ? 1 : rd[rangeIdx].mesh.subMeshCount;
				validRendererIdx[rendererIdx] = rangeIdx;
				rendererIdx++;
            }
        

            List<VertexAttributeDescriptor> vertexAttributes = new List<VertexAttributeDescriptor>(NUM_VTX_CHANNELS);
            ReadOnlySpan<VertexAttributeFormat> formatLUT = PackedChannel.ToUnityFormatLUT;
            for (int i = 0; i < NUM_VTX_CHANNELS; i++)
            {
                if (packedChannels[i].dimension != 0)
                {
                    vertexAttributes.Add(new VertexAttributeDescriptor((VertexAttribute)i, formatLUT[packedChannels[i].format], packedChannels[i].dimension, packedChannels[i].stream));
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


            // Iterate again over the renderers this time getting the submesh descriptors of all the meshes,
            // calculating the sum of their index counts, and calculating the union of all their bounds

            bool initializeBounds = true;
            Bounds totalBounds = new Bounds();

            NativeArray<Bounds> submeshBounds = new NativeArray<Bounds>(submeshCount, Allocator.TempJob);
            NativeArray<float4x4> rendererObject2World = new NativeArray<float4x4>(rendererCount, Allocator.TempJob);
            NativeArray<ushort> submesh2Renderer = new NativeArray<ushort>(submeshCount, Allocator.TempJob);

            // Keep track of the most indices a submesh has, so when it comes time to copy the indices we can make a nativearray of exactly the right size as a staging buffer
            int maxSmIdxCount = 0;
        
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

				// Flatten submeshes from renderers with multiple material slots of the same material to one submesh
				if (isMonoMaterial[i])
				{
					// Debug.Log($"IsMonoMaterial: {AnimationUtility.CalculateTransformPath(rd[rIdx].rendererTransform, null)}");
					Material mat0 = rd[rIdx].meshRenderer.sharedMaterials[0];
					rd[rIdx].meshRenderer.sharedMaterials = new Material[1] { mat0 };
					SubMeshDescriptor smd0 = rd[rIdx].mesh.GetSubMesh(0);
					int smdFirstVertex = smd0.firstVertex;
					int smdVtxCount = smd0.vertexCount;
					int smdIndexCount = smd0.indexCount;
					Bounds smdBounds = smd0.bounds;
					for (int sm = 1; sm < smCount; sm++)
					{
						SubMeshDescriptor smd = rd[rIdx].mesh.GetSubMesh(sm);
						smdFirstVertex = math.min(smdFirstVertex, smd.firstVertex);
						smdIndexCount += smd.indexCount;
						smdVtxCount += smd.vertexCount;
						smdBounds.Encapsulate(smd.bounds);
						//Debug.Log("Submesh " + smPointer + " index start: " + smd2.indexStart + " bounds: " + smd2.bounds);
					}
					SubMeshDescriptor smd2 = new SubMeshDescriptor()
					{
						baseVertex = 0,
						firstVertex = smdFirstVertex + vtxPointer,
						bounds = smdBounds,
						indexCount = smdIndexCount,
						indexStart = idxCount,
						vertexCount = smdVtxCount,
						topology = smd0.topology,
					};
					submesh2Renderer[smPointer] = (ushort)i;
					submeshBounds[smPointer] = smdBounds;
					//Debug.Log("Submesh " + smPointer + " index start: " + smd2.indexStart + " bounds: " + smd2.bounds);
					maxSmIdxCount = math.max(maxSmIdxCount, smdIndexCount);
					idxCount += smdIndexCount;
					subMeshDescriptors[smPointer] = smd2;

					smPointer++;
				}
                else for (int sm = 0; sm < smCount; sm++)
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
					maxSmIdxCount = math.max(maxSmIdxCount, smd.indexCount);
					idxCount += smd.indexCount;
					subMeshDescriptors[smPointer] = smd2;

					smPointer++;
                }
				//Debug.Log($"Submesh index: {firstSubMesh}, Range {smPointer - firstSubMesh}, {AnimationUtility.CalculateTransformPath(rd[rIdx].rendererTransform, null)}");
				combinedSmInfo.rendererIdx[meshPointer] = rIdx;
                combinedSmInfo.submeshStart[meshPointer] = firstSubMesh;
                combinedSmInfo.submeshCount[meshPointer] = smPointer - firstSubMesh;
                meshPointer++;
                vtxPointer += rd[rIdx].mesh.vertexCount;
            }

            // Set the total size of the index buffer of the combined mesh, and set the total bounds.
            combinedMesh.SetIndexBufferParams(idxCount, highPIdxBuffer ? IndexFormat.UInt32 : IndexFormat.UInt16);
            combinedMesh.bounds = totalBounds;

            // Transform the bounds of each submesh from its local object space to world space
            TransformSubmeshBounds transformSubmeshBounds = new TransformSubmeshBounds() { bounds = submeshBounds, obj2World = rendererObject2World, obj2WorldIdx = submesh2Renderer };
            JobHandle transformBoundsHandle = transformSubmeshBounds.Schedule(submeshCount, 16);
            transformBoundsHandle.Complete();

            // Recreate each submesh descriptor struct with the new worldspace bounds
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


            // Create the combined mesh's index buffer, and populate it with the indices of all the input meshes' submeshes
            NativeArray<T> indexBuffer = new NativeArray<T>(idxCount, Allocator.TempJob);
            NativeArray<int4> indexStartCountOffsetFlip = new NativeArray<int4>(submeshCount, Allocator.TempJob);

            int idxPointer = 0;
            int smPointer2 = 0;
            List<T> indices = new List<T>();
            Span<int> topologyCount = stackalloc int[5] { 0, 0, 0, 0, 0 };
            topologyCount[(int)MeshTopology.Triangles] = 3;
            topologyCount[(int)MeshTopology.Quads] = 4;

            for (int i = 0; i < rendererCount; i++)
            {
                int rendererIdx = combinedSmInfo.rendererIdx[i];

                int meshIdx = renderer2Mesh[combinedSmInfo.rendererIdx[i]];
                MeshData tmesh = uniqueMeshList[meshIdx];
                int firstSubMesh = combinedSmInfo.submeshStart[i];
                int totalIdxCount = 0;
				int smCount = combinedSmInfo.submeshCount[i];

				// If this is a multi-submesh renderer using only one material, copy all submeshes indices sequentially as though they constitute a single submesh.
				if (isMonoMaterial[i])
				{
					smCount = tmesh.subMeshCount;
					int numIdx2 = (int)subMeshDescriptors[firstSubMesh].indexCount;
					int idxPointer2 = idxPointer;
					for (int sm = 0; sm < smCount; sm++)
					{
						SubMeshDescriptor smd = tmesh.GetSubMesh(sm);
						int numIdx = smd.indexCount;
						if (highPIdxBuffer)
						{
							NativeArray<int> idxAlias = NativeArraySubArray.GetSubArrayAlias<T, int>(indexBuffer, idxPointer, numIdx);
							tmesh.GetIndices(idxAlias, sm);
						}
						else
						{
							NativeArray<ushort> idxAlias = NativeArraySubArray.GetSubArrayAlias<T, ushort>(indexBuffer, idxPointer, numIdx);
							tmesh.GetIndices(idxAlias, sm);
						}
						idxPointer += numIdx;
						totalIdxCount += numIdx;
					}
					//NativeArray<T>.Copy(CSBListExt.GetInternalArray(indices), 0, indexBuffer, idxPointer, numIdx);

					int sign = rendererScaleSign[rendererIdx] == 0 ? 1 : 0;
					int topo = topologyCount[(int)subMeshDescriptors[smPointer2].topology];
					topo *= sign;
					indexStartCountOffsetFlip[smPointer2] = new int4(idxPointer2, numIdx2, subMeshDescriptors[smPointer2].firstVertex, topo);
					smPointer2++;
				}
				else for (int sm = 0; sm < smCount; sm++)
                {
                    int totalSm = firstSubMesh + sm;
                    int numIdx = (int)subMeshDescriptors[totalSm].indexCount;
                    
                    if (highPIdxBuffer)
                    {
                        NativeArray<int> idxAlias = NativeArraySubArray.GetSubArrayAlias<T, int>(indexBuffer, idxPointer, numIdx);
                        tmesh.GetIndices(idxAlias, sm);
                    }
                    else
                    {
                        NativeArray<ushort> idxAlias = NativeArraySubArray.GetSubArrayAlias<T, ushort>(indexBuffer, idxPointer, numIdx);
                        tmesh.GetIndices(idxAlias, sm);
                    }

                    //NativeArray<T>.Copy(CSBListExt.GetInternalArray(indices), 0, indexBuffer, idxPointer, numIdx);

                    int sign = rendererScaleSign[rendererIdx] == 0 ? 1 : 0;
                    int topo = topologyCount[(int)subMeshDescriptors[smPointer2].topology];
                    topo *= sign;
                    indexStartCountOffsetFlip[smPointer2] = new int4(idxPointer, numIdx, subMeshDescriptors[firstSubMesh].firstVertex, topo);

                    idxPointer += numIdx;
                    totalIdxCount += numIdx;
                    smPointer2++;
                }
            }

            // Offset the indices for each submesh in the combined mesh's index buffer by the total index count of the preceeding submeshes
            if (highPIdxBuffer)
            {
                NativeArray<int> intBuffer = indexBuffer.Reinterpret<int>(sizeof(int));
                GenericInt32 intMath = new GenericInt32();
                OffsetFlipIndexBuffer<int, GenericInt32> offsetIdxJob = new OffsetFlipIndexBuffer<int, GenericInt32> { 
                    indices = intBuffer, indexStartCountOffset = indexStartCountOffsetFlip, converter = intMath };
                JobHandle jobHandle = offsetIdxJob.Schedule(submeshCount, 1);
                jobHandle.Complete();
            }
            else 
            {
                NativeArray<ushort> shortBuffer = indexBuffer.Reinterpret<ushort>(sizeof(ushort));
                GenericInt16 shortMath = new GenericInt16();
                OffsetFlipIndexBuffer<ushort, GenericInt16> offsetIdxJob = new OffsetFlipIndexBuffer<ushort, GenericInt16> { 
                    indices = shortBuffer, indexStartCountOffset = indexStartCountOffsetFlip, converter = shortMath };
                JobHandle jobHandle = offsetIdxJob.Schedule(submeshCount, 1);
                jobHandle.Complete();
            }

            combinedMesh.SetIndexBufferData<T>(indexBuffer, 0, 0, idxCount, MeshUpdateFlags.DontRecalculateBounds);
            combinedMesh.SetSubMeshes(subMeshDescriptors, MeshUpdateFlags.DontRecalculateBounds);


            indexBuffer.Dispose();
            indexStartCountOffsetFlip.Dispose();

            return combinedMesh;
        }

		/// <summary>
		/// Determines if a renderer with multiple material slots has the same material applied to every slot.
		/// </summary>
		/// <param name="r">MeshRenderer to check.</param>
		/// <returns>True if the renderer has more than one material slot and every material slot has the same material</returns>
        bool IsMonoMaterial(MeshRenderer r)
        {
            Material[] sm = r.sharedMaterials;
            int numMat = sm.Length;
            bool isMonoMaterial = numMat > 1;
            for (int i = 1; i < numMat; i++)
            {
                if (sm[i] != sm[0]) return false;
            }
            return isMonoMaterial;
        }

        /// <summary>
        /// Job for offsetting the values of the indices of each submesh in the combined index buffer by the total number of indices of all the preceeding submeshes
        /// Also flips the order of the indices of each primitive if the submesh was scaled negatively
        /// </summary>
        /// <typeparam name="T">Type of the index buffer, assumed to be int or ushort</typeparam>
        /// <typeparam name="TConverter">Struct implementing the IGenericInt interface for T, to provide the method adding an int and T</typeparam>
        [BurstCompile]
        public struct OffsetFlipIndexBuffer<T, TConverter> : IJobParallelFor
            where T : unmanaged 
            where TConverter : IGenericInt<T>
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<T> indices;
            [ReadOnly]
            public NativeArray<int4> indexStartCountOffset;
            public TConverter converter;

            public JobHandle ISchedule(int arrayLength, int innerLoopBatchCount)
            {
                return this.Schedule(arrayLength, innerLoopBatchCount);
            }

            public void Execute(int i)
            {
                int4 idxDat = indexStartCountOffset[i];
                int idxStart = idxDat.x;
                int idxEnd = idxStart + idxDat.y;
                int offset = idxDat.z;
                int primitiveCount = idxDat.w;
                if (primitiveCount == 0)
                {
                    for (int idx = idxStart; idx < idxEnd; idx++)
                    {
                        indices[idx] = converter.Add(indices[idx], offset);
                    }
                }
                else if (primitiveCount == 3)
                {
                    for (int idx = idxStart; idx < idxEnd; idx += 3)
                    {
                        indices[idx] = converter.Add(indices[idx], offset);
                        indices[idx + 1] = converter.Add(indices[idx + 1], offset);
                        indices[idx + 2] = converter.Add(indices[idx + 2], offset);
                        T temp = indices[idx];
                        indices[idx] = indices[idx + 2];
                        indices[idx + 2] = temp;
                    }
                }
                else if (primitiveCount == 4)
                {
                    for (int idx = idxStart; idx < idxEnd; idx += 4)
                    {
                        indices[idx] = converter.Add(indices[idx], offset);
                        indices[idx + 1] = converter.Add(indices[idx + 1], offset);
                        indices[idx + 2] = converter.Add(indices[idx + 2], offset);
                        indices[idx + 3] = converter.Add(indices[idx + 3], offset);
                        T temp = indices[idx];
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
        public struct TransformSubmeshBounds : IJobParallelFor
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
            RendererData[] rd, int2 rendererRange, int[] renderer2Mesh, List<Mesh> meshList)
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
#if UNITY_2022_2_OR_NEWER
            request.forcePlayerLoopUpdate = true;
#endif
            AsyncMeshReadbackData readbackInfo = new AsyncMeshReadbackData() { request = request, gpuBuffer = combinedMeshBuffer, cpuBuffer = bufferBytes };

            return readbackInfo;
        }

        internal void JobCopyMeshes(ref NativeArray<PackedChannel> meshPackedChannels, ref NativeArray<PackedChannel> combinedPackedChannels, ref NativeArray<byte> rendererScaleSign, Mesh combinedMesh,
    RendererData[] rd, int2 rendererRange, int[] renderer2Mesh, MeshDataArray meshList)
        {
            // Figure out what lightmaps are potentially present in the combined mesh.
            // If either UV1 or UV2 are in the combined mesh, but not in an input mesh,
            // then we need to instruct the job to copy the previous UV channel with a
            // dimension > 0 to that channel
            bool hasLightmap = combinedPackedChannels[5].dimension > 0;
            bool hasDynLightmap = combinedPackedChannels[6].dimension > 0;

        
            int combinedStride = combinedMesh.GetVertexBufferStride(0);
            int combinedStride2 = combinedMesh.GetVertexBufferStride(1);
            NativeArray<byte> combinedMeshVert = new NativeArray<byte>(combinedStride * combinedMesh.vertexCount, Allocator.TempJob);
            NativeArray<byte> combinedMeshVert2 = new NativeArray<byte>(combinedStride2 * combinedMesh.vertexCount, Allocator.TempJob);
            int4 strideOut = new int4(combinedStride, combinedStride2, 0, 0);
            
            int combinedMeshCopyIndex = 0;
            int combinedMeshCopyIndex2 = 0;
            int numMeshesCopied = 0;

            FixedList32Bytes<uint> formatToBytes = new FixedList32Bytes<uint>() { 1, 1, 1, 2, 4 };

            NativeArray<PackedChannel> meshPackedChannels2 = new NativeArray<PackedChannel>(NUM_VTX_CHANNELS, Allocator.TempJob);
            for (int renderer = rendererRange.x; renderer < rendererRange.y; renderer++)
            {

                int meshIdx = renderer2Mesh[renderer];
                
                int stride = meshList[meshIdx].GetVertexBufferStride(0);
                int stride2 = meshList[meshIdx].GetVertexBufferStride(1);
                bool hasSecondBuffer = stride2 > 0;
                if (!hasSecondBuffer) stride2 = 1; // max of 1, so we can store the sign of the tangent in this value even if there is no second buffer
                //Debug.Log("single Mesh Stride = " + stride);
                int tanSign = rendererScaleSign[renderer] > 0 ? 1 : -1;
                NativeArray<PackedChannel>.Copy(meshPackedChannels, NUM_VTX_CHANNELS * meshIdx, meshPackedChannels2, 0, NUM_VTX_CHANNELS);
                
                for (int i = 0; i < NUM_VTX_CHANNELS; i++)
                {
                
                    Debug.Assert(meshPackedChannels2[i].offset % 4 == 0, "offset not aligned on 4 bytes, failure!");
                }

                // Handle lightmapped meshes where uv0 is being used as the lightmap UV. Set UV1's packed data to be UV0's so it just copies UV0 to UV1
                // Also handle the dynamic lightmap. If UV2 is missing and there's no UV2 in the output, its using UV1 
                bool missingLM = hasLightmap && meshPackedChannels2[5].dimension == 0;
                bool missingDynLM = hasDynLightmap && meshPackedChannels2[6].dimension == 0;

                if (missingLM || missingDynLM)
                {

                    if (missingLM)
                    {
                        meshPackedChannels2[5] = meshPackedChannels2[4];
                    }
                    if (missingDynLM)
                    {
                        meshPackedChannels2[6] = meshPackedChannels2[5].dimension == 0 ? meshPackedChannels2[4] : meshPackedChannels2[5];
                    }
                }
                NativeArray<byte> inVert = meshList[meshIdx].GetVertexData<byte>(0);
                TransferVtxBuffer vtxJob = new TransferVtxBuffer
                {
                    vertIn = inVert,
                    vertIn2 = hasSecondBuffer ? meshList[meshIdx].GetVertexData<byte>(1) : inVert,
                    vertOut = combinedMeshVert,
                    vertOut2 = combinedMeshVert2,
                    ObjectToWorld = rd[renderer].rendererTransform.localToWorldMatrix,
                    WorldToObject = rd[renderer].rendererTransform.worldToLocalMatrix,
                    lightmapScaleOffset = new float4(rd[renderer].meshRenderer.lightmapScaleOffset),
                    dynLightmapScaleOffset = new float4(rd[renderer].meshRenderer.realtimeLightmapScaleOffset),
                    offset_strideIn_offset2_strideIn2 = new int4(combinedMeshCopyIndex, stride, combinedMeshCopyIndex2, stride2 * tanSign),
                    inPackedChannelInfo = meshPackedChannels2,
                    strideOut = strideOut,
                    outPackedChannelInfo = combinedPackedChannels,
                    formatToBytes = formatToBytes,
                    normalizeNormTan = crs.normalizeNormalTangent,
                    vtxRounding = crs.roundVertexPositions,
                    vtxRoundingAmount = crs.vertexRoundingSize
                };
                int vertexCount = meshList[meshIdx].vertexCount;
                
                combinedMeshCopyIndex += combinedStride * vertexCount;
                combinedMeshCopyIndex2 += combinedStride2 * vertexCount;

                JobHandle vtxJobHandle = vtxJob.Schedule(vertexCount, 16);
                vtxJobHandle.Complete();
                //vtxJob.vertIn.Dispose(); // Can cause an error? Somehow, the array is already disposed of sometimes. Anyways, I don't need to dispose of this as its just a view into the mesh's buffer.
                numMeshesCopied++;

            }

            combinedMesh.SetVertexBufferData<byte>(combinedMeshVert, 0, 0, combinedMeshVert.Length, 0, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
            if (combinedStride2 > 0)
            {
                combinedMesh.SetVertexBufferData<byte>(combinedMeshVert2, 0, 0, combinedMeshVert2.Length, 1, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
            }

            //for (int uvIdx = 4; uvIdx < 12; uvIdx++)
            //{
            //    if (combinedPackedChannels[uvIdx].dimension > 0)
            //    {
            //        combinedMesh.RecalculateUVDistributionMetric(uvIdx - 4);
            //    }
            //}
            combinedMesh.RecalculateUVDistributionMetrics();

            combinedMesh.UploadMeshData(true);
            meshPackedChannels2.Dispose();
            combinedMeshVert.Dispose();
            combinedMeshVert2.Dispose();
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