using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.EditorBrushes
{
	public class ClipboardTile
	{
		public TerrainTile TerrainTile { get; set; }
		public ResourceTile ResourceTile { get; set; }
		public ResourceLayerContents ResourceLayerContents { get; set; }
		public byte Height { get; set; }
	}

	public class EditorClipboard
	{
		public readonly CellRegion CellRegion;
		public readonly Dictionary<string, EditorActorPreview> Actors;
		public readonly Dictionary<CPos, ClipboardTile> Tiles;

		public EditorClipboard(CellRegion cellRegion, Dictionary<string, EditorActorPreview> actors, Dictionary<CPos, ClipboardTile> tiles)
		{
			CellRegion = cellRegion;
			Actors = actors;
			Tiles = tiles;
		}
	}
}
