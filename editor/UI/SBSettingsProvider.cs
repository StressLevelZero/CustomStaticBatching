using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using NUnit.Framework;
using static SLZ.CustomStaticBatching.PackedChannel;
using static UnityEngine.UI.InputField;
using System.Runtime.Remoting.Messaging;
using UnityEditor.UIElements.Bindings;
using System;
using static UnityEditor.Search.SearchValue;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;

namespace SLZ.CustomStaticBatching.Editor
{
	/// <summary>
	/// Menu that controls the global settings used by the static batcher in editor
	/// </summary>
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

			Toggle enableInPlayMode = new Toggle("Enable In Play Mode");
			enableInPlayMode.tooltip = "Run modified static batching in play mode in editor. As the editor runs static batching on scene load, this will cause hitching when asynchronously streaming scenes. Disabling this allows unity's default static batching to run instead";
			enableInPlayMode.bindingPath = "executeInPlayMode";
			enableInPlayMode.Bind(globalSettings);


			VisualElement testButtonStrip = new VisualElement();
			//testButtonStrip.style.marginTop = 4;
			testButtonStrip.style.flexShrink = 0;

			testButtonStrip.style.flexDirection = FlexDirection.Row;
			testButtonStrip.style.justifyContent = Justify.Center;


			BuildPlatformsMirror.buildPlatformInfo[] buildPlatforms = BuildPlatformsMirror.ValidBuildPlatforms;

			int numPlatforms = buildPlatforms.Length;
			TabBox tabBox = new TabBox(numPlatforms + 1);
			tabBox.tabs[0].text = "Default";

			for (int i = 0; i < numPlatforms; i++)
			{
				if (buildPlatforms[i].icon != null)
				{
					tabBox.tabs[i + 1].icon = buildPlatforms[i].icon;
				}
				else
				{
					tabBox.tabs[i + 1].text = buildPlatforms[i].name;
				}
				tabBox.tabs[i + 1].tooltip = buildPlatforms[i].tooltip;
			}

			PlatformSettingsPage[] settingsPage = new PlatformSettingsPage[numPlatforms + 1];

			settingsPage[0] = new PlatformSettingsPage(globalSettings);

			tabBox.tabPages[0].Add(settingsPage[0]);

			for (int i = 1; i <= numPlatforms; i++)
			{
				settingsPage[i] = new PlatformSettingsPage(globalSettings, buildPlatforms[i - 1].buildTarget.TargetName);
				tabBox.tabPages[i].Add(settingsPage[i]);
			}

