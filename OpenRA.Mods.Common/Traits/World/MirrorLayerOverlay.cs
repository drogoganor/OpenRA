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
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Traits;
using Color = OpenRA.Primitives.Color;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public class MirrorLayerOverlayInfo : TraitInfo
	{
		[Desc("A list of colors to be used for drawing.")]
		public readonly Color[] Colors = new[]
		{
			Color.FromArgb(255, 0, 0),
			Color.FromArgb(255, 127, 0),
			Color.FromArgb(255, 238, 70),
			Color.FromArgb(0, 255, 33),
			Color.FromArgb(0, 255, 255),
			Color.FromArgb(0, 42, 255),
			Color.FromArgb(165, 0, 255),
			Color.FromArgb(255, 0, 220),
		};

		[Desc("Default alpha blend.")]
		public readonly int Alpha = 85;

		[Desc("Color of the axis angle display.")]
		public readonly Color AxisAngleColor = Color.Crimson;

		public override object Create(ActorInitializer init)
		{
			return new MirrorLayerOverlay(init.Self, this);
		}
	}

	public class MirrorLayerOverlay : IRenderAnnotations, INotifyActorDisposing, IWorldLoaded
	{
		public class MirrorLayerFile
		{
			public Dictionary<int, List<int>> Tiles { get; set; }
			public MirrorTileMode MirrorMode { get; set; }
			public int NumSides { get; set; }
			public int AxisAngle { get; set; }
			public bool ShowAxisGuide { get; set; }
			public int TileAlpha { get; set; }
		}

		const double DegreesToRadians = Math.PI / 180;

		readonly int[] validFlipModeSides = { 2, 4 };

		public enum MirrorTileMode
		{
			None,
			Flip,
			Rotate
		}

		readonly World world;
		readonly WPos mapCenter;
		readonly Color[] alphaBlendColors;

		public readonly CellLayer<int?> CellLayer;
		public readonly Dictionary<int, HashSet<CPos>> Tiles = new();

		public bool Enabled = true;
		public MirrorTileMode MirrorMode { get; private set; } = MirrorTileMode.Flip;
		public MirrorLayerOverlayInfo Info { get; }
		public int NumSides = 2;
		public int AxisAngle;
		public bool ShowAxisGuide;
		public int TileAlpha
		{
			get => tileAlpha;
			set
			{
				tileAlpha = value;
				UpdateTileAlpha();
			}
		}

		int tileAlpha;
		bool disposed;

		public MirrorLayerOverlay(Actor self, MirrorLayerOverlayInfo info)
		{
			Info = info;
			world = self.World;
			var map = self.World.Map;

			tileAlpha = info.Alpha;
			alphaBlendColors = new Color[info.Colors.Length];
			UpdateTileAlpha();

			CellLayer = new CellLayer<int?>(map);

			mapCenter = GetMapCenterWPos();
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			try
			{
				var modData = Game.ModData;
				var mod = modData.Manifest.Metadata;
				var directory = Path.Combine(Platform.SupportDir, "Editor", modData.Manifest.Id, mod.Version, "MirrorTiles");
				if (!Directory.Exists(directory))
					return;

				if (string.IsNullOrWhiteSpace(world.Map.Package.Name))
					return;

				var mirrorTileFilename = $"{Path.GetFileNameWithoutExtension(world.Map.Package.Name)}.json";
				var mirrorTilePath = Path.Combine(directory, mirrorTileFilename);
				if (!File.Exists(mirrorTilePath))
					return;

				using (var streamReader = new StreamReader(mirrorTilePath))
				{
					var content = streamReader.ReadToEnd();
					var file = JsonConvert.DeserializeObject<MirrorLayerFile>(content);

					TileAlpha = file.TileAlpha;
					MirrorMode = file.MirrorMode;
					NumSides = file.NumSides;
					AxisAngle = file.AxisAngle;
					ShowAxisGuide = file.ShowAxisGuide;

					var savedTilesHashSetDictionary = file.Tiles.ToDictionary(x => x.Key, x => x.Value.Select(bits => new CPos(bits)).ToHashSet());
					SetAll(savedTilesHashSetDictionary);
				}
			}
			catch (Exception e)
			{
				Log.Write("debug", "Failed to load map editor mirror tiles.");
				Log.Write("debug", e);
			}
		}

		public MirrorLayerFile ToFile()
		{
			var tilesBitsDictionary = Tiles.ToDictionary(x => x.Key, x => x.Value.Select(cpos => cpos.Bits).ToList());
			return new MirrorLayerFile
			{
				Tiles = tilesBitsDictionary,
				TileAlpha = TileAlpha,
				MirrorMode = MirrorMode,
				NumSides = NumSides,
				AxisAngle = AxisAngle,
				ShowAxisGuide = ShowAxisGuide,
			};
		}

		void UpdateTileAlpha()
		{
			for (var i = 0; i < Info.Colors.Length; i++)
				alphaBlendColors[i] = Color.FromArgb(tileAlpha, Info.Colors[i]);
		}

		public void ClearSelected(int tileType)
		{
			if (Tiles.TryGetValue(tileType, out var set))
				foreach (var pos in set)
					SetTile(pos, null);
		}

		public void ClearAll()
		{
			foreach (var position in Tiles.SelectMany(x => x.Value))
				CellLayer[position] = null;

			Tiles.Clear();
		}

		public void SetAll(Dictionary<int, HashSet<CPos>> newTiles)
		{
			ClearAll();

			foreach (var type in newTiles)
			{
				var set = new HashSet<CPos>();
				Tiles.Add(type.Key, set);

				foreach (var position in type.Value)
				{
					set.Add(position);
					CellLayer[position] = type.Key;
				}
			}
		}

		public void SetSelected(int tile, HashSet<CPos> newTiles)
		{
			var type = Tiles[tile];
			foreach (var pos in type)
				SetTile(pos, null);

			type.Clear();

			foreach (var pos in newTiles)
			{
				type.Add(pos);
				CellLayer[pos] = tile;
			}
		}

		public void SetMirrorMode(MirrorTileMode mirrorMode)
		{
			MirrorMode = mirrorMode;

			if (mirrorMode == MirrorTileMode.Flip && !validFlipModeSides.Contains(NumSides))
				NumSides = validFlipModeSides[0];
		}

		WPos GetMapCenterWPos()
		{
			var map = world.Map;

			var boundsWidth = map.AllCells.BottomRight.X - map.AllCells.TopLeft.X;
			var boundsHeight = map.AllCells.BottomRight.Y - map.AllCells.TopLeft.Y;

			var xIsEven = boundsWidth % 2 == 0;
			var yIsEven = boundsHeight % 2 == 0;

			var xCenter = boundsWidth / 2;
			var yCenter = boundsHeight / 2;

			var centerWpos = map.CenterOfCell(new CPos(xCenter, yCenter));
			if (xIsEven)
				centerWpos -= new WVec(512, 0, 0);

			if (yIsEven)
				centerWpos -= new WVec(0, 512, 0);

			return centerWpos;
		}

		public CPos[] CalculateMirrorPositions(CPos cell)
		{
			const int DegreesInCircle = 360;

			var map = world.Map;

			var wpos = map.CenterOfCell(cell);
			var wposVec = wpos - mapCenter;
			var angle = DegreesInCircle / NumSides;

			var targets = new List<CPos>();

			if (map.Contains(cell))
				targets.Add(cell);

			if (MirrorMode == MirrorTileMode.Flip)
			{
				var startAxis = new WVec(1024, 0, 0);
				var axes = new List<WVec>();
				for (var i = 0; i < NumSides / 2; i++)
				{
					var targetAngle = (i * angle + AxisAngle) * DegreesToRadians;
					var point = new WVec((int)(startAxis.X * Math.Cos(targetAngle) - startAxis.Y * Math.Sin(targetAngle)),
						(int)(startAxis.X * Math.Sin(targetAngle) + startAxis.Y * Math.Cos(targetAngle)),
						wpos.Z);

					axes.Add(point);
				}

				foreach (var axis in axes)
				{
					var point = GetAxisMirrorPoint(mapCenter, axis, wpos);
					var cellPoint = map.CellContaining(point);

					if (map.Contains(cellPoint))
						targets.Add(cellPoint);
				}

				// Mirror twice for both
				if (axes.Count == 2)
				{
					var point = GetAxisMirrorPoint(mapCenter, axes[0], wpos);
					point = GetAxisMirrorPoint(mapCenter, axes[1], point);
					var cellPoint = map.CellContaining(point);

					if (map.Contains(cellPoint))
						targets.Add(cellPoint);
				}

				///////////////

				static WPos GetAxisMirrorPoint(WPos center, WVec axis, WPos point)
				{
					var testPoint = center - new WVec(point.X, point.Y, 0);
					var a = axis.Y;
					var b = -axis.X;
					var c = -a * 0 - b * 0;

					var m = Math.Sqrt(a * a + b * b);
					var aDash = a / m;
					var bDash = b / m;
					var cDash = c / m;

					var d = aDash * testPoint.X + bDash * testPoint.Y + cDash;
					var pxDash = testPoint.X - 2 * aDash * d;
					var pyDash = testPoint.Y - 2 * bDash * d;

					return new WPos((int)pxDash + center.X, (int)pyDash + center.Y, 0);
				}
			}
			else if (MirrorMode == MirrorTileMode.Rotate)
			{
				// Rotate
				var flipAngleRadians = DegreesToRadians * angle;

				var sidesAreEven = NumSides % 2 == 0;
				var oddSideStartIndex = (int)Math.Floor((double)NumSides / 2);
				var startIndex = sidesAreEven ? 0 : -oddSideStartIndex;
				var count = sidesAreEven ? NumSides : oddSideStartIndex + 1;

				for (var i = startIndex; i < count; i++)
				{
					var targetAngle = i * flipAngleRadians;
					var point = new WPos((int)(wposVec.X * Math.Cos(targetAngle) - wposVec.Y * Math.Sin(targetAngle)),
						(int)(wposVec.X * Math.Sin(targetAngle) + wposVec.Y * Math.Cos(targetAngle)),
						wpos.Z);

					var cellPoint = map.CellContaining(point + new WVec(mapCenter.X, mapCenter.Y, 0));

					if (map.Contains(cellPoint))
						targets.Add(cellPoint);
				}
			}

			return targets.ToArray();
		}

		public void SetTile(CPos target, int? tileType)
		{
			if (!world.Map.Contains(target))
				return;

			// Maintain map of tile types for selective clearing
			var prevTile = CellLayer[target];
			if (prevTile.HasValue && Tiles.TryGetValue(prevTile.Value, out var set))
			{
				set.Remove(target);
			}

			if (tileType.HasValue)
			{
				if (Tiles.TryGetValue(tileType.Value, out set))
					set.Add(target);
				else
					Tiles.Add(tileType.Value, new HashSet<CPos> { target });

				CellLayer[target] = tileType;
			}
			else
			{
				CellLayer[target] = null;
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			disposed = true;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			foreach (var cellPair in Tiles)
			{
				foreach (var cellPos in cellPair.Value)
				{
					yield return new MirrorTileRenderable(cellPos, alphaBlendColors[cellPair.Key]);
				}
			}

			if (!ShowAxisGuide || MirrorMode != MirrorTileMode.Flip)
				yield break;

			const int Width = 1;

			var color = Info.AxisAngleColor;
			var map = wr.World.Map;
			var lineLength = (int)Math.Sqrt(Math.Pow(map.MapSize.X, 2) + Math.Pow(map.MapSize.Y, 2)) / 2 * 1024;
			var points = new[]
			{
				new WVec(0, lineLength, 0),
				new WVec(0, -lineLength, 0),
				new WVec(lineLength, 0, 0),
				new WVec(-lineLength, 0, 0),
			};

			for (var i = 0; i < NumSides; i++)
			{
				var point = points[i];
				var targetAngle = AxisAngle * DegreesToRadians;
				var rotatedVec = new WVec((int)(point.X * Math.Cos(targetAngle) - point.Y * Math.Sin(targetAngle)),
					(int)(point.X * Math.Sin(targetAngle) + point.Y * Math.Cos(targetAngle)),
					0);
				var rotatedPos = mapCenter + rotatedVec;

				yield return new LineAnnotationRenderable(mapCenter, rotatedPos, Width, color, color);
			}
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
