using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SLZ.CustomStaticBatching
{
	public struct RendererSortItem : IComparable<RendererSortItem>
	{
		public int rendererArrayIdx;

		public ushort materialCount;
		public ulong shaderID;
		//public ulong variantHash;
		public ulong materialID;
		public ushort lightmapIdx;
		public ushort probe0Id;
		public ushort probe1Id;
		public ulong hilbertIdx;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int CompareTo(RendererSortItem other)
		{
			if (materialCount != other.materialCount)
			{
				return materialCount > other.materialCount ? 1 : -1;
			}
			if (shaderID != other.shaderID)
			{
				return shaderID > other.shaderID ? 1 : -1;
			}
			//if (variantHash != other.variantHash)
			//{
			//	return variantHash > other.variantHash ? 1 : -1;
			//}
			if (materialID != other.materialID)
			{
				return materialID > other.materialID ? 1 : -1;
			}
			if (lightmapIdx != other.lightmapIdx)
			{
				return lightmapIdx > other.lightmapIdx ? 1 : -1;
			}
			if (probe0Id != other.probe0Id)
			{
				return probe0Id > other.probe0Id ? 1 : -1;
			}
			if (probe1Id != other.probe1Id)
			{
				return probe1Id > other.probe1Id ? 1 : -1;
			}
			if (hilbertIdx != other.hilbertIdx)
			{
				return probe1Id > other.probe1Id ? 1 : -1;
			}
			return 0;
		}
	}

	public static class ReflectKWFields
	{

		static Func<LocalKeyword, int> _localKW_Index;
		static void GetDelegate()
		{
			FieldInfo fieldInfo = typeof(LocalKeyword).GetField("m_Index");
			_localKW_Index = CreateGetter<LocalKeyword, int>(fieldInfo);
		}

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

		public static int GetIndex(LocalKeyword kw)
		{
			if (_localKW_Index == null)
			{
				GetDelegate();
			}
			return _localKW_Index.Invoke(kw);
		}
	}
}
