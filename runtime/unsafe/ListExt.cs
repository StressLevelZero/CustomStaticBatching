using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public unsafe static class CSBListExt
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T[] GetInternalArray<T>(List<T> list)
	{
		if (list == null)
		{
			return null;
		}
		ListInternals<T> tListAccess = UnsafeUtility.As<List<T>, ListInternals<T>>(ref list);
		return tListAccess._items;
	}

	// Copied from 2023's version of the NoAllocHelpers, names and order are magic and should not be changed
	private class ListInternals<T>
	{
		internal T[] _items;
		internal int _size;
		internal int _version;
	}
}