			rootElement.Add(margin);
			margin.Add(title);
			margin.Add(enableInPlayMode);
			margin.Add(tabBox);
			//EditorToolbarUtility.SetupChildrenAsButtonStrip(testButtonStrip);
		}

		static string[] VtxFormatNames = new string[] { "Use Player Vertex Compression", "UNorm8", "SNorm8", "Float16", "Float32" };
		
		class PlatformSettingsPage : VisualElement
		{
			VisualElement toggleGroup;
			public readonly int index;
			public readonly string rootBindingPath;

			public PlatformSettingsPage(SerializedObject settings)
			{
				index = -1;
				rootBindingPath = "defaultSettings.";
				InitializePage(settings, false);
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


				InitializePage(settings, true);
			}

			void InitializePage(SerializedObject settings, bool isOverride)
			{
				VisualElement root;

				if (isOverride)
				{
					SBSettingsSO globalSettings = SBSettingsSO.GlobalSettings;
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
					root = toggleGroup;
				}
				else
				{
					root = this;
				}

				root.style.flexGrow = 1;
				root.style.flexDirection = FlexDirection.Column;
				root.style.flexGrow = 1;
				root.style.alignItems = Align.Stretch;
				root.style.flexShrink = 1;
				root.style.flexBasis = StyleKeyword.Auto;

				Toggle highPIdx = new Toggle("Allow 32 bit index combined meshes");
				highPIdx.bindingPath = rootBindingPath + "settings.allow32bitIdx";
				highPIdx.tooltip = "Allow making combined meshes of 32-bit index buffer meshes. This sorts the 32 bit meshes into their own set combined meshes, so it should not cause issues";
				SetToggleStyle(highPIdx);
				highPIdx.BindProperty(settings);

				Toggle normalize = new Toggle("Normalize normal and tangent");
				normalize.bindingPath = rootBindingPath + "settings.normalizeNormalTangent";
				normalize.tooltip = "Normalize normals and tangents after applying the object to world transform. This is default unity static batching behavior, but it will cause shaders to recieve different normal information vs unbatched. SNORM formats are always normalized.";
				SetToggleStyle(normalize);
				normalize.BindProperty(settings);

				IntegerField highPIdxCount = new IntegerField("Max vertices per 32-bit index combined mesh");
				highPIdxCount.isDelayed = true;
				highPIdxCount.tooltip = "The maximum number of vertices that can be in a 32-bit index buffer combined mesh. Since a 32-bit index buffer can represent trillions of vertices, its a good idea to arbitrarily put a cap on how large the combined mesh can be";
				highPIdxCount.bindingPath = rootBindingPath + "settings.maxCombined32Idx";
				SetToggleStyle(highPIdxCount);
				highPIdxCount.BindProperty(settings);
				highPIdxCount.RegisterValueChangedCallback((ChangeEvent<int> e) => { highPIdxCount.value = Math.Max( 0x10000, highPIdxCount.value); });

				Foldout vertexSettings = new Foldout();
				vertexSettings.text = "Vertex Attribute Format Settings";
				VisualElement columnLabels = new VisualElement();
				columnLabels.style.alignSelf = Align.Stretch;
				columnLabels.style.flexGrow = 1;
				columnLabels.style.flexDirection = FlexDirection.Row;
				columnLabels.style.alignItems = Align.Center;
				columnLabels.style.justifyContent = Justify.SpaceBetween;
				columnLabels.style.flexShrink = 1;
				columnLabels.style.flexBasis = StyleKeyword.Auto;


				Label channelLabel = new Label("<b>Attribute</b>");
				channelLabel.style.flexGrow =0.4f;
				channelLabel.style.flexBasis = 0.4f;
				channelLabel.style.flexShrink = 0.4f;
				channelLabel.style.textOverflow = TextOverflow.Ellipsis;
				channelLabel.style.overflow = Overflow.Hidden;
				channelLabel.style.unityTextOverflowPosition = TextOverflowPosition.End;
				channelLabel.tooltip = "Vertex Attribute";

				Label altStreamLabel = new Label("<b>Secondary Stream</b>");
				altStreamLabel.style.flexGrow = 0.6f;
				altStreamLabel.style.flexBasis = 0.6f;
				altStreamLabel.style.flexShrink = 0.6f;
				altStreamLabel.style.textOverflow = TextOverflow.Ellipsis;
				altStreamLabel.style.overflow = Overflow.Hidden;
				altStreamLabel.style.unityTextOverflowPosition = TextOverflowPosition.End;
				altStreamLabel.tooltip = "Split the vertex buffer and put the marked attributes in a secondary vertex stream. Useful for many tile-based mobile GPUs, you should put any attributes that do not affect the vertex position into the secondary stream";

				Label maxPrecisionLabel = new Label("<b>Maximum Precision</b>");
				maxPrecisionLabel.style.flexGrow = 1f;
				maxPrecisionLabel.style.flexBasis = 1f;
				maxPrecisionLabel.style.flexShrink = 1f;
				maxPrecisionLabel.style.textOverflow = TextOverflow.Ellipsis;
				maxPrecisionLabel.style.overflow = Overflow.Hidden;
				maxPrecisionLabel.style.unityTextOverflowPosition = TextOverflowPosition.End;


				columnLabels.Add(channelLabel);
				columnLabels.Add(altStreamLabel);
				columnLabels.Add(maxPrecisionLabel);

				vertexSettings.Add(columnLabels);

				// vertex channel compression
				List<int> normTanFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.SNorm8, 0};
				VisualElement normField = VtxCompressionOption("Normal", settings, rootBindingPath, 1, normTanFmtsInt);
			
				vertexSettings.contentContainer.Add(normField);

				VisualElement tanField = VtxCompressionOption("Tangent", settings, rootBindingPath, 2, normTanFmtsInt);
				vertexSettings.contentContainer.Add(tanField);

				List<int> colorFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.UNorm8, 0 };
				VisualElement colorField = VtxCompressionOption("Color", settings, rootBindingPath, 3, colorFmtsInt);
			
				vertexSettings.contentContainer.Add(colorField);

				List<int> uvFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.SNorm8, (int)VtxFormats.UNorm8, 0 };
				
				for (int i = 0; i < 8; i++)
				{
					VisualElement uvField = VtxCompressionOption("UV" + i, settings, rootBindingPath, i + 4, uvFmtsInt);
					vertexSettings.contentContainer.Add(uvField);
				}


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


				root.Add(normalize);
				root.Add(highPIdx);
				root.Add(highPIdxCount);
				root.Add(vertexSettings);
				root.Add(NotImplHeader);
				root.Add(splitMultiMeshes);
			}

			private void OnOverrideToggled(ChangeEvent<bool> evt)
			{
				toggleGroup.SetEnabled(evt.newValue);
			}
			
			private VisualElement VtxCompressionOption(string name, SerializedObject settings, string rootPath, int index, List<int> options)
			{
				VisualElement vtxOptions = new VisualElement();

				vtxOptions.style.flexGrow = 1;
				vtxOptions.style.flexDirection = FlexDirection.Row;
				vtxOptions.style.flexGrow = 1;
				vtxOptions.style.alignItems = Align.Stretch;
				vtxOptions.style.flexShrink = 1;
				vtxOptions.style.flexBasis = StyleKeyword.Auto;

				Label label = new Label(name);
				label.style.flexGrow = 0.4f;
				label.style.flexBasis = 0.4f;
				label.style.flexShrink = 0.4f;
				label.style.textOverflow = TextOverflow.Ellipsis;
				label.style.overflow = Overflow.Hidden;
				label.style.unityTextOverflowPosition = TextOverflowPosition.End;

				Toggle toggle = new Toggle();
				toggle.bindingPath = rootPath + "settings.altStream.Array.data[" + index + "]";
				toggle.BindProperty(settings);
				toggle.style.flexGrow = 0.6f;
				toggle.style.flexBasis = 0.6f;
				toggle.style.flexShrink = 0.6f;
				toggle.style.flexDirection = FlexDirection.Row;

				PopupField<int> popup = new PopupField<int>();
				popup.formatListItemCallback = (int b) => { return VtxFormatNames[b]; };
				popup.formatSelectedValueCallback = (int b) => { return VtxFormatNames[b]; };
				popup.choices = options;
				popup.bindingPath = rootPath + "settings.serializedVtxFormats.Array.data[" + index + "]";
				popup.BindProperty(settings);
				popup.style.flexGrow = 1f;
				popup.style.flexBasis = 1;
				popup.style.flexShrink = 1f;
				popup.style.flexDirection = FlexDirection.Row;

				vtxOptions.Add(label); 
				vtxOptions.Add(toggle);
				vtxOptions.Add(popup);
				//popup.label = name;

				return vtxOptions;
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
	}
}
