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
		public bool[] overrideVtxFmt;

		public EditorCombineRendererSettings(string target = "Default")
		{
			this.buildTarget = target;
			overrideBuildTarget = false;
			settings = new CombineRendererSettings(true);
			overrideVtxFmt = new bool[settings.vtxFormatCompression.Length];
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

			outp.vtxFormatCompression[0] = crs.overrideVtxFmt[0] ?
				crs.settings.vtxFormatCompression[0] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Position) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[1] = crs.overrideVtxFmt[1] ?
				crs.settings.vtxFormatCompression[1] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Normal) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[2] = crs.overrideVtxFmt[2] ?
				crs.settings.vtxFormatCompression[2] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Tangent) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[3] = crs.overrideVtxFmt[3] ?
				crs.settings.vtxFormatCompression[3] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.Color) == 0 ? VtxFormats.Float32 : VtxFormats.UNorm8;
			outp.vtxFormatCompression[4] = crs.overrideVtxFmt[4] ?
				crs.settings.vtxFormatCompression[4] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord0) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[5] = crs.overrideVtxFmt[5] ?
				crs.settings.vtxFormatCompression[5] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord1) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[6] = crs.overrideVtxFmt[6] ?
				crs.settings.vtxFormatCompression[6] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord2) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;
			outp.vtxFormatCompression[7] = crs.overrideVtxFmt[7] ?
				crs.settings.vtxFormatCompression[7] :
				(vertexCompressionFlags & (int)VertexChannelCompressionFlags.TexCoord3) == 0 ? VtxFormats.Float32 : VtxFormats.Float16;

			int numVtxChannels = outp.vtxFormatCompression.Length;

			for (int i = 8; i < numVtxChannels; i++)
			{
				outp.vtxFormatCompression[i] = crs.overrideVtxFmt[i] ?
					crs.settings.vtxFormatCompression[i] : VtxFormats.Float32;
			}

			return outp;
		}
	}
}
