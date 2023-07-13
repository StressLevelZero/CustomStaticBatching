using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Search;
using UnityEngine.UIElements;
using System.Reflection;
using Unity.Plastic.Newtonsoft.Json.Serialization;
using System;
using UnityEditor.UIElements;
using UnityEditor.Toolbars;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor.Build;
using static SLZ.CustomStaticBatching.SBSettingsProvider;
using System.Linq;
using static UnityEngine.GraphicsBuffer;
using System.Drawing.Printing;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SLZ.CustomStaticBatching
{
	public class SBSettingsSO : ScriptableObject
	{
		public const string settingsPath = "Assets/Settings/SLZStaticBatchingSettings.asset";
		private static SBSettingsSO m_globalSettings;
		

		public EditorCombineRendererSettings defaultSettings;
		public List<EditorCombineRendererSettings> platformOverrideSettings;



		public SBSettingsSO()
		{
			defaultSettings = new EditorCombineRendererSettings();
			platformOverrideSettings = new List<EditorCombineRendererSettings>();
		}

#if UNITY_EDITOR
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
#endif


#if UNITY_EDITOR


		public CombineRendererSettings GetActiveBuildTargetSettings()
		{
			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
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
#endif

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

	#region EditorOnly
#if UNITY_EDITOR

	

	static class SBSettingsProvider
	{
		[SettingsProvider]
		public static SettingsProvider CreateSBSettingsProvider()
		{

			System.Action<string, VisualElement> uiAction = SBSettingsUI;
			SettingsProvider provider = new SettingsProvider("Project/SLZCustomStaticBatching", SettingsScope.Project)
			{
				label = "Custom Static Batching",
				activateHandler = uiAction,
			};
			return provider;
		}

		public static void SBSettingsUI(string searchContext, VisualElement rootElement)
		{
			
			SerializedObject globalSettings = new SerializedObject(SBSettingsSO.GlobalSettings);
			VisualElement margin = new VisualElement();
			margin.style.paddingLeft = 8;
			margin.style.paddingRight = 8;
			margin.style.paddingTop = 4;
			margin.style.flexDirection = FlexDirection.Column;
			margin.style.flexGrow = 1;
			margin.style.alignItems = Align.Stretch;
			margin.style.flexShrink = 1;
			margin.style.flexBasis = StyleKeyword.Auto;

			Label title = new Label("<b>Custom Static Batching</b>");
			title.style.fontSize = 20;
			title.style.paddingBottom = 8;
			
			


			VisualElement testButtonStrip = new VisualElement();
			//testButtonStrip.style.marginTop = 4;
			testButtonStrip.style.flexShrink = 0;

			testButtonStrip.style.flexDirection= FlexDirection.Row;
			testButtonStrip.style.justifyContent = Justify.Center;




			BuildPlatformsMirror.buildPlatformInfo[] buildPlatforms = BuildPlatformsMirror.ValidBuildPlatforms;

			int numPlatforms = buildPlatforms.Length;
			TabBox tabBox = new TabBox(numPlatforms + 1);
			tabBox.tabs[0].text = "Default";

			for (int i = 0; i < numPlatforms; i++)
			{
				if (buildPlatforms[i].icon != null)
				{
					tabBox.tabs[i+1].icon = buildPlatforms[i].icon;
				}
				else
				{
					tabBox.tabs[i + 1].text = buildPlatforms[i].name;
				}
				tabBox.tabs[i+1].tooltip = buildPlatforms[i].tooltip;
			}

			PlatformSettingsPage[] settingsPage = new PlatformSettingsPage[numPlatforms + 1];

			settingsPage[0] = new PlatformSettingsPage(globalSettings);

			tabBox.tabPages[0].Add(settingsPage[0]);

			for (int i = 1; i <= numPlatforms; i++)
			{
				settingsPage[i] = new PlatformSettingsPage(globalSettings, buildPlatforms[i-1].buildTarget.TargetName);
				tabBox.tabPages[i].Add(settingsPage[i]);
			}

			rootElement.Add(margin);
			margin.Add(title);
			margin.Add(tabBox);
			//EditorToolbarUtility.SetupChildrenAsButtonStrip(testButtonStrip);
		}

		class PlatformSettingsPage : VisualElement
		{
			VisualElement toggleGroup;
			public readonly int index;
			public readonly string rootBindingPath;

			public PlatformSettingsPage(SerializedObject settings)
			{
				index = -1;
				rootBindingPath = "defaultSettings.";
				InitializePage(settings, this);
			}
			public PlatformSettingsPage(SerializedObject settings, string buildTargetName)
			{
				index = 0;
				SBSettingsSO globalSettings = SBSettingsSO.GlobalSettings;
				int settingsCount = globalSettings.platformOverrideSettings.Count;
				for (; index < settingsCount; index++)
				{
					if (buildTargetName == globalSettings.platformOverrideSettings[index].buildTarget)
						break;
				}

				rootBindingPath = "platformOverrideSettings.Array.data[" + index + "].";


				Toggle enableOverride = new Toggle("Override Default Settings");

				enableOverride.tooltip = "Use settings specific to this platform rather than the global defaults";
				enableOverride.bindingPath = rootBindingPath + "overrideBuildTarget";
				SetToggleStyle(enableOverride);
				enableOverride.BindProperty(settings);
				enableOverride.RegisterValueChangedCallback(OnOverrideToggled);
				enableOverride.style.paddingBottom = 8;

				style.flexGrow = 1;
				style.flexDirection = FlexDirection.Column;
				style.flexGrow = 1;
				style.alignItems = Align.Stretch;
				style.flexShrink = 1;
				style.flexBasis = StyleKeyword.Auto;

				toggleGroup = new VisualElement();
				toggleGroup.SetEnabled(globalSettings.platformOverrideSettings[index].overrideBuildTarget);				

				Add(enableOverride);
				Add(toggleGroup);

				InitializePage(settings, toggleGroup);
			}

			void InitializePage(SerializedObject settings, VisualElement root)
			{
				root.style.flexGrow = 1;
				root.style.flexDirection = FlexDirection.Column;
				root.style.flexGrow = 1;
				root.style.alignItems = Align.Stretch;
				root.style.flexShrink = 1;
				root.style.flexBasis = StyleKeyword.Auto;

				Label NotImplHeader = new Label("\nNot Yet Implemented");

				Toggle splitMultiMeshes = new Toggle("Split Multi-Material Renderers (DANGEROUS!)");
				splitMultiMeshes.tooltip =
					"Splits Renderers with multi-material meshes into several single material renderers " +
					"parented to the original game object. This is slow, and will break scripts and animations " +
					"that expect to be able to change the materials on or change the state of the whole mesh";
				splitMultiMeshes.bindingPath = rootBindingPath + "settings.splitMultiMaterialMeshes";
				splitMultiMeshes.SetEnabled(false);
				SetToggleStyle(splitMultiMeshes);
				splitMultiMeshes.BindProperty(settings);

				Toggle highPIdx = new Toggle("Allow 32 bit index combined meshes");
				highPIdx.bindingPath = rootBindingPath + "settings.allow32bitIdx";
				highPIdx.tooltip = "Allow making combined meshes of 32-bit index buffer meshes. This sorts the 32 bit meshes into their own combined meshes.";
				SetToggleStyle(highPIdx);
				highPIdx.BindProperty(settings);

				root.Add(highPIdx);
				root.Add(NotImplHeader);
				root.Add(splitMultiMeshes);
			}

			private void OnOverrideToggled(ChangeEvent<bool> evt)
			{
				toggleGroup.SetEnabled(evt.newValue);
			}
		}
		static void SetToggleStyle(VisualElement el)
		{
			el.style.alignItems = Align.Stretch;
			el.style.justifyContent = Justify.FlexStart;

			VisualElement label = el.ElementAt(0);
			label.style.flexGrow = 1f;
			label.style.flexBasis = 1f;
			label.style.flexShrink = 1f;
			label.style.textOverflow = TextOverflow.Ellipsis;
			label.style.overflow = Overflow.Hidden;
			label.style.unityTextOverflowPosition = TextOverflowPosition.End;
			VisualElement checkmark = el.ElementAt(1);
			checkmark.style.flexGrow = 1f;
			checkmark.style.flexBasis = 1;
			checkmark.style.flexShrink = 0.25f;
			checkmark.style.flexDirection = FlexDirection.Row;
		}



		static class TabStyle
		{
			public const int borderWidth = 1;
			public const int borderRadius = 3;

			// --unity-colors-app_toolbar_button-background-hover
			public const uint backgroundDark = 0xFF424242;
			public const uint backgroundLight = 0xFFBBBBBB;
			public static StyleColor background = EditorGUIUtility.isProSkin ? intToColor(backgroundDark) : intToColor(backgroundLight);

			// --unity-colors-tab-background
			public const uint backgroundDisabledDark = 0xFF353535;
			public const uint backgroundDisabledLight = 0xFFB6B6B6;
			public static StyleColor backgroundDisabled = EditorGUIUtility.isProSkin ? intToColor(backgroundDisabledDark) : intToColor(backgroundDisabledLight);

			// --unity-colors-tab-background-hover
			public const uint hoverDisabledDark = 0xFF303030;
			public const uint hoverDisabledLight = 0xFFB0B0B0;
			public static StyleColor hoverDisabled = EditorGUIUtility.isProSkin ? intToColor(hoverDisabledDark) : intToColor(hoverDisabledLight);


			public const uint borderDark = 0xFF232323;
			public const uint borderLight = 0xFF999999;
			public static StyleColor border = EditorGUIUtility.isProSkin ? intToColor(borderDark) : intToColor(borderLight);

			public static StyleColor intToColor(uint hexColor)
			{
				return new StyleColor(((Color)UnsafeUtility.As<uint, Color32>(ref hexColor)));
			}
		}

		class Tab : VisualElement
		{

			bool isActive;
			bool hover;
			bool click;

			Color32 colorInactive;
			Color32 colorActive;

			Color32 colorInactiveHighlighted;
			Color32 colorActiveHighlighted;

			public TabBox tabParent;
			public int index;

			string m_text;
			Label m_label;

			public string text { 
				get => m_text;
				set {
					if (m_label == null)
					{
						m_label = new Label();
						Add(m_label);
					}
					m_label.text = value;
					}
			}

			Image m_icon;
			public Texture icon
			{
				get => m_icon?.image;
				set
				{
					if (m_icon == null)
					{
						m_icon = new Image() { image = value };
						Add(m_icon);
					}
					else
						m_icon.image = value;
				}
			}
			
			public Tab(string text)
			{
				Init();
				Add(new Label(text));
			}

			public Tab(Texture2D image)
			{
				Init();
				Add(new Image() { image = image });
			}

			public Tab() 
			{
				Init();
			}

			void Init()
			{

				SetInitialStyle();
				RegisterCallback<MouseEnterEvent>(delegate { hover = true; UpdateState(); });
				RegisterCallback<MouseLeaveEvent>(delegate { hover = false; UpdateState(); });
				RegisterCallback<MouseDownEvent>(delegate { click = true; UpdateState(); });
				RegisterCallback<MouseUpEvent>(delegate { click = false; SendEventToTab(); UpdateState(); });
			}

			void SendEventToTab()
			{
				tabParent.SetActiveTab(index);
			}

			void UpdateState()
			{
				if (isActive) 
				{
					style.borderBottomWidth = 0;
					if (click)
					{
						style.backgroundColor = TabStyle.background;
					}
					else if (hover)
					{
						style.backgroundColor = TabStyle.background;
					}
					else
					{
						style.backgroundColor = TabStyle.background;
					}
				}
				else
				{
					style.borderBottomWidth = TabStyle.borderWidth;
					if (click)
					{
						style.backgroundColor = TabStyle.backgroundDisabled;
					}
					else if (hover)
					{
						style.backgroundColor = TabStyle.hoverDisabled;
					}
					else
					{
						style.backgroundColor = TabStyle.backgroundDisabled;
					}
				}
			}

			void SetInitialStyle()
			{
				style.backgroundColor = TabStyle.backgroundDisabled;
				style.borderTopColor = style.borderLeftColor = style.borderRightColor = style.borderBottomColor = TabStyle.border;
				style.borderTopLeftRadius = 0;
				style.borderTopRightRadius = 0;
				style.flexGrow = 1f;
				style.flexBasis = 0;
				style.borderBottomWidth = TabStyle.borderWidth;
				style.borderLeftWidth = TabStyle.borderWidth;
				style.borderRightWidth = 0;
				style.borderTopWidth = TabStyle.borderWidth;
				style.marginLeft = 0;
				style.marginRight = 0;
				style.paddingTop = 2;
				style.paddingBottom = 2;

				style.alignContent = Align.Center;
				style.alignItems = Align.Center;
			}

			internal void SetLeftEnd()
			{
				style.borderTopLeftRadius = TabStyle.borderRadius;
			}

			internal void SetRightEnd()
			{
				style.borderTopRightRadius = TabStyle.borderRadius;
				style.borderRightWidth = TabStyle.borderWidth;
			}


			public void SetActiveState(bool state)
			{
				isActive = state;
				UpdateState();
			}


		}

		public class TabPage : VisualElement
		{
			public TabPage()
			{
				SetInitialStyle();
			}
			void SetInitialStyle()
			{
				style.backgroundColor = TabStyle.background;
				style.borderTopColor = style.borderLeftColor = style.borderRightColor = style.borderBottomColor = TabStyle.border;
				style.borderTopLeftRadius = 0;
				style.borderTopRightRadius = 0;
				style.borderBottomLeftRadius = TabStyle.borderRadius;
				style.borderBottomRightRadius = TabStyle.borderRadius;
				style.flexGrow = 1f;
				style.flexBasis = 0;
				style.borderBottomWidth = TabStyle.borderWidth;
				style.borderLeftWidth = TabStyle.borderWidth;
				style.borderRightWidth = TabStyle.borderWidth;
				style.borderTopWidth = 0;
				style.marginLeft = 0;
				style.marginRight = 0;
				style.paddingTop = 8;
				style.paddingLeft = 8;
				style.paddingRight = 8;
				style.paddingBottom = 4;

				style.flexGrow = 1;
				style.flexDirection = FlexDirection.Column;
				style.flexGrow = 1;
				style.alignItems = Align.Stretch;
				style.flexShrink = 1;
				style.flexBasis = StyleKeyword.Auto;
			}
		}

		class TabBox : VisualElement
		{
			public int activeBox = 0;
			public Tab[] tabs;
			public TabPage[] tabPages;
			

			public TabBox(int tabCount)
			{
				this.tabs = new Tab[tabCount];
				this.tabPages = new TabPage[tabCount];
				VisualElement tabHeader = new VisualElement();
				int lastTab = tabCount - 1;
				for  (int i = 0; i < tabCount; i++) 
				{
					tabs[i] = new Tab();
					if (i == 0)
					{
						tabs[i].SetActiveState(true);
						tabs[i].SetLeftEnd();
					}
					if (i == lastTab) tabs[i].SetRightEnd();
					tabs[i].index = i;
					tabs[i].tabParent = this;
					tabHeader.Add(tabs[i]);
				}
				tabHeader.style.flexShrink = 0;
				tabHeader.style.flexDirection = FlexDirection.Row;
				tabHeader.style.justifyContent = Justify.Center;
				Add(tabHeader);
				for (int i = 0; i < tabCount; i++)
				{
					tabPages[i] = new TabPage();
					if (i != 0) tabPages[i].style.display = DisplayStyle.None;
					Add(tabPages[i]);
				}
				style.flexShrink = 0;
				style.flexDirection = FlexDirection.Column;
				style.justifyContent = Justify.Center;

			}

			public void SetActiveTab(int tabIndex)
			{
				for (int i =0; i < tabs.Length; i++)
				{
					if (tabs[i].index == tabIndex)
					{
						tabPages[i].style.display = DisplayStyle.Flex;
						tabs[i].SetActiveState(true);
					}
					else
					{
						tabPages[i].style.display = DisplayStyle.None;
						tabs[i].SetActiveState(false);
					}
				}
			}


		}
	}

#endif
#endregion
}