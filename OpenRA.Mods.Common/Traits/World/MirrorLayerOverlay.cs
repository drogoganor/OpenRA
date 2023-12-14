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
using OpenRA.Traits;
using Color = OpenRA.Primitives.Color;
using Rectangle = OpenRA.Primitives.Rectangle;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public class MirrorLayerOverlayInfo : TraitInfo
	{
		[FieldLoader.Require]
		public readonly Color[] Colors = null;

		[FieldLoader.Require]
		public readonly string TileImage = null;

		[FieldLoader.Require]
		public readonly string TileSequence = null;

		public readonly float Alpha = 0.5f;

		public override object Create(ActorInitializer init)
		{
			return new MirrorLayerOverlay(init.Self, this);
		}
	}

	public class MirrorLayerOverlay : IRenderAboveWorld, IWorldLoaded, INotifyActorDisposing
	{
		readonly World world;

		public bool Enabled = false;
		TerrainSpriteLayer render;

		bool disposed;

		public CellLayer<int?> CellLayer { get; }
		public int TileIndex = 0;

		public MirrorLayerOverlayInfo Info { get; }
		public int NumSides = 2;
		public int StartAngle = 0;
		public bool UseFlipMethod = true;

		readonly ISpriteSequence spriteSequence;

		public MirrorLayerOverlay(Actor self, MirrorLayerOverlayInfo info)
		{
			Info = info;
			world = self.World;
			var map = self.World.Map;

			spriteSequence = map.Sequences.GetSequence(info.TileImage, info.TileSequence);

			CellLayer = new CellLayer<int?>(map);
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			var first = spriteSequence.GetSprite(0);
			var emptySprite = new Sprite(first.Sheet, Rectangle.Empty, TextureChannel.Alpha);
			render = new TerrainSpriteLayer(w, wr, emptySprite, BlendMode.Alpha, wr.World.Type != WorldType.Editor);

			CellLayer.CellEntryChanged += UpdateTerrainCell;
		}

		public void SetTile(CPos cell, int? tileType)
		{
			const double DegreesToRadians = Math.PI / 180;
			var map = world.Map;
			var gridType = map.Grid.Type;

			var xIsEven = map.MapSize.X % 2 == 0;
			var yIsEven = map.MapSize.Y % 2 == 0;

			var xCenter = gridType == MapGridType.RectangularIsometric ? map.MapSize.X : map.MapSize.X / 2;
			var yCenter = gridType == MapGridType.RectangularIsometric ? 0 : map.MapSize.Y / 2;

			var center = new CPos(xCenter, yCenter);
			var centerWpos = map.CenterOfCell(center);
			if (xIsEven)
				centerWpos -= new WVec(512, 0, 0);
			if (yIsEven)
				centerWpos -= new WVec(0, 512, 0);

			var wpos = map.CenterOfCell(cell);
			var wposVec = wpos - centerWpos;
			var angle = 360 / NumSides;

			var targets = new List<CPos>();
			if (UseFlipMethod)
			{
				if (NumSides != 2 && NumSides != 4) return;

				var startAxis = new WVec(1024, 0, 0);
				var axes = new List<WVec>();
				for (var i = 0; i < NumSides / 2; i++)
				{
					var targetAngle = (i * angle + StartAngle) * DegreesToRadians;
					var point = new WVec((int)(startAxis.X * Math.Cos(targetAngle) - startAxis.Y * Math.Sin(targetAngle)),
						(int)(startAxis.X * Math.Sin(targetAngle) + startAxis.Y * Math.Cos(targetAngle)),
						wpos.Z);

					axes.Add(point);
				}

				targets.Add(cell);
				foreach (var axis in axes)
				{
					var point = GetAxisMirrorPoint(centerWpos, axis, wpos);
					var cellPoint = map.CellContaining(point);
					targets.Add(cellPoint);
				}

				// Mirror twice for both
				if (axes.Count == 2)
				{
					var point = GetAxisMirrorPoint(centerWpos, axes[0], wpos);
					point = GetAxisMirrorPoint(centerWpos, axes[1], point);
					var cellPoint = map.CellContaining(point);
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
			else
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

					var cellPoint = map.CellContaining(point + new WVec(centerWpos.X, centerWpos.Y, 0));

					targets.Add(cellPoint);
				}
			}

			foreach (var target in targets)
			{
				if (!map.Contains(target))
					continue;

				CellLayer[target] = tileType ?? null;
			}
		}

		void UpdateTerrainCell(CPos cell)
		{
			if (!world.Map.Contains(cell))
				return;

			var currentValue = CellLayer[cell];
			if (currentValue.HasValue)
			{
				var tileSprite = spriteSequence.GetSprite(currentValue.Value);
				var tileSpriteScale = spriteSequence.Scale;
				render.Update(cell, tileSprite, null, tileSpriteScale, Info.Alpha);
			}
			else
			{
				render.Clear(cell);
			}
		}

		void IRenderAboveWorld.RenderAboveWorld(Actor self, WorldRenderer wr)
		{
			if (Enabled)
				render.Draw(wr.Viewport);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			render.Dispose();
			disposed = true;
		}
	}
}
