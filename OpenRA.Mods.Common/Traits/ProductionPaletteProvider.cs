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
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	// DTO object
	public class GenericProductionPaletteIcon
	{
		public ActorInfo Actor;
		public string Name;
		public Sprite Sprite;
		public string Palette;
	}

	public class DefaultProductionPaletteProviderInfo : TraitInfo
	{
		public readonly string ClickSound;
		public readonly string ClickDisabledSound;

		public override object Create(ActorInitializer init)
		{
			return new DefaultProductionPaletteProvider(init, this);
		}
	}

	public class DefaultProductionPaletteProvider : IProductionPaletteProvider, INotifySelected
	{
		public IList<GenericProductionPaletteIcon> Icons { get; private set; } = new List<GenericProductionPaletteIcon>();

		readonly World world;
		readonly DefaultProductionPaletteProviderInfo info;
		ProductionQueue currentQueue;
		public ProductionQueue CurrentQueue
		{
			get => currentQueue;
			set
			{
				currentQueue = value;

				RefreshIcons();
			}
		}

		public IEnumerable<ActorInfo> AllBuildables
		{
			get
			{
				if (CurrentQueue == null)
					return Enumerable.Empty<ActorInfo>();

				return CurrentQueue.AllItems().OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder);
			}
		}

		public DefaultProductionPaletteProvider(ActorInitializer init, DefaultProductionPaletteProviderInfo info)
		{
			world = init.World;
			this.info = info;
		}

		public void RefreshIcons()
		{
			Icons = new List<GenericProductionPaletteIcon>();
			var producer = CurrentQueue != null ? CurrentQueue.MostLikelyProducer() : default;
			if (CurrentQueue == null || producer.Trait == null)
			{
				return;
			}

			var faction = producer.Trait.Faction;

			foreach (var item in AllBuildables)
			{
				var rsi = item.TraitInfo<RenderSpritesInfo>();
				var icon = new Animation(world, rsi.GetImage(item, faction));
				var bi = item.TraitInfo<BuildableInfo>();
				icon.Play(bi.Icon);

				var palette = bi.IconPaletteIsPlayerPalette ? bi.IconPalette + producer.Actor.Owner.InternalName : bi.IconPalette;

				var pi = new GenericProductionPaletteIcon()
				{
					Actor = item,
					Name = item.Name,
					Sprite = icon.Image,
					Palette = palette,
				};

				Icons.Add(pi);
			}
		}

		void INotifySelected.Selected(Actor self)
		{
			CurrentQueue = self.TraitOrDefault<ProductionQueue>();
		}

		public bool HandleEvent(IProductionPaletteIcon icon, MouseButton btn, Modifiers modifiers, WorldRenderer wr)
		{
			var productionIcon = icon as DefaultProductionIcon;
			var startCount = modifiers.HasModifier(Modifiers.Shift) ? 5 : 1;

			// PERF: avoid an unnecessary enumeration by casting back to its known type
			var cancelCount = modifiers.HasModifier(Modifiers.Ctrl) ? ((List<ProductionItem>)CurrentQueue.AllQueued()).Count : startCount;
			var item = productionIcon.Queued.FirstOrDefault();
			var handled = btn == MouseButton.Left ? HandleLeftClick(item, productionIcon, startCount, modifiers, wr)
				: btn == MouseButton.Right ? HandleRightClick(item, productionIcon, cancelCount)
				: btn == MouseButton.Middle && HandleMiddleClick(item, productionIcon, cancelCount);

			if (!handled)
				Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", info.ClickDisabledSound, null);

			return true;
		}

		bool HandleLeftClick(ProductionItem item, DefaultProductionIcon icon, int handleCount, Modifiers modifiers, WorldRenderer wr)
		{
			if (PickUpCompletedBuildingIcon(icon, item, wr))
			{
				Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", info.ClickSound, null);
				return true;
			}

			if (item != null && item.Paused)
			{
				// Resume a paused item
				Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", info.ClickSound, null);
				Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Speech", CurrentQueue.Info.QueuedAudio, world.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(CurrentQueue.Info.QueuedTextNotification, world.LocalPlayer);

				world.IssueOrder(Order.PauseProduction(CurrentQueue.Actor, icon.Name, false));
				return true;
			}

			var buildable = CurrentQueue.BuildableItems().FirstOrDefault(a => a.Name == icon.Name);

			if (buildable != null)
			{
				// Queue a new item
				Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", info.ClickSound, null);
				var canQueue = CurrentQueue.CanQueue(buildable, out var notification, out var textNotification);

				if (!CurrentQueue.AllQueued().Any())
				{
					Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Speech", notification, world.LocalPlayer.Faction.InternalName);
					TextNotificationsManager.AddTransientLine(textNotification, world.LocalPlayer);
				}

				if (canQueue)
				{
					var queued = !modifiers.HasModifier(Modifiers.Ctrl);
					world.IssueOrder(Order.StartProduction(CurrentQueue.Actor, icon.Name, handleCount, queued));
					return true;
				}
			}

			return false;
		}

		protected bool PickUpCompletedBuildingIcon(DefaultProductionIcon icon, ProductionItem item, WorldRenderer wr)
		{
			var actor = world.Map.Rules.Actors[icon.Name];

			if (item != null && item.Done && actor.HasTraitInfo<BuildingInfo>())
			{
				world.OrderGenerator = new PlaceBuildingOrderGenerator(CurrentQueue, icon.Name, wr);
				return true;
			}

			return false;
		}

		public void PickUpCompletedBuilding()
		{
			foreach (var icon in Icons)
			{
				var item = icon.Queued.FirstOrDefault();
				if (PickUpCompletedBuildingIcon(icon, item))
					break;
			}
		}

		protected override bool HandleRightClick(ProductionItem item, DefaultProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);

			if (ProductionPaletteProvider.CurrentQueue.Info.DisallowPaused || item.Paused || item.Done || item.TotalCost == item.RemainingCost)
			{
				// Instantly cancel items that haven't started, have finished, or if the queue doesn't support pausing
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", ProductionPaletteProvider.CurrentQueue.Info.CancelledAudio, World.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(ProductionPaletteProvider.CurrentQueue.Info.CancelledTextNotification, World.LocalPlayer);

				World.IssueOrder(Order.CancelProduction(ProductionPaletteProvider.CurrentQueue.Actor, icon.Name, handleCount));
			}
			else
			{
				// Pause an existing item
				Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", ProductionPaletteProvider.CurrentQueue.Info.OnHoldAudio, World.LocalPlayer.Faction.InternalName);
				TextNotificationsManager.AddTransientLine(ProductionPaletteProvider.CurrentQueue.Info.OnHoldTextNotification, World.LocalPlayer);

				World.IssueOrder(Order.PauseProduction(ProductionPaletteProvider.CurrentQueue.Actor, icon.Name, true));
			}

			return true;
		}

		protected override bool HandleMiddleClick(ProductionItem item, DefaultProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			// Directly cancel, skipping "on-hold"
			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", ClickSound, null);
			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", ProductionPaletteProvider.CurrentQueue.Info.CancelledAudio, World.LocalPlayer.Faction.InternalName);
			TextNotificationsManager.AddTransientLine(ProductionPaletteProvider.CurrentQueue.Info.CancelledTextNotification, World.LocalPlayer);

			World.IssueOrder(Order.CancelProduction(ProductionPaletteProvider.CurrentQueue.Actor, icon.Name, handleCount));

			return true;
		}
	}
}
