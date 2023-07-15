using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Build;
using UnityEditor;
using UnityEngine;

namespace SLZ.CustomStaticBatching
{
	public static class BuildPlatformsMirror
	{
		public struct buildPlatformInfo
		{
			public string name;
			public Texture2D icon;
			public string tooltip;
			public NamedBuildTarget buildTarget;
		}

		static buildPlatformInfo[] m_validBuildPlatforms;

		public static buildPlatformInfo[] ValidBuildPlatforms
		{
			get
			{
				if (m_validBuildPlatforms == null) m_validBuildPlatforms = GetValidBuildPlatforms();
				if (m_validBuildPlatforms == null) m_validBuildPlatforms = new buildPlatformInfo[0];
				return m_validBuildPlatforms;
			}
		}

		static buildPlatformInfo[] GetValidBuildPlatforms()
		{
			Assembly buildPlatformAssembly = Assembly.GetAssembly(typeof(BuildPlayerContext));
			if (buildPlatformAssembly == null)
			{
				Debug.LogError("Couldn't find buildPlatforms's assembly");
				return null;
			}
			Type buildPlatformsType = buildPlatformAssembly.GetType("UnityEditor.Build.BuildPlatforms");
			if (buildPlatformsType == null)
			{
				Debug.LogError("Couldn't find buildPlatforms's type");
				return null;
			}

			PropertyInfo buildPlatformsInstance = buildPlatformsType.GetProperty("instance");
			if (buildPlatformsInstance == null)
			{
				Debug.LogError("Couldn't find buildPlatforms's instance");
				return null;
			}

			Type buildPlatformType = buildPlatformAssembly.GetType("UnityEditor.Build.BuildPlatform");
			if (buildPlatformType == null)
			{
				Debug.LogError("Couldn't find buildPlatform's type");
				return null;
			}

			FieldInfo buildPlatformNameField = buildPlatformType.GetField("name");
			if (buildPlatformNameField == null)
			{
				Debug.LogError("Couldn't find buildPlatform's name field");
				return null;
			}

			FieldInfo buildPlatformTooltipField = buildPlatformType.GetField("tooltip");
			if (buildPlatformTooltipField == null)
			{
				Debug.LogError("Couldn't find buildPlatform's tooltip field");
				return null;
			}

			FieldInfo buildPlatformTargetField = buildPlatformType.GetField("namedBuildTarget");
			if (buildPlatformTargetField == null)
			{
				Debug.LogError("Couldn't find buildPlatform's namedBuildTarget field");
				return null;
			}

			PropertyInfo buildPlatformIconProperty = buildPlatformType.GetProperty("smallIcon");
			if (buildPlatformIconProperty == null)
			{
				Debug.LogError("Couldn't find buildPlatform's icon property");
				return null;
			}

			MethodInfo vaildBuildPlatformMethod = buildPlatformsType.GetMethod("GetValidPlatforms", 0, new Type[0]);
			if (vaildBuildPlatformMethod == null)
			{
				Debug.LogError("Couldn't find BuildPlatforms.GetValidPlatforms method");
				return null;
			}

			IEnumerable buildPlatformArray = (IEnumerable)vaildBuildPlatformMethod.Invoke(buildPlatformsInstance.GetValue(null), new object[0]);
			if (buildPlatformArray == null)
			{
				Debug.LogError("BuildPlatforms.GetValidPlatforms returned null");
				return null;
			}

			IEnumerator enumerator = buildPlatformArray.GetEnumerator();
			List<buildPlatformInfo> platformInfos = new List<buildPlatformInfo>();
			while (enumerator.MoveNext())
			{
				object platform = enumerator.Current;

				buildPlatformInfo info = new buildPlatformInfo()
				{
					name = buildPlatformNameField.GetValue(platform) as string,
					icon = buildPlatformIconProperty.GetValue(platform) as Texture2D,
					tooltip = buildPlatformTooltipField.GetValue(platform) as string,
					buildTarget = (NamedBuildTarget)buildPlatformTargetField.GetValue(platform),
				};
				platformInfos.Add(info);
			}
			return platformInfos.ToArray();
		}
	}
}
