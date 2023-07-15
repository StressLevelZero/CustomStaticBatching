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

				IntegerField highPIdxCount = new IntegerField("Max vertices per 32-bit index combined mesh");
				highPIdxCount.isDelayed = true;
				highPIdxCount.tooltip = "The maximum number of vertices that can be in a 32-bit index buffer combined mesh. Since a 32-bit index buffer can represent trillions of vertices, its a good idea to arbitrarily put a cap on how large the combined mesh can be";
				highPIdxCount.bindingPath = rootBindingPath + "settings.maxCombined32Idx";
				SetToggleStyle(highPIdxCount);
				highPIdxCount.BindProperty(settings);
				highPIdxCount.RegisterValueChangedCallback((ChangeEvent<int> e) => { highPIdxCount.value = Math.Max( 0x10000, highPIdxCount.value); });

				Foldout vertexSettings = new Foldout();
				vertexSettings.text = "Vertex Attribute Format Settings";

				// vertex channel compression
				List<int> normTanFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.SNorm8, 0};
				PopupField<int> normField = VtxCompressionOption("Normal", normTanFmtsInt);
				normField.bindingPath = rootBindingPath + "settings.serializedVtxFormats.Array.data[1]";
				normField.BindProperty(settings);
				vertexSettings.contentContainer.Add(normField);

				PopupField<int> tanField = VtxCompressionOption("Tangent", normTanFmtsInt);
				tanField.bindingPath = rootBindingPath + "settings.serializedVtxFormats.Array.data[2]";
				tanField.BindProperty(settings);
				vertexSettings.contentContainer.Add(tanField);

				List<int> colorFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.UNorm8, 0 };
				PopupField<int> colorField = VtxCompressionOption("Color", colorFmtsInt);
				colorField.bindingPath = rootBindingPath + "settings.serializedVtxFormats.Array.data[3]";
				colorField.BindProperty(settings);
				vertexSettings.contentContainer.Add(colorField);

				List<int> uvFmtsInt = new List<int>() { (int)VtxFormats.Float32, (int)VtxFormats.Float16, (int)VtxFormats.SNorm8, (int)VtxFormats.UNorm8, 0 };
				PopupField<int>[] uvFields = new PopupField<int>[8];
				for (int i = 0; i < 8; i++)
				{
					uvFields[i] = VtxCompressionOption("UV" + i, uvFmtsInt);
					uvFields[i].bindingPath = rootBindingPath + "settings.serializedVtxFormats.Array.data[" + (4 + i) + "]";
					uvFields[i].BindProperty(settings);
					vertexSettings.contentContainer.Add(uvFields[i]);
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
			
			private PopupField<int> VtxCompressionOption(string name, List<int> options)
			{
				PopupField<int> popup = new PopupField<int>();
				popup.formatListItemCallback = (int b) => { return VtxFormatNames[b]; };
				popup.formatSelectedValueCallback = (int b) => { return VtxFormatNames[b]; };
				popup.choices = options;
				popup.label = name;
				SetToggleStyle(popup);
				return popup;
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
