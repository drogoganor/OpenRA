#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits
{
	class ProductionQueueFromSelectionInfo : ITraitInfo
	{
		public string ProductionParent = null;
		public string ProductionTabsWidget = null;
		public string ProductionPaletteWidget = null;
		public string BuildSelectPalette = null;

		public object Create(ActorInitializer init) { return new ProductionQueueFromSelection(init.World, this); }
	}

	// Now performs extraordinary hacking to show two different types of build palettes - regular production queues and building picker
	class ProductionQueueFromSelection : INotifySelection
	{
		readonly World world;
		readonly Lazy<ProductionTabsWidget> tabsWidget;
		readonly Lazy<ProductionPaletteWidget> paletteWidget;
		readonly Lazy<BuildSelectPaletteWidget> buildSelectWidget;
		readonly Lazy<Widget> productionParentWidget;

		public ProductionQueueFromSelection(World world, ProductionQueueFromSelectionInfo info)
		{
			this.world = world;

			tabsWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.ProductionTabsWidget) as ProductionTabsWidget);
			paletteWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.ProductionPaletteWidget) as ProductionPaletteWidget);
			buildSelectWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.BuildSelectPalette) as BuildSelectPaletteWidget);
			productionParentWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.ProductionParent));
		}

		void INotifySelection.SelectionChanged()
		{
			// Disable for spectators
			if (world.LocalPlayer == null)
				return;

			// Check for builder unit
			var builderQueue = world.Selection.Actors
				.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
				.SelectMany(a => a.TraitsImplementing<BuilderUnit>())
				.FirstOrDefault(q => q.Enabled);

			if (builderQueue == null)
			{
				var types = world.Selection.Actors.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
					.SelectMany(a => a.TraitsImplementing<Production>())
					.SelectMany(t => t.Info.Produces);

				builderQueue = world.LocalPlayer.PlayerActor.TraitsImplementing<BuilderUnit>()
					.FirstOrDefault(q => q.Enabled && types.Contains(q.Info.Type));
			}

			buildSelectWidget.Value.Parent.Visible = builderQueue != null;
			productionParentWidget.Value.Visible = builderQueue == null;
			if (builderQueue != null)
			{
				buildSelectWidget.Value.CurrentQueue = builderQueue;
				return;
			}

			// Queue-per-actor
			var queue = world.Selection.Actors
				.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
				.SelectMany(a => a.TraitsImplementing<ProductionQueue>())
				.FirstOrDefault(q => q.Enabled);

			// Queue-per-player
			if (queue == null)
			{
				var types = world.Selection.Actors.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
					.SelectMany(a => a.TraitsImplementing<Production>())
					.SelectMany(t => t.Info.Produces);

				queue = world.LocalPlayer.PlayerActor.TraitsImplementing<ProductionQueue>()
					.FirstOrDefault(q => q.Enabled && types.Contains(q.Info.Type));
			}

			if (queue == null)
				return;

			if (tabsWidget.Value != null)
				tabsWidget.Value.CurrentQueue = queue;
			else if (paletteWidget.Value != null)
				paletteWidget.Value.CurrentQueue = queue;
		}
	}
}
