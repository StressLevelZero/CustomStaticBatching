// Derived from https://github.com/keijiro/Akvfx/blob/master/Packages/jp.keijiro.akvfx/Runtime/Internal/Extensions.cs

using UnityEngine;
using System;
using System.Reflection;
using UnityEngine.Rendering;

namespace SLZ.CustomStaticBatching
{
	public static class CSBBufferExt
	{
		// SetData with ReadOnlySpan
		public unsafe static void SetFromSpan<T>
		  (ComputeBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetData(buffer, (IntPtr)pData, data.Length, sizeof(T));
		}

		public unsafe static void SetFromSpan<T>
			(ComputeBuffer buffer, Span<T> data) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetData(buffer, (IntPtr)pData, data.Length, sizeof(T));
		}

		public unsafe static void SetFromSpan<T>
			(ComputeBuffer buffer, ReadOnlySpan<T> data, int spanStartIndex, int bufferStartIndex, int count) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetCBNativeDelegate(buffer, (IntPtr)pData, spanStartIndex, bufferStartIndex, count, sizeof(T));
		}

		public unsafe static void SetFromSpan<T>
			(ComputeBuffer buffer, Span<T> data, int spanStartIndex, int bufferStartIndex, int count) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetCBNativeDelegate(buffer, (IntPtr)pData, spanStartIndex, bufferStartIndex, count, sizeof(T));
		}

		public unsafe static void CmdSetFromSpan<T>
		  (CommandBuffer cmd, ComputeBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				CmdSetData(cmd, buffer, (IntPtr)pData, data.Length, sizeof(T));
		}

		public unsafe static void CmdSetFromSpan<T>
			(CommandBuffer cmd, ComputeBuffer buffer, Span<T> data) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				CmdSetData(cmd, buffer, (IntPtr)pData, data.Length, sizeof(T));
		}

		public unsafe static void CmdSetFromSpan<T>
			(CommandBuffer cmd, ComputeBuffer buffer, ReadOnlySpan<T> data, int spanStartIndex, int bufferStartIndex, int count) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetCmdNativeDelegate(cmd, buffer, (IntPtr)pData, spanStartIndex, bufferStartIndex, count, sizeof(T));
		}

		public unsafe static void CmdSetFromSpan<T>
			(CommandBuffer cmd, ComputeBuffer buffer, Span<T> data, int spanStartIndex, int bufferStartIndex, int count) where T : unmanaged
		{
			fixed (T* pData = &data.GetPinnableReference())
				SetCmdNativeDelegate(cmd, buffer, (IntPtr)pData, spanStartIndex, bufferStartIndex, count, sizeof(T));
		}

		// Directly load an unmanaged data array to a compute buffer via an
		// Intptr. This is not a public interface so will be broken one day.
		// DO NOT TRY AT HOME.
		static void SetData
		  (ComputeBuffer buffer, IntPtr pointer, int count, int stride)
		{
			/*
			_args5[0] = pointer;
			_args5[1] = 0;      // source offset
			_args5[2] = 0;      // buffer offset
			_args5[3] = count;
			_args5[4] = stride;

			SetNativeData.Invoke(buffer, _args5);
			*/
			SetCBNativeDelegate(buffer, pointer, 0, 0, count, stride);
		}

		static void CmdSetData
		(CommandBuffer cmd, ComputeBuffer buffer, IntPtr pointer, int count, int stride)
		{
			/*
			_args6[0] = buffer;
			_args6[1] = pointer;
			_args6[2] = 0;      // source offset
			_args6[3] = 0;      // buffer offset
			_args6[4] = count;
			_args6[5] = stride;

			CmdSetNativeData.Invoke(cmd, _args6);
			*/
			SetCmdNativeDelegate(cmd, buffer, pointer, 0, 0, count, stride);
		}

		static MethodInfo _setNativeData;
		static MethodInfo _cmdSetNativeData;

		static MethodInfo SetNativeData
		  => _setNativeData ?? (_setNativeData = GetSetNativeDataMethod());

		static MethodInfo CmdSetNativeData
			=> _cmdSetNativeData ?? (_cmdSetNativeData = GetCmdSetNativeDataMethod());

		static MethodInfo GetSetNativeDataMethod()
		  => typeof(ComputeBuffer).GetMethod("InternalSetNativeData",
											 BindingFlags.InvokeMethod |
											 BindingFlags.NonPublic |
											 BindingFlags.Instance);
		static MethodInfo GetCmdSetNativeDataMethod()
		  => typeof(CommandBuffer).GetMethod("InternalSetComputeBufferNativeData",
											 BindingFlags.InvokeMethod |
											 BindingFlags.NonPublic |
											 BindingFlags.Instance);

		static Action<ComputeBuffer, IntPtr, int, int, int, int> _setCBNativeDelegate;
		static Action<CommandBuffer, ComputeBuffer, IntPtr, int, int, int, int> _setCmdNativeDelegate;

		static Action<ComputeBuffer, IntPtr, int, int, int, int> GetCBNativeDelegate()
		{
			MethodInfo method = GetSetNativeDataMethod();
			return (Action<ComputeBuffer, IntPtr, int, int, int, int>)Delegate.CreateDelegate(typeof(Action<ComputeBuffer, IntPtr, int, int, int, int>), method);
		}

		static Action<CommandBuffer, ComputeBuffer, IntPtr, int, int, int, int> GetCmdNativeDelegate()
		{
			MethodInfo method = GetCmdSetNativeDataMethod();
			return (Action<CommandBuffer, ComputeBuffer, IntPtr, int, int, int, int>)Delegate.CreateDelegate(typeof(Action<CommandBuffer, ComputeBuffer, IntPtr, int, int, int, int>), method);
		}

		static Action<ComputeBuffer, IntPtr, int, int, int, int> SetCBNativeDelegate => _setCBNativeDelegate ?? (_setCBNativeDelegate = GetCBNativeDelegate());
		static Action<CommandBuffer, ComputeBuffer, IntPtr, int, int, int, int> SetCmdNativeDelegate => _setCmdNativeDelegate ?? (_setCmdNativeDelegate = GetCmdNativeDelegate());

		//static object[] _args5 = new object[5];
		//static object[] _args6 = new object[6];
	}

}
