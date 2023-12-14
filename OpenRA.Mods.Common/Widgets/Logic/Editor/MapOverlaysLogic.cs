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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	[ChromeLogicArgsHotkeys("ToggleGridOverlayKey", "ToggleBuildableOverlayKey")]
	public class MapOverlaysLogic : ChromeLogic
	{
		[Flags]
		enum MapOverlays
		{
			None = 0,
			Grid = 1,
			Buildable = 2,
			Mirror = 4,
		}

		readonly TerrainGeometryOverlay terrainGeometryTrait;
		readonly BuildableTerrainOverlay buildableTerrainTrait;
		readonly MirrorLayerOverlay mirrorLayerTrait;
		readonly ScrollPanelWidget mirrorTileColorPanel;
		readonly SliderWidget mirrorNumSidesSlider;
		readonly SliderWidget mirrorStartAngleSlider;
		readonly DropDownButtonWidget mirrorMethodDropdown;
		readonly LabelWidget mirrorNumSidesLabel;
		readonly LabelWidget mirrorStartAngleLabel;
		readonly Widget widget;
		readonly EditorViewportControllerWidget editor;

		int mirrorTile = 0;

		[ObjectCreator.UseCtor]
		public MapOverlaysLogic(Widget widget, World world, ModData modData, WorldRenderer worldRenderer, Dictionary<string, MiniYaml> logicArgs)
		{
			this.widget = widget;

			terrainGeometryTrait = world.WorldActor.Trait<TerrainGeometryOverlay>();
			buildableTerrainTrait = world.WorldActor.Trait<BuildableTerrainOverlay>();
			mirrorLayerTrait = world.WorldActor.Trait<MirrorLayerOverlay>();

			editor = widget.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			mirrorTileColorPanel = widget.Get<ScrollPanelWidget>("MIRROR_TILE_COLOR_PANEL");
			{
				mirrorTileColorPanel.Layout = new GridLayout(mirrorTileColorPanel);
				var template = mirrorTileColorPanel.Get<ScrollItemWidget>("MIRROR_TILE_COLOR_TEMPLATE");
				mirrorTileColorPanel.RemoveChildren();

				var colors = mirrorLayerTrait.Info.Colors;

				var defaultTile = false;
				for (var colorIndex = 0; colorIndex < colors.Length; colorIndex++)
				{
					var scrollItem = SetupItem(colorIndex, template);
					mirrorTileColorPanel.AddChild(scrollItem);

					if (!defaultTile)
					{
						mirrorTile = colorIndex;
						defaultTile = true;
					}
				}

				///////

				ScrollItemWidget SetupItem(int index, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template,
						() => mirrorTile == index,
						() =>
						{
							mirrorTile = index;
							editor.SetBrush(new EditorMirrorLayerBrush(editor, index, worldRenderer));
						});

					var colorWidget = item.Get<ColorBlockWidget>("MIRROR_TILE_PREVIEW");
					colorWidget.GetColor = () => colors[index];

					return item;
				}
			}

			mirrorNumSidesLabel = widget.Get<LabelWidget>("MIRROR_NUM_SIDES_VALUE");
			mirrorNumSidesSlider = widget.Get<SliderWidget>("MIRROR_NUM_SIDES_SLIDER");
			mirrorStartAngleSlider = widget.Get<SliderWidget>("MIRROR_START_ANGLE_SLIDER");
			mirrorStartAngleLabel = widget.Get<LabelWidget>("MIRROR_START_ANGLE_VALUE");
			mirrorMethodDropdown = widget.Get<DropDownButtonWidget>("MIRROR_METHOD_DROPDOWN");

			mirrorMethodDropdown.OnMouseDown = _ => ShowMirrorMethodDropDown(mirrorMethodDropdown);
			mirrorMethodDropdown.GetText = () => mirrorLayerTrait.UseFlipMethod ? "Flip" : "Rotate";
			mirrorNumSidesSlider.MinimumValue = 2;
			mirrorNumSidesSlider.MaximumValue = 8;
			mirrorNumSidesSlider.Ticks = 7;
			mirrorStartAngleSlider.MinimumValue = 0;
			mirrorStartAngleSlider.MaximumValue = 11;
			mirrorStartAngleSlider.Ticks = 12;
			mirrorStartAngleSlider.IsDisabled = () => !mirrorLayerTrait.UseFlipMethod;

			mirrorStartAngleSlider.OnChange += (val) => mirrorLayerTrait.StartAngle = (int)val * 15;
			mirrorNumSidesSlider.OnChange += (val) => mirrorLayerTrait.NumSides = (int)val;

			mirrorStartAngleLabel.GetText = () => mirrorLayerTrait.StartAngle.ToString();
			mirrorNumSidesLabel.GetText = () => mirrorLayerTrait.NumSides.ToString();

			var toggleGridKey = new HotkeyReference();
			if (logicArgs.TryGetValue("ToggleGridOverlayKey", out var yaml))
				toggleGridKey = modData.Hotkeys[yaml.Value];

			var toggleBuildableKey = new HotkeyReference();
			if (logicArgs.TryGetValue("ToggleBuildableOverlayKey", out yaml))
				toggleBuildableKey = modData.Hotkeys[yaml.Value];

			var keyhandler = widget.Get<LogicKeyListenerWidget>("OVERLAY_KEYHANDLER");
			keyhandler.AddHandler(e =>
			{
				if (e.Event != KeyInputEvent.Down)
					return false;

				if (toggleGridKey.IsActivatedBy(e))
				{
					terrainGeometryTrait.Enabled ^= true;
					return true;
				}

				if (toggleBuildableKey.IsActivatedBy(e))
				{
					buildableTerrainTrait.Enabled ^= true;
					return true;
				}

				return false;
			});

			var overlayPanel = CreateOverlaysPanel();

			var overlayDropdown = widget.GetOrNull<DropDownButtonWidget>("OVERLAY_BUTTON");
			if (overlayDropdown != null)
			{
				overlayDropdown.OnMouseDown = _ =>
				{
					overlayDropdown.RemovePanel();
					overlayDropdown.AttachPanel(overlayPanel);
				};
			}
		}

		public void ShowMirrorMethodDropDown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(bool ii, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => mirrorLayerTrait.UseFlipMethod == ii,
					() => mirrorLayerTrait.UseFlipMethod = ii);
				item.Get<LabelWidget>("LABEL").GetText = () => ii ? "Flip" : "Rotate";
				return item;
			}

			var options = new[] { true, false };
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		Widget CreateOverlaysPanel()
		{
			var categoriesPanel = widget.Get<Widget>("TOOLS_WIDGETS");
			var showGridCheckbox = categoriesPanel.Get<CheckboxWidget>("SHOW_TILE_GRID");
			var showBuildableAreaCheckbox = categoriesPanel.Get<CheckboxWidget>("SHOW_BUILDABLE_AREA");
			var showMirrorLayerCheckbox = categoriesPanel.Get<CheckboxWidget>("SHOW_MIRROR_LAYER");

			MapOverlays[] allCategories = { MapOverlays.Grid, MapOverlays.Buildable, MapOverlays.Mirror };
			foreach (var cat in allCategories)
			{
				if (cat.HasFlag(MapOverlays.Grid))
				{
					showGridCheckbox.IsChecked = () => terrainGeometryTrait.Enabled;
					showGridCheckbox.OnClick = () => terrainGeometryTrait.Enabled ^= true;
				}
				else if (cat.HasFlag(MapOverlays.Buildable))
				{
					showBuildableAreaCheckbox.IsChecked = () => buildableTerrainTrait.Enabled;
					showBuildableAreaCheckbox.OnClick = () => buildableTerrainTrait.Enabled ^= true;
				}
				else if (cat.HasFlag(MapOverlays.Mirror))
				{
					showMirrorLayerCheckbox.IsChecked = () => mirrorLayerTrait.Enabled;
					showMirrorLayerCheckbox.OnClick = () => mirrorLayerTrait.Enabled ^= true;
				}
			}

			return categoriesPanel;
		}
	}
}
