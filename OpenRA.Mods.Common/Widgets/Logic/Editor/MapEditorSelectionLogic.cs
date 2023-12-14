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

		readonly EditorSettings editorSettings;
		readonly CheckboxWidget copyTerrainCheckbox;
		readonly CheckboxWidget copyResourcesCheckbox;
		readonly CheckboxWidget copyActorsCheckbox;
		readonly EditorActorLayer editorActorLayer;

		MapCopyFilters copyFilters = MapCopyFilters.All;
		ushort fillTile = 0;
		EditorClipboard clipboard;

		readonly IResourceLayer resourceLayer;

		[ObjectCreator.UseCtor]
		public MapEditorSelectionLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.worldRenderer = worldRenderer;

			editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			editorSettings = world.WorldActor.Trait<EditorSettings>();
			resourceLayer = world.WorldActor.Trait<IResourceLayer>();

			editor = widget.Get<EditorViewportControllerWidget>("MAP_EDITOR");

			actorEditPanel = widget.Get<BackgroundWidget>("ACTOR_EDIT_PANEL");
			areaEditPanel = widget.Get<BackgroundWidget>("AREA_EDIT_PANEL");

			actorEditPanel.IsVisible = () => editor.CurrentBrush == editor.DefaultBrush && editor.DefaultBrush.Selection.Actor != null;
			areaEditPanel.IsVisible = () => !actorEditPanel.IsVisible();

			copyTerrainCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_TERRAIN_CHECKBOX");
			copyResourcesCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_RESOURCES_CHECKBOX");
			copyActorsCheckbox = areaEditPanel.Get<CheckboxWidget>("COPY_FILTER_ACTORS_CHECKBOX");

			terrainInfo = world.Map.Rules.TerrainInfo as ITemplatedTerrainInfo;

			var tileFillPanel = widget.Get<ScrollPanelWidget>("TILE_CLEAR_PANEL");
			{
				tileFillPanel.Layout = new GridLayout(tileFillPanel);
				var template = tileFillPanel.Get<ScrollItemWidget>("TILE_CLEAR_TEMPLATE");

				tileFillPanel.RemoveChildren();

				var clearTiles = editorSettings.Info.TerrainClearTiles.Values.First(x => x.TilesetName == terrainInfo.Id);

				var defaultFillTile = false;
				foreach (var tileIndex in clearTiles.TileIndices)
				{
					if (!terrainInfo.Templates.ContainsKey(tileIndex))
						continue;

					var scrollItem = SetupItem(tileIndex, template);
					tileFillPanel.AddChild(scrollItem);

					if (!defaultFillTile)
					{
						fillTile = tileIndex;
						defaultFillTile = true;
					}
				}

				///////

				ScrollItemWidget SetupItem(ushort tile, ScrollItemWidget template)
				{
					var item = ScrollItemWidget.Setup(template,
						() => fillTile == tile,
						() => fillTile = tile);

					var terrainPreview = item.Get<TerrainTemplatePreviewWidget>("TILE_PREVIEW");
					terrainPreview.SetTemplate(terrainInfo.Templates[tile]);
					return item;
				}
			}

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

			var clearButton = widget.Get<ButtonWidget>("CLEAR_BUTTON");
			clearButton.OnClick = () => ClearSelectionContents();

			clearButton.IsDisabled = () => editor.DefaultBrush.Selection.Area == null;
			clearButton.IsHighlighted = () => editor.CurrentBrush is EditorCopyPasteBrush;

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

		void ClearSelectionContents()
		{
			// Copy existing selection into a clipboard
			var previousContent = CopySelectionContents();
			var selectionArea = editor.DefaultBrush.Selection.Area;

			editorActionManager.Add(new ClearSelectionEditorAction(
				copyFilters,
				resourceLayer,
				selectionArea,
				world.Map,
				previousContent,
				fillTile,
				editorActorLayer));
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

	sealed class ClearSelectionEditorAction : IEditorAction
	{
		[TranslationReference("amount")]
		const string ClearedTiles = "notification-cleared-tiles";

		public string Text { get; }

		readonly MapCopyFilters copyFilters;
		readonly IResourceLayer resourceLayer;
		readonly ITemplatedTerrainInfo templatedTerrainInfo;
		readonly EditorActorLayer editorActorLayer;
		readonly EditorClipboard clipboard;
		readonly CellRegion clearRegion;
		readonly Map map;

		readonly ushort clearTile;

		public ClearSelectionEditorAction(
			MapCopyFilters copyFilters,
			IResourceLayer resourceLayer,
			CellRegion clearRegion,
			Map map,
			EditorClipboard clipboard,
			ushort clearTile,
			EditorActorLayer editorActorLayer)
		{
			this.copyFilters = copyFilters;
			this.resourceLayer = resourceLayer;
			this.clipboard = clipboard;
			this.clearRegion = clearRegion;
			this.editorActorLayer = editorActorLayer;
			this.clearTile = clearTile;
			this.map = map;

			templatedTerrainInfo = (ITemplatedTerrainInfo)map.Rules.TerrainInfo;

			Text = TranslationProvider.GetString(ClearedTiles, Translation.Arguments("amount", clearRegion.Count()));
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			var terrainTemplate = templatedTerrainInfo.Templates[clearTile];
			foreach (var position in clearRegion)
			{
				if (!map.Contains(position))
					continue;

				var index = terrainTemplate.PickAny ? (byte)Game.CosmeticRandom.Next(0, terrainTemplate.TilesCount) : (byte)0;
				if (copyFilters.HasFlag(MapCopyFilters.Terrain))
					map.Tiles[position] = new TerrainTile(clearTile, index);

				if (copyFilters.HasFlag(MapCopyFilters.Resources))
					resourceLayer.ClearResources(position);

				if (copyFilters.HasFlag(MapCopyFilters.Actors))
				{
					var actors = editorActorLayer.PreviewsAt(position).ToArray();
					foreach (var actor in actors)
						editorActorLayer.Remove(actor);
				}
			}
		}

		public void Undo()
		{
			foreach (var tileKeyValuePair in clipboard.Tiles)
			{
				var position = tileKeyValuePair.Key;

				if (!map.Contains(position))
					continue;

				var tile = tileKeyValuePair.Value;
				var resourceLayerContents = tile.ResourceLayerContents;

				if (copyFilters.HasFlag(MapCopyFilters.Terrain))
					map.Tiles[position] = tile.TerrainTile;

				if (copyFilters.HasFlag(MapCopyFilters.Resources) && !string.IsNullOrWhiteSpace(resourceLayerContents.Type))
					resourceLayer.AddResource(resourceLayerContents.Type, position, resourceLayerContents.Density);
			}

			if (copyFilters.HasFlag(MapCopyFilters.Actors))
			{
				foreach (var actor in clipboard.Actors.Values)
					editorActorLayer.Add(actor);
			}
		}
	}
}
