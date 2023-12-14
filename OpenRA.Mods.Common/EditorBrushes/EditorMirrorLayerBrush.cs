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
using System.IO;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public sealed class EditorMirrorLayerBrush : IEditorBrush
	{
		public readonly int Template;

		readonly WorldRenderer worldRenderer;
		readonly World world;
		readonly ITemplatedTerrainInfo terrainInfo;
		readonly EditorActionManager editorActionManager;
		readonly MirrorLayerOverlay mirrorLayerOverlay;

		bool painting;

		public EditorMirrorLayerBrush(EditorViewportControllerWidget editorWidget, int id, WorldRenderer wr)
		{
			worldRenderer = wr;
			world = wr.World;
			terrainInfo = world.Map.Rules.TerrainInfo as ITemplatedTerrainInfo;
			if (terrainInfo == null)
				throw new InvalidDataException("EditorMirrorLayerBrush can only be used with template-based tilesets");

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			mirrorLayerOverlay = world.WorldActor.Trait<MirrorLayerOverlay>();

			Template = id;
			worldRenderer = wr;
			world = wr.World;
		}

		public bool HandleMouseInput(MouseInput mi)
		{
			// Exclusively uses left and right mouse buttons, but nothing else
			if (mi.Button != MouseButton.Left && mi.Button != MouseButton.Right)
				return false;

			if (mi.Button == MouseButton.Left)
			{
				if (mi.Event == MouseInputEvent.Down)
					painting = true;
				else if (mi.Event == MouseInputEvent.Up)
					painting = false;
			}

			if (!painting)
				return true;

			if (mi.Event != MouseInputEvent.Down && mi.Event != MouseInputEvent.Move)
				return true;

			var cell = worldRenderer.Viewport.ViewToWorld(mi.Location);
			if (!world.Map.Contains(cell))
				return false;

			PaintCell(cell);

			return true;
		}

		void PaintCell(CPos cell)
		{

			var current = mirrorLayerOverlay.CellLayer[cell];
			if (current != Template)
			{
				editorActionManager.Add(new PaintMirrorTileEditorAction(Template, cell, mirrorLayerOverlay));
			}
		}

		public void Tick() { }

		public void Dispose()
		{
		}
	}

	class PaintMirrorTileEditorAction : IEditorAction
	{
		public string Text { get; }

		readonly int template;
		readonly CPos cell;

		readonly Queue<UndoMirrorTile> undoTiles = new();
		readonly MirrorLayerOverlay mirrorLayerOverlay;

		public PaintMirrorTileEditorAction(int template, CPos cell, MirrorLayerOverlay mirrorLayerOverlay)
		{
			this.mirrorLayerOverlay = mirrorLayerOverlay;
			this.template = template;
			this.cell = cell;
			Text = $"Added mirror tile {template}";
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			undoTiles.Enqueue(new UndoMirrorTile(cell, mirrorLayerOverlay.CellLayer[cell]));
			mirrorLayerOverlay.SetTile(cell, template);
		}

		public void Undo()
		{
			while (undoTiles.Count > 0)
			{
				var undoTile = undoTiles.Dequeue();
				mirrorLayerOverlay.SetTile(undoTile.Cell, undoTile.Tile);
			}
		}
	}

	class UndoMirrorTile
	{
		public CPos Cell { get; }
		public int? Tile { get; }

		public UndoMirrorTile(CPos cell, int? tile)
		{
			Cell = cell;
			Tile = tile;
		}
	}
}
