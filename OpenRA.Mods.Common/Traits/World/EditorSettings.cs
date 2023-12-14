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
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	[Desc("Settings for the editor such as selectable clear tiles.")]
	public class EditorSettingsInfo : TraitInfo
	{
		[Desc("Tile indices to select from the selection clear tile control.")]
		[FieldLoader.LoadUsing(nameof(LoadTerrainClearTiles))]
		public readonly Dictionary<string, EditorTerrainClearTileInfo> TerrainClearTiles;

		static object LoadTerrainClearTiles(MiniYaml yaml)
		{
			var infoDictionary = new Dictionary<string, EditorTerrainClearTileInfo>();
			var terrainClearTiles = yaml.Nodes.First(x => x.Key == "TerrainClearTiles");
			foreach (var node in terrainClearTiles.Value.Nodes.Where(n => n.Key.StartsWith("Tiles", StringComparison.InvariantCulture)))
			{
				var info = new EditorTerrainClearTileInfo();
				FieldLoader.Load(info, node.Value);
				infoDictionary.Add(node.Key, info);
			}

			return infoDictionary;
		}

		public override object Create(ActorInitializer init) { return new EditorSettings(this); }
	}

	public class EditorTerrainClearTileInfo
	{
		public readonly string TilesetName;
		public readonly ushort[] TileIndices;
	}

	public class EditorSettings
	{
		public EditorSettingsInfo Info { get; }

		public EditorSettings(EditorSettingsInfo info)
		{
			Info = info;
		}
	}
}
