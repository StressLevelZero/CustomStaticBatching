using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.Burst;
using Unity.Profiling;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace SLZ.CustomStaticBatching
{
<<<<<<< HEAD
	public class MeshUtilities
	{
		static ProfilerMarker profilerGetSubMesh = new ProfilerMarker("MeshUtilities.GetSplitMesh");
		static ProfilerMarker profilerGetSubMeshMarkUsed = new ProfilerMarker("MeshUtilities.GetSplitMesh.MarkUsed");
		static ProfilerMarker profilerGetSubMeshHashMap = new ProfilerMarker("MeshUtilities.GetSplitMesh.HashMap");
		static ProfilerMarker profilerGetSubMeshInitMesh = new ProfilerMarker("MeshUtilities.GetSplitMesh.InitMesh");
		static ProfilerMarker profilerSplitBySubMesh = new ProfilerMarker("MeshUtilities.SplitBySubmesh");
		// Start is called before the first frame update
		static Mesh GetSplitMesh(
			Mesh originalMesh, 
			ref SubMeshDescriptor oldSmDesc, 
			ref NativeArray<ushort> indexBuffer, 
			ref NativeArray<int> oldVertexBuffer, 
			ref NativeArray<int> tempVertexBuffer, 
			ref NativeArray<int> hashMap,
			int vertexByteStride)
		{
			profilerGetSubMesh.Begin();
			int vertexStride = vertexByteStride / sizeof(int); // Each channel in a mesh buffer MUST be a multiple of 4 bytes!
			NativeArrayClear.Clear(ref hashMap, hashMap.Length);
			int indexCount = indexBuffer.Length;
			int maxIndex = 0;
			profilerGetSubMeshMarkUsed.Begin();


			NativeReference<int> maxIdxRef = new NativeReference<int>(Allocator.TempJob);
			maxIdxRef.Value = 0;
			FlagUsedVerticesSerial flagJob = new FlagUsedVerticesSerial() { hashMap = hashMap, maxIdx = maxIdxRef, indexBuffer = indexBuffer };
			flagJob.Run();
			maxIndex = flagJob.maxIdx.Value;
			maxIdxRef.Dispose();


			profilerGetSubMeshMarkUsed.End();

			profilerGetSubMeshHashMap.Begin();
			ushort currentVtx = 0;

			NativeReference<int> vtxCountRef = new NativeReference<int>(Allocator.TempJob);
			vtxCountRef.Value = 0;
			PopulateVtxBufferSerial popVtxBuffer = new PopulateVtxBufferSerial()
			{
				hashMap = hashMap,
				oldVertexBuffer = oldVertexBuffer,
				tempVertexBuffer = tempVertexBuffer,
				vtxCount = vtxCountRef,
				maxIndex = maxIndex,
				vertexIntStride = vertexStride
			};
			popVtxBuffer.Run();
			currentVtx = (ushort)vtxCountRef.Value;
			vtxCountRef.Dispose();
			profilerGetSubMeshHashMap.End();


			ReIndexBuffer16 reindexJob = new ReIndexBuffer16() { indexBuffer = indexBuffer, hashMap = hashMap };
			JobHandle reindexJobHandle = reindexJob.Schedule(indexCount, 32);
			reindexJobHandle.Complete();

			profilerGetSubMeshInitMesh.Begin();
			Mesh splitMesh = new Mesh();
			
			MeshUpdateFlags noMeshUpdate = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;
			splitMesh.SetVertexBufferParams(currentVtx, originalMesh.GetVertexAttributes());
			splitMesh.SetVertexBufferData<int>(tempVertexBuffer, 0, 0, currentVtx * vertexStride, 0, noMeshUpdate);
			splitMesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
			SubMeshDescriptor newSmDesc = new SubMeshDescriptor()
			{
				indexStart = 0,
				baseVertex = 0,
				firstVertex = 0,
				vertexCount = oldSmDesc.vertexCount,
				bounds = oldSmDesc.bounds,
				indexCount = indexCount,
				topology = oldSmDesc.topology
			};

			splitMesh.SetSubMesh(0, newSmDesc, noMeshUpdate);
			splitMesh.bounds = oldSmDesc.bounds;
			splitMesh.SetIndexBufferData(indexBuffer, 0, 0, indexCount, noMeshUpdate);
			profilerGetSubMeshInitMesh.End();



			

			profilerGetSubMesh.End();
			return splitMesh;
		}
		[BurstCompile]
		struct ReIndexBuffer16 : IJobParallelFor
		{
			public NativeArray<ushort> indexBuffer;
			[ReadOnly]
			public NativeArray<int> hashMap;

			public void Execute(int i)
			{
				indexBuffer[i] = (ushort)hashMap[indexBuffer[i]];
			}
		}

		[BurstCompile]
		struct FlagUsedVertices : IJobParallelFor
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<int> hashMap;

			[ReadOnly]
			public NativeArray<ushort> indexBuffer;

			public void Execute(int i)
			{
				
				unsafe {
					Interlocked.Increment(ref ((int*)hashMap.GetUnsafePtr())[indexBuffer[i]]);
					}
			}
		}

		[BurstCompile]
		struct FlagUsedVerticesSerial : IJob
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<int> hashMap;

			[ReadOnly]
			public NativeArray<ushort> indexBuffer;

			public NativeReference<int> maxIdx;

			public void Execute()
			{
				int numIdx = indexBuffer.Length;
				int maxIdxLocal = 0;
				for (int i = 0; i < numIdx; i++) 
				{
					int idx = indexBuffer[i];
					hashMap[idx] = 1;
					maxIdxLocal = math.max(maxIdxLocal, idx);
				}
				maxIdx.Value = maxIdxLocal;
			}
		}

		[BurstCompile]
		struct PopulateVtxBufferSerial : IJob
		{
			[NativeDisableParallelForRestriction]
			public NativeArray<int> hashMap;

			public NativeArray<int> tempVertexBuffer;
			[ReadOnly]
			public NativeArray<int> oldVertexBuffer;

			public NativeReference<int> vtxCount;

			public int vertexIntStride;
			public int maxIndex;

			public unsafe void Execute()
			{
				int vtxCountLocal = 0;
				int* tempVertexBufferPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(tempVertexBuffer);
				int* oldVertexBufferPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(oldVertexBuffer);
				long vtxStructSize = sizeof(int) * vertexIntStride;
				for (int i = 0; i <= maxIndex; i++)
				{
					if (hashMap[i] > 0)
					{
						
						hashMap[i] = vtxCountLocal;
						//CopyUnsafe<int>(oldVertexBuffer, i * vertexStride, tempVertexBuffer, vtxIdx * vertexStride, vertexStride);
						UnsafeUtility.MemCpy(tempVertexBufferPtr + vtxCountLocal * vertexIntStride, oldVertexBufferPtr + i * vertexIntStride, vtxStructSize);
						vtxCountLocal++;
					}
				}
				vtxCount.Value = vtxCountLocal;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private unsafe static void CopyUnsafe<T>(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length) where T : struct
		{
			UnsafeUtility.MemCpy((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(dst) + dstIndex * UnsafeUtility.SizeOf<T>(), 
				(byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(src) + srcIndex * UnsafeUtility.SizeOf<T>(), length * UnsafeUtility.SizeOf<T>());
		}

		static Mesh[] SplitBySubmesh16(Mesh mesh, Mesh.MeshData meshData)
		{
			profilerSplitBySubMesh.Begin();
			int subMeshCount = meshData.subMeshCount;
			if (subMeshCount <= 1)
			{
				return new Mesh[1] { mesh };
			}
			Mesh[] outMeshes = new Mesh[subMeshCount];
			int vertexBufferStride = meshData.GetVertexBufferStride(0);
			NativeArray<int> oldVertexBuffer = meshData.GetVertexData<int>(0);
			NativeArray<int> tempVertexBuffer = new NativeArray<int>(meshData.vertexCount * (vertexBufferStride / sizeof(int)), Allocator.TempJob);
			NativeArray<int> hashMap = new NativeArray<int>(meshData.vertexCount, Allocator.TempJob);
			try
			{
				for (int smIdx = 0; smIdx < subMeshCount; smIdx++)
				{
					SubMeshDescriptor subMeshDescriptor = meshData.GetSubMesh(smIdx);
					NativeArray<ushort> indexBuffer = new NativeArray<ushort>(subMeshDescriptor.indexCount, Allocator.TempJob);
					meshData.GetIndices(indexBuffer, smIdx, true);
					outMeshes[smIdx] = GetSplitMesh(
						mesh, 
						ref subMeshDescriptor, 
						ref indexBuffer, 
						ref oldVertexBuffer,
						ref tempVertexBuffer,
						ref hashMap,
						vertexBufferStride
						);
					indexBuffer.Dispose();
				}
			}
			finally
			{
				tempVertexBuffer.Dispose();
				hashMap.Dispose();
			}
			profilerSplitBySubMesh.End();
			return outMeshes;
		}

		public static Mesh[] SplitBySubmesh(Mesh mesh, Mesh.MeshData meshData)
		{

			if (meshData.indexFormat == IndexFormat.UInt16)
			{
				return SplitBySubmesh16(mesh, meshData);
			}
			else
			{
				Debug.LogError("Could not split mesh, 32 bit index buffer not implemented yet");
				return null;
			}
		}


	}
}
