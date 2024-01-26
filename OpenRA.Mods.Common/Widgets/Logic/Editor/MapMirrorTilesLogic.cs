#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;
using static OpenRA.Mods.Common.Traits.MirrorLayerOverlay;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapMirrorTilesLogic : ChromeLogic
	{
		[TranslationReference]
		const string MirrorModeNoneTranslation = "mirror-mode.none";

		[TranslationReference]
		const string MirrorModeFlipTranslation = "mirror-mode.flip";

		[TranslationReference]
		const string MirrorModeRotateTranslation = "mirror-mode.rotate";

		readonly EditorActionManager editorActionManager;
		readonly MirrorLayerOverlay mirrorLayerTrait;
		readonly ScrollPanelWidget mirrorTileColorPanel;
		readonly SliderWidget alphaSlider;
		readonly LabelWidget alphaValueLabel;
		readonly DropDownButtonWidget modeDropdown;
		readonly SliderWidget rotateNumSidesSlider;
		readonly DropDownButtonWidget flipNumSidesDropdown;
		readonly LabelWidget numSidesLabel;
		readonly LabelWidget rotateNumSidesValueLabel;
		readonly LabelWidget axisAngleLabel;
		readonly SliderWidget axisAngleSlider;
		readonly LabelWidget axisAngleValueLabel;
		readonly CheckboxWidget axisAngleGuide;
		readonly ButtonWidget clearSelectedButtonWidget;
		readonly ButtonWidget clearAllButtonWidget;
		readonly EditorViewportControllerWidget editor;

		int? mirrorTile;

		[ObjectCreator.UseCtor]
		public MapMirrorTilesLogic(Widget widget, World world, ModData modData, WorldRenderer worldRenderer, Dictionary<string, MiniYaml> logicArgs)
		{
			mirrorLayerTrait = world.WorldActor.Trait<MirrorLayerOverlay>();
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			editor = widget.Parent.Parent.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			editor.BrushChanged += HandleBrushChanged;

			mirrorTileColorPanel = widget.Get<ScrollPanelWidget>("TILE_COLOR_PANEL");
			{
				mirrorTileColorPanel.Layout = new GridLayout(mirrorTileColorPanel);
				var colorSwatchTemplate = mirrorTileColorPanel.Get<ScrollItemWidget>("TILE_COLOR_TEMPLATE");
				var iconTemplate = mirrorTileColorPanel.Get<ScrollItemWidget>("TILE_ICON_TEMPLATE");
				mirrorTileColorPanel.RemoveChildren();

				var colors = mirrorLayerTrait.Info.Colors;
				for (var colorIndex = 0; colorIndex < colors.Length; colorIndex++)
				{
					var scrollItem = SetupColorSwatchItem(colorIndex, colorSwatchTemplate);
					mirrorTileColorPanel.AddChild(scrollItem);
				}

				var eraseItem = SetupEraseItem(iconTemplate);
				mirrorTileColorPanel.AddChild(eraseItem);

				///////

				ScrollItemWidget SetupColorSwatchItem(int index, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template,
						() => mirrorTile == index,
						() =>
						{
							mirrorTile = index;
							editor.SetBrush(new EditorMirrorLayerBrush(editor, index, worldRenderer));
						});

					var colorWidget = item.Get<ColorBlockWidget>("TILE_PREVIEW");
					colorWidget.GetColor = () => colors[index];

					return item;
				}

				ScrollItemWidget SetupEraseItem(ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template,
						() => mirrorTile == null && editor.CurrentBrush != null && editor.CurrentBrush is EditorMirrorLayerBrush,
						() =>
						{
							mirrorTile = null;
							editor.SetBrush(new EditorMirrorLayerBrush(editor, null, worldRenderer));
						});

					return item;
				}
			}

			clearSelectedButtonWidget = widget.Get<ButtonWidget>("CLEAR_CURRENT_BUTTON");
			clearSelectedButtonWidget.IsDisabled = () => mirrorTile == null;
			clearSelectedButtonWidget.OnClick = ClearSelected;

			clearAllButtonWidget = widget.Get<ButtonWidget>("CLEAR_ALL_BUTTON");
			clearAllButtonWidget.OnClick = ClearAll;

			alphaSlider = widget.Get<SliderWidget>("ALPHA_SLIDER");
			alphaSlider.MinimumValue = 1;
			alphaSlider.MaximumValue = 255;
			alphaSlider.Ticks = 12;
			alphaSlider.OnChange += (val) => mirrorLayerTrait.TileAlpha = (int)val;
			alphaSlider.GetValue = () => mirrorLayerTrait.TileAlpha;

			alphaValueLabel = widget.Get<LabelWidget>("ALPHA_VALUE");
			alphaValueLabel.GetText = () => mirrorLayerTrait.TileAlpha.ToString(NumberFormatInfo.InvariantInfo);

			modeDropdown = widget.Get<DropDownButtonWidget>("MODE_DROPDOWN");
			modeDropdown.OnMouseDown = _ => ShowMirrorModeDropDown(modeDropdown);
			modeDropdown.GetText = () =>
			{
				switch (mirrorLayerTrait.MirrorMode)
				{
					case MirrorTileMode.None:
						return TranslationProvider.GetString(MirrorModeNoneTranslation);
					case MirrorTileMode.Flip:
						return TranslationProvider.GetString(MirrorModeFlipTranslation);
					case MirrorTileMode.Rotate:
						return TranslationProvider.GetString(MirrorModeRotateTranslation);
					default:
						throw new ArgumentException($"Couldn't find translation for mirror tile flip mode '{mirrorLayerTrait.MirrorMode}'");
				}
			};

			bool IsFlipMode() => mirrorLayerTrait.MirrorMode == MirrorTileMode.Flip;
			bool IsRotateMode() => mirrorLayerTrait.MirrorMode == MirrorTileMode.Rotate;

			numSidesLabel = widget.Get<LabelWidget>("NUM_SIDES_LABEL");
			numSidesLabel.IsVisible = () => IsFlipMode() || IsRotateMode();

			rotateNumSidesSlider = widget.Get<SliderWidget>("ROTATE_NUM_SIDES_SLIDER");
			rotateNumSidesSlider.MinimumValue = 2;
			rotateNumSidesSlider.MaximumValue = 8;
			rotateNumSidesSlider.Ticks = 7;
			rotateNumSidesSlider.IsVisible = IsRotateMode;
			rotateNumSidesSlider.OnChange += (val) => mirrorLayerTrait.NumSides = (int)val;
			rotateNumSidesSlider.GetValue = () => mirrorLayerTrait.NumSides;

			rotateNumSidesValueLabel = widget.Get<LabelWidget>("ROTATE_NUM_SIDES_VALUE");
			rotateNumSidesValueLabel.IsVisible = IsRotateMode;
			rotateNumSidesValueLabel.GetText = () => mirrorLayerTrait.NumSides.ToString(NumberFormatInfo.InvariantInfo);

			flipNumSidesDropdown = widget.Get<DropDownButtonWidget>("FLIP_NUM_SIDES_DROPDOWN");
			flipNumSidesDropdown.OnMouseDown = _ => ShowFlipNumSidesDropDown(flipNumSidesDropdown);
			flipNumSidesDropdown.IsVisible = IsFlipMode;
			flipNumSidesDropdown.GetText = () => mirrorLayerTrait.NumSides.ToString(NumberFormatInfo.InvariantInfo);

			axisAngleLabel = widget.Get<LabelWidget>("AXIS_ANGLE_LABEL");
			axisAngleLabel.IsVisible = IsFlipMode;

			axisAngleSlider = widget.Get<SliderWidget>("AXIS_ANGLE_SLIDER");
			axisAngleSlider.MinimumValue = 0;
			axisAngleSlider.MaximumValue = 11;
			axisAngleSlider.Ticks = 12;
			axisAngleSlider.IsVisible = IsFlipMode;
			axisAngleSlider.OnChange += (val) => mirrorLayerTrait.AxisAngle = (int)val * 15;
			axisAngleSlider.GetValue = () => mirrorLayerTrait.AxisAngle / 15;

			axisAngleValueLabel = widget.Get<LabelWidget>("AXIS_ANGLE_VALUE");
			axisAngleValueLabel.IsVisible = IsFlipMode;
			axisAngleValueLabel.GetText = () => mirrorLayerTrait.AxisAngle.ToString(NumberFormatInfo.InvariantInfo);

			axisAngleGuide = widget.Get<CheckboxWidget>("AXIS_ANGLE_GUIDE");
			axisAngleGuide.IsVisible = IsFlipMode;
			axisAngleGuide.IsChecked = () => mirrorLayerTrait.ShowAxisGuide;
			axisAngleGuide.OnClick = () => mirrorLayerTrait.ShowAxisGuide = !mirrorLayerTrait.ShowAxisGuide;
		}

		protected override void Dispose(bool disposing)
		{
			editor.BrushChanged -= HandleBrushChanged;
			base.Dispose(disposing);
		}

		void HandleBrushChanged()
		{
			if (editor.CurrentBrush is not EditorMirrorLayerBrush)
			{
				mirrorTile = null;
			}
		}

		void ClearSelected()
		{
			if (editor.CurrentBrush is EditorMirrorLayerBrush mirrorLayerBrush &&
				mirrorLayerBrush.Template.HasValue &&
				mirrorLayerTrait.Tiles.TryGetValue(mirrorLayerBrush.Template.Value, out var tiles) &&
				tiles.Count > 0)
				editorActionManager.Add(new ClearSelectedMirrorTilesEditorAction(mirrorLayerBrush.Template.Value, mirrorLayerTrait));
		}

		void ClearAll()
		{
			if (mirrorLayerTrait.Tiles.Count > 0 && mirrorLayerTrait.Tiles.Any(x => x.Value.Count > 0))
				editorActionManager.Add(new ClearAllMirrorTilesEditorAction(mirrorLayerTrait));
		}

		void ShowMirrorModeDropDown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(MirrorTileMode mode, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => mirrorLayerTrait.MirrorMode == mode,
					() => mirrorLayerTrait.SetMirrorMode(mode));

				item.Get<LabelWidget>("LABEL").GetText = () =>
				{
					switch (mode)
					{
						case MirrorTileMode.None:
							return TranslationProvider.GetString(MirrorModeNoneTranslation);
						case MirrorTileMode.Flip:
							return TranslationProvider.GetString(MirrorModeFlipTranslation);
						case MirrorTileMode.Rotate:
							return TranslationProvider.GetString(MirrorModeRotateTranslation);
						default:
							throw new ArgumentException($"Couldn't find translation for mirror tile flip mode '{mode}'");
					}
				};

				return item;
			}

			var options = new[] { MirrorTileMode.None, MirrorTileMode.Flip, MirrorTileMode.Rotate };
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		void ShowFlipNumSidesDropDown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(int value, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => mirrorLayerTrait.NumSides == value,
					() => mirrorLayerTrait.NumSides = value);

				item.Get<LabelWidget>("LABEL").GetText = () => value.ToString(NumberFormatInfo.InvariantInfo);
				return item;
			}

			var options = new[] { 2, 4 };
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}
	}
}
