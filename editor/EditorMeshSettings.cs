using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using static SLZ.CustomStaticBatching.PackedChannel;

namespace SLZ.CustomStaticBatching
{
	[Serializable]
	public class EditorCombineRendererSettings
	{
		public string buildTarget;
		public bool overrideBuildTarget;
		public CombineRendererSettings settings;

		public EditorCombineRendererSettings(string target = "Default")
		{
			this.buildTarget = target;
			overrideBuildTarget = false;
			settings = new CombineRendererSettings(false);
		}

		static SerializedObject GetProjectSettingsAsset()
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

		public static CombineRendererSettings ApplyProjectSettingsCompression(EditorCombineRendererSettings crs)
		{
			SerializedObject projectSettings = GetProjectSettingsAsset();
			CombineRendererSettings outp = new CombineRendererSettings(false);

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

			
			outp.serializedVtxFormats[0] = crs.settings.serializedVtxFormats[0] != 0 ?
				crs.settings.serializedVtxFormats[0] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Position) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			
			outp.serializedVtxFormats[1] = crs.settings.serializedVtxFormats[1] != 0 ?
				crs.settings.serializedVtxFormats[1] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Normal) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			outp.serializedVtxFormats[2] = crs.settings.serializedVtxFormats[2] != 0 ?
				crs.settings.serializedVtxFormats[2] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Tangent) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			outp.serializedVtxFormats[3] = crs.settings.serializedVtxFormats[3] != 0 ?
				crs.settings.serializedVtxFormats[3] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Color) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.UNorm8;
			outp.serializedVtxFormats[4] = crs.settings.serializedVtxFormats[4] != 0 ?
				crs.settings.serializedVtxFormats[4] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord0) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			outp.serializedVtxFormats[5] = crs.settings.serializedVtxFormats[5] != 0 ?
				crs.settings.serializedVtxFormats[5] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord1) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			outp.serializedVtxFormats[6] = crs.settings.serializedVtxFormats[6] != 0 ?
				crs.settings.serializedVtxFormats[6] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord2) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;
			outp.serializedVtxFormats[7] = crs.settings.serializedVtxFormats[7] != 0 ?
				crs.settings.serializedVtxFormats[7] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord3) == 0 ? (byte)VtxFormats.Float32 : (byte)VtxFormats.Float16;

			int numVtxChannels = outp.serializedVtxFormats.Length;

			for (int i = 8; i < numVtxChannels; i++)
			{
				outp.serializedVtxFormats[i] = crs.settings.serializedVtxFormats[i] != 0 ?
					crs.settings.serializedVtxFormats[i] : (byte)VtxFormats.Float32;
			}
			Debug.Log("Normal compression: " + outp.serializedVtxFormats[1]);
			return outp;
		}
	}
}
