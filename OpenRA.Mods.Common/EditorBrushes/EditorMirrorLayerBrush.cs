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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public sealed class EditorMirrorLayerBrush : IEditorBrush
	{
		public int? Template;

		readonly WorldRenderer worldRenderer;
		readonly World world;
		readonly EditorActionManager editorActionManager;
		readonly MirrorLayerOverlay mirrorLayerOverlay;
		readonly EditorViewportControllerWidget editorWidget;

		PaintMirrorTileEditorAction action;
		bool painting;

		public EditorMirrorLayerBrush(EditorViewportControllerWidget editorWidget, int? id, WorldRenderer wr)
		{
			this.editorWidget = editorWidget;
			worldRenderer = wr;
			world = wr.World;

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			mirrorLayerOverlay = world.WorldActor.Trait<MirrorLayerOverlay>();

			Template = id;
			worldRenderer = wr;
			world = wr.World;
			action = new PaintMirrorTileEditorAction(Template, mirrorLayerOverlay);
		}

		public bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Button != MouseButton.Left && mi.Button != MouseButton.Right)
				return false;

			if (mi.Button == MouseButton.Right)
			{
				if (mi.Event == MouseInputEvent.Up)
				{
					editorWidget.ClearBrush();
					return true;
				}

				return false;
			}

			var cell = worldRenderer.Viewport.ViewToWorld(mi.Location);

			if (mi.Button == MouseButton.Left && mi.Event != MouseInputEvent.Up)
			{
				action.Add(cell);
				painting = true;
			}
			else if (painting && mi.Button == MouseButton.Left && mi.Event == MouseInputEvent.Up)
			{
				if (action.DidPaintTiles)
					editorActionManager.Add(action);

				action = new PaintMirrorTileEditorAction(Template, mirrorLayerOverlay);
				painting = false;
			}

			return true;
		}

		public void Tick() { }

		public void Dispose() { }
	}

	readonly struct PaintMirrorTile
	{
		public readonly CPos Cell;
		public readonly int? Previous;

		public PaintMirrorTile(CPos cell, int? previous)
		{
			Cell = cell;
			Previous = previous;
		}
	}

	class PaintMirrorTileEditorAction : IEditorAction
	{
		[TranslationReference("amount", "type")]
		const string AddedMirrorTiles = "notification-added-mirror-tiles";

		[TranslationReference("amount")]
		const string RemovedMirrorTiles = "notification-removed-mirror-tiles";

		public string Text { get; private set; }

		readonly int? type;
		readonly MirrorLayerOverlay mirrorLayerOverlay;

		readonly List<PaintMirrorTile> paintTiles = new();

		public bool DidPaintTiles => paintTiles.Count > 0;

		public PaintMirrorTileEditorAction(
			int? type,
			MirrorLayerOverlay mirrorLayerOverlay)
		{
			this.mirrorLayerOverlay = mirrorLayerOverlay;
			this.type = type;
		}

		public void Execute()
		{
		}

		public void Do()
		{
			foreach (var paintTile in paintTiles)
				mirrorLayerOverlay.SetTile(paintTile.Cell, type);
		}

		public void Undo()
		{
			foreach (var paintTile in paintTiles)
				mirrorLayerOverlay.SetTile(paintTile.Cell, paintTile.Previous);
		}

		public void Add(CPos target)
		{
			foreach (var cell in mirrorLayerOverlay.CalculateMirrorPositions(target))
			{
				var existing = mirrorLayerOverlay.CellLayer[cell];
				if (existing == type)
					continue;

				paintTiles.Add(new PaintMirrorTile(cell, existing));
				mirrorLayerOverlay.SetTile(cell, type);
			}

			if (type != null)
				Text = TranslationProvider.GetString(AddedMirrorTiles, Translation.Arguments("amount", paintTiles.Count, "type", type));
			else
				Text = TranslationProvider.GetString(RemovedMirrorTiles, Translation.Arguments("amount", paintTiles.Count));
		}
	}

	class ClearSelectedMirrorTilesEditorAction : IEditorAction
	{
		[TranslationReference("amount", "type")]
		const string ClearedSelectedMirrorTiles = "notification-cleared-selected-mirror-tiles";

		public string Text { get; }

		readonly MirrorLayerOverlay mirrorLayerOverlay;
		readonly HashSet<CPos> tiles;
		readonly int tile;

		public ClearSelectedMirrorTilesEditorAction(
			int tile,
			MirrorLayerOverlay mirrorLayerOverlay)
		{
			this.tile = tile;
			this.mirrorLayerOverlay = mirrorLayerOverlay;

			tiles = new HashSet<CPos>(mirrorLayerOverlay.Tiles[tile]);

			Text = TranslationProvider.GetString(ClearedSelectedMirrorTiles, Translation.Arguments("amount", tiles.Count, "type", tile));
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			mirrorLayerOverlay.ClearSelected(tile);
		}

		public void Undo()
		{
			mirrorLayerOverlay.SetSelected(tile, tiles);
		}
	}

	class ClearAllMirrorTilesEditorAction : IEditorAction
	{
		[TranslationReference("amount")]
		const string ClearedAllMirrorTiles = "notification-cleared-all-mirror-tiles";

		public string Text { get; }

		readonly MirrorLayerOverlay mirrorLayerOverlay;
		readonly Dictionary<int, HashSet<CPos>> tiles;

		public ClearAllMirrorTilesEditorAction(
			MirrorLayerOverlay mirrorLayerOverlay)
		{
			this.mirrorLayerOverlay = mirrorLayerOverlay;
			tiles = new Dictionary<int, HashSet<CPos>>(mirrorLayerOverlay.Tiles);

			var allTilesCount = tiles.Values.Select(x => x.Count).Sum();

			Text = TranslationProvider.GetString(ClearedAllMirrorTiles, Translation.Arguments("amount", allTilesCount));
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			mirrorLayerOverlay.ClearAll();
		}

		public void Undo()
		{
			mirrorLayerOverlay.SetAll(tiles);
		}
	}
}
