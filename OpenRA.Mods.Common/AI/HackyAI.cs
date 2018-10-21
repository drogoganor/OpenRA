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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.AI
{
	public sealed class HackyAI : ITick, IBot, INotifyDamage
	{
		public MersenneTwister Random { get; private set; }
		public readonly AIInfo Info;

		public CPos GetRandomBaseCenter()
		{
			var randomConstructionYard = World.Actors.Where(a => a.Owner == Player &&
				Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name))
				.RandomOrDefault(Random);

			return randomConstructionYard != null ? randomConstructionYard.Location : initialBaseCenter;
		}

		public bool IsEnabled;
		public List<Squad> Squads = new List<Squad>();
		public Player Player { get; private set; }

		readonly Func<Actor, bool> isEnemyUnit;
		readonly Predicate<Actor> unitCannotBeOrdered;

		CPos initialBaseCenter;
		PowerManager playerPower;
		PlayerResources playerResource;
		int ticks;

		BitArray resourceTypeIndices;

		AIHarvesterManager harvManager;
		AISupportPowerManager supportPowerManager;

		List<BaseBuilder> builders = new List<BaseBuilder>();

		List<Actor> unitsHangingAroundTheBase = new List<Actor>();

		// Units that the ai already knows about. Any unit not on this list needs to be given a role.
		List<Actor> activeUnits = new List<Actor>();

		// Harvesters are usually listed under ExcludeFromSquads, so they're not included in the activeUnits list, but still needed in AIHarvesterManager.
		// TODO: Consider adding an explicit UnitsCommonNames.Harvester category.
		List<Actor> harvesters = new List<Actor>();

		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		public readonly World World;
		public Map Map { get { return World.Map; } }
		IBotInfo IBot.Info { get { return Info; } }

		int rushTicks;
		int assignRolesTicks;
		int attackForceTicks;
		int minAttackForceDelayTicks;
		int minCaptureDelayTicks;
		readonly int maximumCaptureTargetOptions;

		readonly Queue<Order> orders = new Queue<Order>();

		public HackyAI(AIInfo info, ActorInitializer init)
		{
			Info = info;
			World = init.World;

			if (World.Type == WorldType.Editor)
				return;

			isEnemyUnit = unit =>
				Player.Stances[unit.Owner] == Stance.Enemy
					&& !unit.Info.HasTraitInfo<HuskInfo>()
					&& unit.Info.HasTraitInfo<ITargetableInfo>();

			unitCannotBeOrdered = a => a.Owner != Player || a.IsDead || !a.IsInWorld;

			maximumCaptureTargetOptions = Math.Max(1, Info.MaximumCaptureTargetOptions);
		}

		public static void BotDebug(string s, params object[] args)
		{
			if (Game.Settings.Debug.BotDebug)
				Game.Debug(s, args);
		}

		// Called by the host's player creation code
		public void Activate(Player p)
		{
			Player = p;
			IsEnabled = true;
			playerPower = p.PlayerActor.TraitOrDefault<PowerManager>();
			playerResource = p.PlayerActor.Trait<PlayerResources>();

			harvManager = new AIHarvesterManager(this, p);
			supportPowerManager = new AISupportPowerManager(this, p);

			foreach (var building in Info.BuildingQueues)
				builders.Add(new BaseBuilder(this, building, p, playerPower, playerResource));
			foreach (var defense in Info.DefenseQueues)
				builders.Add(new BaseBuilder(this, defense, p, playerPower, playerResource));

			Random = new MersenneTwister(Game.CosmeticRandom.Next());

			// Avoid all AIs trying to rush in the same tick, randomize their initial rush a little.
			var smallFractionOfRushInterval = Info.RushInterval / 20;
			rushTicks = Random.Next(Info.RushInterval - smallFractionOfRushInterval, Info.RushInterval + smallFractionOfRushInterval);

			// Avoid all AIs reevaluating assignments on the same tick, randomize their initial evaluation delay.
			assignRolesTicks = Random.Next(0, Info.AssignRolesInterval);
			attackForceTicks = Random.Next(0, Info.AttackForceInterval);
			minAttackForceDelayTicks = Random.Next(0, Info.MinimumAttackForceDelay);
			minCaptureDelayTicks = Random.Next(0, Info.MinimumCaptureDelay);

			var tileset = World.Map.Rules.TileSet;
			resourceTypeIndices = new BitArray(tileset.TerrainInfo.Length); // Big enough
			foreach (var t in Map.Rules.Actors["world"].TraitInfos<ResourceTypeInfo>())
				resourceTypeIndices.Set(tileset.GetTerrainIndex(t.TerrainType), true);
		}

		public void QueueOrder(Order order)
		{
			orders.Enqueue(order);
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(Random);
			return HasAdequateAirUnitReloadBuildings(unit) ? unit : null;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = Player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(Random))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) < unit.Value * myUnits.Count)
						if (HasAdequateAirUnitReloadBuildings(Map.Rules.Actors[unit.Key]))
							return Map.Rules.Actors[unit.Key];

			return null;
		}

		IEnumerable<Actor> GetActorsWithTrait<T>()
		{
			return World.ActorsHavingTrait<T>();
		}

		int CountActorsWithTrait<T>(string actorName, Player owner)
		{
			return GetActorsWithTrait<T>().Count(a => a.Owner == owner && a.Info.Name == actorName);
		}

		int CountBuildingByCommonName(HashSet<string> buildings, Player owner)
		{
			return GetActorsWithTrait<Building>()
				.Count(a => a.Owner == owner && buildings.Contains(a.Info.Name));
		}

		public ActorInfo GetInfoByCommonName(HashSet<string> names, Player owner)
		{
			return Map.Rules.Actors.Where(k => names.Contains(k.Key)).Random(Random).Value;
		}

		public bool HasAdequateFact()
		{
			// Require at least one construction yard, unless we have no vehicles factory (can't build it).
			return CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.VehiclesFactory, Player) == 0;
		}

		public bool HasAdequateProc()
		{
			// Require at least one refinery, unless we can't build it.
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) == 0;
		}

		public bool HasMinimumProc()
		{
			// Require at least two refineries, unless we have no power (can't build it)
			// or barracks (higher priority?)
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) >= 2 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Barracks, Player) == 0;
		}

		// For mods like RA (number of building must match the number of aircraft)
		bool HasAdequateAirUnitReloadBuildings(ActorInfo actorInfo)
		{
			var aircraftInfo = actorInfo.TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo == null)
				return true;

			// If the aircraft has at least 1 AmmoPool and defines 1 or more RearmBuildings, check if we have enough of those
			var hasAmmoPool = actorInfo.TraitInfos<AmmoPoolInfo>().Any();
			if (hasAmmoPool && aircraftInfo.RearmBuildings.Count > 0)
			{
				var countOwnAir = CountActorsWithTrait<IPositionable>(actorInfo.Name, Player);
				var countBuildings = aircraftInfo.RearmBuildings.Sum(b => CountActorsWithTrait<Building>(b, Player));
				if (countOwnAir >= countBuildings)
					return false;
			}

			return true;
		}

		CPos defenseCenter;
		public CPos? ChooseBuildLocation(string actorType, bool distanceToBaseIsImportant, BuildingType type)
		{
			var ai = Map.Rules.Actors[actorType];
			var bi = ai.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;

			// Find the buildable cell that is closest to pos and centered around center
			Func<CPos, CPos, int, int, CPos?> findPos = (center, target, minRange, maxRange) =>
			{
				var cells = Map.FindTilesInAnnulus(center, minRange, maxRange);

				// Sort by distance to target if we have one
				if (center != target)
					cells = cells.OrderBy(c => (c - target).LengthSquared);
				else
					cells = cells.Shuffle(Random);

				foreach (var cell in cells)
				{
					if (!World.CanPlaceBuilding(cell, ai, bi, null))
						continue;

					if (distanceToBaseIsImportant && !bi.IsCloseEnoughToBase(World, Player, ai, cell))
						continue;

					return cell;
				}

				return null;
			};

			var baseCenter = GetRandomBaseCenter();

			switch (type)
			{
				case BuildingType.Defense:

					// Build near the closest enemy structure
					var closestEnemy = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && Player.Stances[a.Owner] == Stance.Enemy)
						.ClosestTo(World.Map.CenterOfCell(defenseCenter));

					var targetCell = closestEnemy != null ? closestEnemy.Location : baseCenter;
					return findPos(defenseCenter, targetCell, Info.MinimumDefenseRadius, Info.MaximumDefenseRadius);

				case BuildingType.Refinery:

					// Try and place the refinery near a resource field
					var nearbyResources = Map.FindTilesInAnnulus(baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius)
						.Where(a => resourceTypeIndices.Get(Map.GetTerrainIndex(a)))
						.Shuffle(Random).Take(Info.MaxResourceCellsToCheck);

					foreach (var r in nearbyResources)
					{
						var found = findPos(baseCenter, r, Info.MinBaseRadius, Info.MaxBaseRadius);
						if (found != null)
							return found;
					}

					// Try and find a free spot somewhere else in the base
					return findPos(baseCenter, baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius);

				case BuildingType.Building:
					return findPos(baseCenter, baseCenter, Info.MinBaseRadius, distanceToBaseIsImportant ? Info.MaxBaseRadius : Map.Grid.MaximumTileSearchRange);
			}

			// Can't find a build location
			return null;
		}

		void ITick.Tick(Actor self)
		{
			if (!IsEnabled)
				return;

			ticks++;

			if (ticks == 1)
				InitializeBase(self, false);

			if (ticks % FeedbackTime == 0)
				ProductionUnits(self);

			AssignRolesToIdleUnits(self);
			SetRallyPointsForNewProductionBuildings(self);
			supportPowerManager.TryToUseSupportPower(self);

			foreach (var b in builders)
				b.Tick();

			var ordersToIssueThisTick = Math.Min((orders.Count + Info.MinOrderQuotientPerTick - 1) / Info.MinOrderQuotientPerTick, orders.Count);
			for (var i = 0; i < ordersToIssueThisTick; i++)
				World.IssueOrder(orders.Dequeue());
		}

		internal Actor FindClosestEnemy(WPos pos)
		{
			return World.Actors.Where(isEnemyUnit).ClosestTo(pos);
		}

		internal Actor FindClosestEnemy(WPos pos, WDist radius)
		{
			return World.FindActorsInCircle(pos, radius).Where(isEnemyUnit).ClosestTo(pos);
		}

		List<Actor> FindEnemyConstructionYards()
		{
			return World.Actors.Where(a => Player.Stances[a.Owner] == Stance.Enemy && !a.IsDead &&
				Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name)).ToList();
		}

		void CleanSquads()
		{
			Squads.RemoveAll(s => !s.IsValid);
			foreach (var s in Squads)
				s.Units.RemoveAll(unitCannotBeOrdered);
		}

		// Use of this function requires that one squad of this type. Hence it is a piece of shit
		Squad GetSquadOfType(SquadType type)
		{
			return Squads.FirstOrDefault(s => s.Type == type);
		}

		Squad RegisterNewSquad(SquadType type, Actor target = null)
		{
			var ret = new Squad(this, type, target);
			Squads.Add(ret);
			return ret;
		}

		void AssignRolesToIdleUnits(Actor self)
		{
			CleanSquads();

			activeUnits.RemoveAll(unitCannotBeOrdered);
			unitsHangingAroundTheBase.RemoveAll(unitCannotBeOrdered);
			harvesters.RemoveAll(unitCannotBeOrdered);

			if (--rushTicks <= 0)
			{
				rushTicks = Info.RushInterval;
				TryToRushAttack();
			}

			if (--attackForceTicks <= 0)
			{
				attackForceTicks = Info.AttackForceInterval;
				foreach (var s in Squads)
					s.Update();
			}

			if (--assignRolesTicks <= 0)
			{
				assignRolesTicks = Info.AssignRolesInterval;
				FindNewUnits(self);
				harvManager.Tick(harvesters);
				InitializeBase(self, true);
			}

			if (--minAttackForceDelayTicks <= 0)
			{
				minAttackForceDelayTicks = Info.MinimumAttackForceDelay;
				CreateAttackForce();
			}

			if (--minCaptureDelayTicks <= 0)
			{
				minCaptureDelayTicks = Info.MinimumCaptureDelay;
				QueueCaptureOrders();
			}
		}

		IEnumerable<Actor> GetVisibleActorsBelongingToPlayer(Player owner)
		{
			foreach (var actor in GetActorsThatCanBeOrderedByPlayer(owner))
				if (actor.CanBeViewedByPlayer(Player))
					yield return actor;
		}

		IEnumerable<Actor> GetActorsThatCanBeOrderedByPlayer(Player owner)
		{
			foreach (var actor in World.Actors)
				if (actor.Owner == owner && !actor.IsDead && actor.IsInWorld)
					yield return actor;
		}

		void QueueCaptureOrders()
		{
			if (!Info.CapturingActorTypes.Any() || Player.WinState != WinState.Undefined)
				return;

			var capturers = unitsHangingAroundTheBase
				.Where(a => a.IsIdle && Info.CapturingActorTypes.Contains(a.Info.Name))
				.Select(a => new TraitPair<CaptureManager>(a, a.TraitOrDefault<CaptureManager>()))
				.Where(tp => tp.Trait != null)
				.ToArray();

			if (capturers.Length == 0)
				return;

			var randPlayer = World.Players.Where(p => !p.Spectating
				&& Info.CapturableStances.HasStance(Player.Stances[p])).Random(Random);

			var targetOptions = Info.CheckCaptureTargetsForVisibility
				? GetVisibleActorsBelongingToPlayer(randPlayer)
				: GetActorsThatCanBeOrderedByPlayer(randPlayer);

			var capturableTargetOptions = targetOptions
				.Select(a => new CaptureTarget<CapturableInfo>(a, "CaptureActor"))
				.Where(target =>
				{
					if (target.Info == null)
						return false;

					var captureManager = target.Actor.TraitOrDefault<CaptureManager>();
					if (captureManager == null)
						return false;

					return capturers.Any(tp => captureManager.CanBeTargetedBy(target.Actor, tp.Actor, tp.Trait));
				})
				.OrderByDescending(target => target.Actor.GetSellValue())
				.Take(maximumCaptureTargetOptions);

			if (Info.CapturableActorTypes.Any())
				capturableTargetOptions = capturableTargetOptions.Where(target => Info.CapturableActorTypes.Contains(target.Actor.Info.Name.ToLowerInvariant()));

			if (!capturableTargetOptions.Any())
				return;

			var capturesCapturers = capturers.Where(tp => tp.Actor.Info.HasTraitInfo<CapturesInfo>());
			foreach (var tp in capturesCapturers)
				QueueCaptureOrderFor(tp.Actor, GetCapturerTargetClosestToOrDefault(tp.Actor, capturableTargetOptions));
		}

		void QueueCaptureOrderFor<TTargetType>(Actor capturer, CaptureTarget<TTargetType> target) where TTargetType : class, ITraitInfoInterface
		{
			if (capturer == null)
				return;

			if (target == null)
				return;

			if (target.Actor == null)
				return;

			QueueOrder(new Order(target.OrderString, capturer, Target.FromActor(target.Actor), true));
			BotDebug("AI ({0}): Ordered {1} to capture {2}", Player.ClientIndex, capturer, target.Actor);
			activeUnits.Remove(capturer);
		}

		CaptureTarget<TTargetType> GetCapturerTargetClosestToOrDefault<TTargetType>(Actor capturer, IEnumerable<CaptureTarget<TTargetType>> targets)
			where TTargetType : class, ITraitInfoInterface
		{
			return targets.MinByOrDefault(target => (target.Actor.CenterPosition - capturer.CenterPosition).LengthSquared);
		}

		void FindNewUnits(Actor self)
		{
			var newUnits = self.World.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player && !activeUnits.Contains(a) && !harvesters.Contains(a));

			foreach (var a in newUnits)
			{
				if (a.Info.HasTraitInfo<HarvesterInfo>())
					harvesters.Add(a);

				if (Info.UnitsCommonNames.Mcv.Contains(a.Info.Name) || Info.UnitsCommonNames.ExcludeFromSquads.Contains(a.Info.Name))
					continue;

				unitsHangingAroundTheBase.Add(a);

				if (a.Info.HasTraitInfo<AircraftInfo>() && a.Info.HasTraitInfo<AttackBaseInfo>())
				{
					var air = GetSquadOfType(SquadType.Air);
					if (air == null)
						air = RegisterNewSquad(SquadType.Air);

					air.Units.Add(a);
				}
				else if (Info.UnitsCommonNames.NavalUnits.Contains(a.Info.Name))
				{
					var ships = GetSquadOfType(SquadType.Naval);
					if (ships == null)
						ships = RegisterNewSquad(SquadType.Naval);

					ships.Units.Add(a);
				}

				activeUnits.Add(a);
			}
		}

		void CreateAttackForce()
		{
			// Create an attack force when we have enough units around our base.
			// (don't bother leaving any behind for defense)
			var randomizedSquadSize = Info.SquadSize + Random.Next(Info.SquadSizeRandomBonus);

			if (unitsHangingAroundTheBase.Count >= randomizedSquadSize)
			{
				var attackForce = RegisterNewSquad(SquadType.Assault);

				foreach (var a in unitsHangingAroundTheBase)
					if (!a.Info.HasTraitInfo<AircraftInfo>())
						attackForce.Units.Add(a);

				unitsHangingAroundTheBase.Clear();
			}
		}

		void TryToRushAttack()
		{
			var allEnemyBaseBuilder = FindEnemyConstructionYards();
			var ownUnits = activeUnits
				.Where(unit => unit.IsIdle && unit.Info.HasTraitInfo<AttackBaseInfo>()
					&& !unit.Info.HasTraitInfo<AircraftInfo>() && !unit.Info.HasTraitInfo<HarvesterInfo>()).ToList();

			if (!allEnemyBaseBuilder.Any() || (ownUnits.Count < Info.SquadSize))
				return;

			foreach (var b in allEnemyBaseBuilder)
			{
				var enemies = World.FindActorsInCircle(b.CenterPosition, WDist.FromCells(Info.RushAttackScanRadius))
					.Where(unit => Player.Stances[unit.Owner] == Stance.Enemy && unit.Info.HasTraitInfo<AttackBaseInfo>()).ToList();

				if (AttackOrFleeFuzzy.Rush.CanAttack(ownUnits, enemies))
				{
					var target = enemies.Any() ? enemies.Random(Random) : b;
					var rush = GetSquadOfType(SquadType.Rush);
					if (rush == null)
						rush = RegisterNewSquad(SquadType.Rush, target);

					foreach (var a3 in ownUnits)
						rush.Units.Add(a3);

					return;
				}
			}
		}

		void ProtectOwn(Actor attacker)
		{
			var protectSq = GetSquadOfType(SquadType.Protection);
			if (protectSq == null)
				protectSq = RegisterNewSquad(SquadType.Protection, attacker);

			if (!protectSq.IsTargetValid)
				protectSq.TargetActor = attacker;

			if (!protectSq.IsValid)
			{
				var ownUnits = World.FindActorsInCircle(World.Map.CenterOfCell(GetRandomBaseCenter()), WDist.FromCells(Info.ProtectUnitScanRadius))
					.Where(unit => unit.Owner == Player && !unit.Info.HasTraitInfo<BuildingInfo>() && !unit.Info.HasTraitInfo<HarvesterInfo>()
						&& unit.Info.HasTraitInfo<AttackBaseInfo>());

				foreach (var a in ownUnits)
					protectSq.Units.Add(a);
			}
		}

		bool IsRallyPointValid(CPos x, BuildingInfo info)
		{
			return info != null && World.IsCellBuildable(x, null, info);
		}

		void SetRallyPointsForNewProductionBuildings(Actor self)
		{
			foreach (var rp in self.World.ActorsWithTrait<RallyPoint>())
			{
				if (rp.Actor.Owner == Player &&
					!IsRallyPointValid(rp.Trait.Location, rp.Actor.Info.TraitInfoOrDefault<BuildingInfo>()))
				{
					QueueOrder(new Order("SetRallyPoint", rp.Actor, Target.FromCell(World, ChooseRallyLocationNear(rp.Actor)), false)
					{
						SuppressVisualFeedback = true
					});
				}
			}
		}

		// Won't work for shipyards...
		CPos ChooseRallyLocationNear(Actor producer)
		{
			var possibleRallyPoints = Map.FindTilesInCircle(producer.Location, Info.RallyPointScanRadius)
				.Where(c => IsRallyPointValid(c, producer.Info.TraitInfoOrDefault<BuildingInfo>()));

			if (!possibleRallyPoints.Any())
			{
				BotDebug("Bot Bug: No possible rallypoint near {0}", producer.Location);
				return producer.Location;
			}

			return possibleRallyPoints.Random(Random);
		}

		void InitializeBase(Actor self, bool chooseLocation)
		{
			var mcv = FindAndDeployMcv(self, chooseLocation);

			if (mcv == null)
				return;

			initialBaseCenter = mcv.Location;
			defenseCenter = mcv.Location;
		}

		// Find any MCV and deploy them at a sensible location.
		Actor FindAndDeployMcv(Actor self, bool move)
		{
			var mcv = self.World.Actors.FirstOrDefault(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name) && a.IsIdle);

			if (mcv == null)
				return null;

			// Don't try to move and deploy an undeployable actor
			var transformsInfo = mcv.Info.TraitInfoOrDefault<TransformsInfo>();
			if (transformsInfo == null)
				return null;

			// If we lack a base, we need to make sure we don't restrict deployment of the MCV to the base!
			var restrictToBase = Info.RestrictMCVDeploymentFallbackToBase &&
				CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0;

			if (move)
			{
				var desiredLocation = ChooseBuildLocation(transformsInfo.IntoActor, restrictToBase, BuildingType.Building);
				if (desiredLocation == null)
					return null;

				QueueOrder(new Order("Move", mcv, Target.FromCell(World, desiredLocation.Value), true));
			}

			QueueOrder(new Order("DeployTransform", mcv, true));

			return mcv;
		}

		internal IEnumerable<ProductionQueue> FindQueues(string category)
		{
			return World.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == Player && a.Trait.Info.Type == category && a.Trait.Enabled)
				.Select(a => a.Trait);
		}

		void ProductionUnits(Actor self)
		{
			// Stop building until economy is restored
			if (!HasAdequateProc())
				return;

			// No construction yards - Build a new MCV
			if (Info.UnitsCommonNames.Mcv.Any() && !HasAdequateFact() && !self.World.Actors.Any(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name)))
				BuildUnit("Vehicle", GetInfoByCommonName(Info.UnitsCommonNames.Mcv, Player).Name);

			foreach (var q in Info.UnitQueues)
				BuildUnit(q, unitsHangingAroundTheBase.Count < Info.IdleBaseUnitsMaximum);
		}

		void BuildUnit(string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = FindQueues(category).FirstOrDefault(q => !q.AllQueued().Any());
			if (queue == null)
				return;

			var unit = buildRandom ?
				ChooseRandomUnitToBuild(queue) :
				ChooseUnitToBuild(queue);

			if (unit == null)
				return;

			var name = unit.Name;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return;

			if (Info.UnitLimits != null &&
				Info.UnitLimits.ContainsKey(name) &&
				World.Actors.Count(a => a.Owner == Player && a.Info.Name == name) >= Info.UnitLimits[name])
				return;

			QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		void BuildUnit(string category, string name)
		{
			var queue = FindQueues(category).FirstOrDefault(q => !q.AllQueued().Any());
			if (queue == null)
				return;

			if (Map.Rules.Actors[name] != null)
				QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (!IsEnabled || e.Attacker == null)
				return;

			if (e.Attacker.Owner.Stances[self.Owner] == Stance.Neutral)
				return;

			var rb = self.TraitOrDefault<RepairableBuilding>();

			if (Info.ShouldRepairBuildings && rb != null)
			{
				if (e.DamageState > DamageState.Light && e.PreviousDamageState <= DamageState.Light && !rb.RepairActive)
				{
					BotDebug("Bot noticed damage {0} {1}->{2}, repairing.",
						self, e.PreviousDamageState, e.DamageState);
					QueueOrder(new Order("RepairBuilding", self.Owner.PlayerActor, Target.FromActor(self), false));
				}
			}

			if (e.Attacker.Disposed)
				return;

			if (!e.Attacker.Info.HasTraitInfo<ITargetableInfo>())
				return;

			// Protected priority assets, MCVs, harvesters and buildings
			if ((self.Info.HasTraitInfo<HarvesterInfo>() || self.Info.HasTraitInfo<BuildingInfo>() || self.Info.HasTraitInfo<BaseBuildingInfo>()) &&
				Player.Stances[e.Attacker.Owner] == Stance.Enemy)
			{
				defenseCenter = e.Attacker.Location;
				ProtectOwn(e.Attacker);
			}
		}
	}
}
