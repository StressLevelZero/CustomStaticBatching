using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor;
using System.IO;

namespace SLZ.CustomStaticBatching.Editor
{
	public class SBSettingsSO : ScriptableObject
	{
		public const string settingsPath = "Assets/Settings/SLZStaticBatchingSettings.asset";
		public bool executeInPlayMode = true;
		private static SBSettingsSO m_globalSettings;

		const int currentSettingsVersion = 2;
		public int thisSettingsVersion = currentSettingsVersion;
		public EditorCombineRendererSettings defaultSettings;
		public List<EditorCombineRendererSettings> platformOverrideSettings;

		public SBSettingsSO()
		{
			defaultSettings = new EditorCombineRendererSettings();
			platformOverrideSettings = new List<EditorCombineRendererSettings>();
		}

		private void OnEnable()
		{
			UpdateSettings();
			BuildPlatformsMirror.buildPlatformInfo[] buildPlatforms = BuildPlatformsMirror.ValidBuildPlatforms;
			if (platformOverrideSettings == null || platformOverrideSettings.Count == 0)
			{
				platformOverrideSettings = new List<EditorCombineRendererSettings>(buildPlatforms.Length);
				for (int i = 0; i < buildPlatforms.Length; i++)
				{
					EditorCombineRendererSettings ps = new EditorCombineRendererSettings(buildPlatforms[i].buildTarget.TargetName);
					platformOverrideSettings.Add(ps);
				}
			}
			else
			{
				HashSet<string> oldTargets = new HashSet<string>();
				for (int i=0; i < platformOverrideSettings.Count; i++)
				{
					oldTargets.Add(platformOverrideSettings[i].buildTarget);
				}
				for (int i = 0; i < buildPlatforms.Length; i++)
				{
					if (!oldTargets.Contains(buildPlatforms[i].buildTarget.TargetName))
						platformOverrideSettings.Add(new EditorCombineRendererSettings(buildPlatforms[i].buildTarget.TargetName));
				}
			}
		}

		private void UpdateSettings()
		{
			// Added unorm16 and snorm16
			if (thisSettingsVersion == 1)
			{
				
				for( int i = 0; i < 12; i++)
				{
					if (defaultSettings.settings.serializedVtxFormats[i] > 2)
					{
						defaultSettings.settings.serializedVtxFormats[i] += 2;
					}
				}
				int numPlatforms = platformOverrideSettings.Count;
				for (int pIdx = 0; pIdx < numPlatforms; pIdx++)
				{
					EditorCombineRendererSettings platSettings = platformOverrideSettings[pIdx];
					for (int i = 0; i < 12; i++)
					{
						if (platSettings.settings.serializedVtxFormats[i] > 2)
						{
							platSettings.settings.serializedVtxFormats[i] += 2;
						}
					}
				}
				thisSettingsVersion = 2;
				EditorUtility.SetDirty(this);
			}

		}

		public CombineRendererSettings GetActiveBuildTargetSettings()
		{
			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
			return GetBuildTargetSettings(target);
		}

		public CombineRendererSettings GetBuildTargetSettings(BuildTarget target)
		{
			string targetName = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target)).TargetName;
			EditorCombineRendererSettings settings = defaultSettings;
			for (int i = 0; i < platformOverrideSettings.Count; i++)
			{
				if (platformOverrideSettings[i].buildTarget == targetName)
				{
					if (platformOverrideSettings[i].overrideBuildTarget)
					{
						settings = platformOverrideSettings[i];
					}
					break;
				}
			}

			return EditorCombineRendererSettings.ApplyProjectSettingsCompression(settings);
		}
		public static SBSettingsSO GlobalSettings
		{
			get
			{
				if (m_globalSettings == null)
					m_globalSettings = AssetDatabase.LoadAssetAtPath<SBSettingsSO>(settingsPath);
				if (m_globalSettings == null)
				{
					m_globalSettings = ScriptableObject.CreateInstance<SBSettingsSO>();
					string settingsDir = Path.Combine(
							Application.dataPath,
							"Settings"
							);
					if (!Directory.Exists(settingsDir))
					{
						AssetDatabase.CreateFolder("Assets", "Settings");
					}
					AssetDatabase.CreateAsset(m_globalSettings, settingsPath);
				}
				return m_globalSettings;
			}
		}
	}

	
}