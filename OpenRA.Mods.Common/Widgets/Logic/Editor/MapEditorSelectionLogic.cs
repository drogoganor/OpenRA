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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapEditorSelectionLogic : ChromeLogic
	{
		enum EditorSelectionMode
		{
			Area,
			Actor
		}

		readonly EditorViewportControllerWidget editor;
		readonly WorldRenderer worldRenderer;

		readonly BackgroundWidget actorEditPanel;
		readonly BackgroundWidget areaEditPanel;

		readonly CheckboxWidget copyTerrainCheckbox;
		readonly CheckboxWidget copyResourcesCheckbox;
		readonly CheckboxWidget copyActorsCheckbox;
		readonly EditorActorLayer editorActorLayer;

		MapCopyFilters copyFilters = MapCopyFilters.All;
		EditorClipboard clipboard;

		readonly IResourceLayer resourceLayer;

		[ObjectCreator.UseCtor]
		public MapEditorSelectionLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.worldRenderer = worldRenderer;

			editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			resourceLayer = world.WorldActor.Trait<IResourceLayer>();

			editor = widget.Get<EditorViewportControllerWidget>("MAP_EDITOR");

			actorEditPanel = widget.Get<BackgroundWidget>("ACTOR_EDIT_PANEL");
			areaEditPanel = widget.Get<BackgroundWidget>("AREA_EDIT_PANEL");

			actorEditPanel.IsVisible = () => editor.CurrentBrush == editor.DefaultBrush && editor.DefaultBrush.Selection.Actor != null;
			areaEditPanel.IsVisible = () => !actorEditPanel.IsVisible();

			copyTerrainCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_TERRAIN_CHECKBOX");
			copyResourcesCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_RESOURCES_CHECKBOX");
			copyActorsCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_ACTORS_CHECKBOX");

			var copyButton = widget.Get<ButtonWidget>("COPY_BUTTON");
			copyButton.OnClick = () => clipboard = CopySelectionContents();

			copyButton.IsDisabled = () => editor.DefaultBrush.Selection.Area == null;

			var pasteButton = widget.Get<ButtonWidget>("PASTE_BUTTON");
			pasteButton.OnClick = () =>
			{
				if (clipboard == null)
					return;

				editor.SetBrush(new EditorCopyPasteBrush(
					editor,
					worldRenderer,
					clipboard,
					resourceLayer,
					() => copyFilters));
			};

			pasteButton.IsDisabled = () => clipboard == null;
			pasteButton.IsHighlighted = () => editor.CurrentBrush is EditorCopyPasteBrush;

			var closeAreaSelectionButton = areaEditPanel.Get<ButtonWidget>("SELECTION_CANCEL_BUTTON");
			closeAreaSelectionButton.OnClick = () => editor.DefaultBrush.ClearSelection();

			CreateCategoriesPanel();
		}

		EditorClipboard CopySelectionContents()
		{
			var selection = editor.DefaultBrush.Selection.Area;
			var source = new CellCoordsRegion(selection.TopLeft, selection.BottomRight);

			var mapTiles = worldRenderer.World.Map.Tiles;
			var mapHeight = worldRenderer.World.Map.Height;
			var mapResources = worldRenderer.World.Map.Resources;

			var previews = new Dictionary<string, EditorActorPreview>();
			var tiles = new Dictionary<CPos, ClipboardTile>();

			foreach (var cell in source)
			{
				if (!mapTiles.Contains(cell))
					continue;

				var offset = new CVec(selection.TopLeft.X, selection.TopLeft.Y);

				var resourceLayerContents = resourceLayer.GetResource(cell);

				tiles.Add(cell, new ClipboardTile
				{
					TerrainTile = mapTiles[cell],
					ResourceTile = mapResources[cell],
					ResourceLayerContents = resourceLayerContents,
					Height = mapHeight[cell]
				});

				if (copyFilters.HasFlag(MapCopyFilters.Actors))
				{
					var regionActors = selection.SelectMany(editorActorLayer.PreviewsAt).Distinct().ToList();
					foreach (var preview in regionActors)
					{
						if (previews.ContainsKey(preview.ID))
							continue;

						previews.Add(preview.ID, preview);
					}
				}
			}

			return new EditorClipboard(selection, previews, tiles);
		}

		void CreateCategoriesPanel()
		{
			MapCopyFilters[] allCategories = { MapCopyFilters.Terrain, MapCopyFilters.Resources, MapCopyFilters.Actors };
			foreach (var cat in allCategories)
			{
				CheckboxWidget checkbox = null;
				if (cat.HasFlag(MapCopyFilters.Terrain))
					checkbox = copyTerrainCheckbox;
				else if (cat.HasFlag(MapCopyFilters.Resources))
					checkbox = copyResourcesCheckbox;
				else if (cat.HasFlag(MapCopyFilters.Actors))
					checkbox = copyActorsCheckbox;

				checkbox.GetText = () => cat.ToString();
				checkbox.IsChecked = () => copyFilters.HasFlag(cat);
				checkbox.IsVisible = () => true;
				checkbox.OnClick = () => copyFilters ^= cat;
			}
		}
	}
}
