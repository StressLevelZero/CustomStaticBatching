using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor;

namespace SLZ.CustomStaticBatching.Editor
{
	public class SBSettingsSO : ScriptableObject
	{
		public const string settingsPath = "Assets/Settings/SLZStaticBatchingSettings.asset";
		private static SBSettingsSO m_globalSettings;

		const int currentSettingsVersion = 1;
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
					AssetDatabase.CreateAsset(m_globalSettings, settingsPath);
				}
				return m_globalSettings;
			}
		}
	}

	
}