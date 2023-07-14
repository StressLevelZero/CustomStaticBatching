using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SLZ.CustomStaticBatching
{
	public class TabBox : VisualElement
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
			for (int i = 0; i < tabCount; i++)
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
			for (int i = 0; i < tabs.Length; i++)
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

		public static class TabStyle
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

		public class Tab : VisualElement
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

			public string text
			{
				get => m_text;
				set
				{
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

	}
}