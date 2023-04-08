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
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	class ProductionQueueFromSelectionInfo : TraitInfo
	{
		public readonly string ProductionTabsWidget = null;
		public readonly string ProductionPaletteWidget = null;

		public override object Create(ActorInitializer init) { return new ProductionQueueFromSelection(init.World, this); }
	}

	class ProductionQueueFromSelection : INotifySelection
	{
		readonly World world;
		readonly Lazy<ProductionTabsWidget> tabsWidget;
		readonly Lazy<ProductionPaletteWidget> paletteWidget;

		public ProductionQueueFromSelection(World world, ProductionQueueFromSelectionInfo info)
		{
			this.world = world;

			tabsWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.ProductionTabsWidget) as ProductionTabsWidget);
			paletteWidget = Exts.Lazy(() => Ui.Root.GetOrNull(info.ProductionPaletteWidget) as ProductionPaletteWidget);
		}

		void INotifySelection.SelectionChanged()
		{
			// Disable for spectators
			if (world.LocalPlayer == null)
				return;

			// Queue-per-actor
			var queue = world.Selection.Actors
				.SelectMany(a => a.TraitsImplementing<ProductionQueue>())
				.FirstOrDefault(q => q.Enabled);

			var paletteProvider = world.Selection.Actors
				.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
				.SelectMany(a => a.TraitsImplementing<DefaultProductionPaletteProvider>())
				.FirstOrDefault(); // q => q.Enabled

			if (paletteProvider != null && queue != null)
			{
				paletteProvider.CurrentQueue = queue;

				paletteWidget.Value.ProductionPaletteProvider = paletteProvider;
				return;
			}

			// Queue-per-player
			var types = world.Selection.Actors.Where(a => a.IsInWorld && a.World.LocalPlayer == a.Owner)
				.SelectMany(a => a.TraitsImplementing<Production>().Where(p => !p.IsTraitDisabled))
				.SelectMany(t => t.Info.Produces);

			queue = world.LocalPlayer.PlayerActor.TraitsImplementing<ProductionQueue>()
				.FirstOrDefault(q => q.Enabled && types.Contains(q.Info.Type));

			paletteProvider = world.LocalPlayer.PlayerActor.TraitsImplementing<DefaultProductionPaletteProvider>()
				.FirstOrDefault();

			if (queue == null || paletteProvider == null || !queue.BuildableItems().Any())
				return;

			paletteProvider.CurrentQueue = queue;
			paletteWidget.Value.ProductionPaletteProvider = paletteProvider;

			if (tabsWidget.Value != null)
				tabsWidget.Value.CurrentQueue = queue;
		}
	}
}
