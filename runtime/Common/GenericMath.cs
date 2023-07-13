using Unity.Collections;
using System.Runtime.CompilerServices;


namespace SLZ.CustomStaticBatching
{
	public interface IGenericInt<T>
	{
		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int ToInt(T value);

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T ToType(int other);

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Add(T value, int other);
	}

	public struct GenericInt32 : IGenericInt<int>
	{
		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int ToInt(int value)
		{
			return value;
		}

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int ToType(int value)
		{
			return value;
		}

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Add(int value, int other)
		{
			return value + other;
		}
	}

	public struct GenericInt16 : IGenericInt<ushort>
	{
		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int ToInt(ushort value)
		{
			return (int)value;
		}

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ushort ToType(int value)
		{
			return (ushort)value;
		}

		[BurstCompatible]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ushort Add(ushort value, int other)
		{
			return (ushort)(value + other);
		}
	}
}
