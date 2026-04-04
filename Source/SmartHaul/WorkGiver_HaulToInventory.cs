using System.Linq;

namespace SmartHaul;

public sealed class WorkGiver_HaulToInventory : WorkGiver_HaulGeneral
{
    /// <summary>
    /// Maximum distance from current item to next candidate in Phase 1.
    /// Prevents collecting items across the entire map.
    /// </summary>
    private const float MAX_NEIGHBOR_DISTANCE = 30f;

    /// <summary>
    /// Maximum candidates to collect in Phase 1.
    /// Prevents excessive route computation.
    /// </summary>
    private const int MAX_CANDIDATES = 24;

    /// <summary>
    /// Minimum search radius in cells.
    /// </summary>
    private const float MIN_SEARCH_RANGE = 12f;

    /// <summary>
    /// Reusable buffer for all haulables in range. Cleared before each use.
    /// </summary>
    private static readonly List<Thing> _allHaulables = new(64);

    /// <summary>
    /// Reusable buffer for Phase 1 candidates. Cleared before each use.
    /// </summary>
    private static readonly List<Thing> _candidates = new(32);

    /// <summary>
    /// Reusable route buffer. Filled by Phase 2.
    /// </summary>
    private static readonly List<Thing> _route = new(32);

    /// <summary>
    /// Identifies a storage destination: either a cell in a slot group or a container Thing.
    /// </summary>
    public struct StoreTarget : IEquatable<StoreTarget>
    {
        public IntVec3 cell;
        public Thing container;

        public readonly IntVec3 Position => container?.Position ?? cell;

        public StoreTarget(IntVec3 c)
        {
            cell = c;
            container = null;
        }

        public StoreTarget(Thing t)
        {
            cell = default;
            container = t;
        }

        public readonly bool Equals(StoreTarget other)
        {
            return container == null
                ? other.container == null && cell == other.cell
                : container == other.container;
        }

        public override readonly bool Equals(object obj) => obj is StoreTarget t && Equals(t);
        public override readonly int GetHashCode() => container?.GetHashCode() ?? cell.GetHashCode();
        public override readonly string ToString() => container?.ToString() ?? cell.ToString();

        public static bool operator ==(StoreTarget a, StoreTarget b) => a.Equals(b);
        public static bool operator !=(StoreTarget a, StoreTarget b) => !a.Equals(b);

        public static implicit operator LocalTargetInfo(StoreTarget t) =>
            t.container != null ? t.container : t.cell;
    }

    /// <summary>
    /// Tracks allocated capacity per storage cell during job building.
    /// </summary>
    public sealed class CellAlloc
    {
        public Thing thing;
        public int cap;

        public CellAlloc(Thing t, int c)
        {
            thing = t;
            cap = c;
        }
    }

    private static HashSet<IntVec3> _skipCells;
    private static HashSet<Thing> _skipThings;

    #region Validation

    private static bool ShouldUseSmartHaul(Pawn pawn, Thing thing, bool forced)
    {
        if (pawn.Faction != Faction.OfPlayerSilentFail)
            return false;

        if (!Settings.IsAllowedRace(pawn.RaceProps))
            return false;

        if (pawn.GetComp<CompHauledToInventory>() == null)
            return false;

        if (pawn.IsQuestLodger())
            return false;

        if (GearMassRatio(pawn) >= Settings.MaximumOccupiedCapacityToConsiderHauling)
            return false;

        if (!thing.Spawned)
            return false;

        if (thing.IsForbidden(pawn))
            return false;

        if (!pawn.CanReserve(thing, 1, -1, null, forced))
            return false;

        if (!Settings.AllowCorpses && thing is Corpse)
            return false;

        if (thing.IsInValidBestStorage())
            return false;

        if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1))
            return false;

        if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced))
            return false;

        if (!StoreUtility.TryFindBestBetterStorageFor(
            thing, pawn, pawn.Map,
            StoreUtility.CurrentStoragePriorityOf(thing),
            pawn.Faction, out _, out _, false))
            return false;

        return true;
    }

    private static float GearMassRatio(Pawn p)
    {
        var cap = MassUtility.Capacity(p);
        return cap > 0f ? MassUtility.GearMass(p) / cap : 1f;
    }

    #endregion

    #region WorkGiver overrides

    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        if (base.ShouldSkip(pawn, forced))
            return true;

        if (pawn.Faction != Faction.OfPlayerSilentFail)
            return true;

        if (!Settings.IsAllowedRace(pawn.RaceProps))
            return true;

        if (pawn.GetComp<CompHauledToInventory>() == null)
            return true;

        if (pawn.IsQuestLodger())
            return true;

        if (GearMassRatio(pawn) >= Settings.MaximumOccupiedCapacityToConsiderHauling)
            return true;

        return false;
    }

    public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        return ShouldUseSmartHaul(pawn, thing, forced);
    }

    public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        if (!ShouldUseSmartHaul(pawn, thing, forced))
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        var map = pawn.Map;
        var priority = StoreUtility.CurrentStoragePriorityOf(thing);

        if (!StoreUtility.TryFindBestBetterStorageFor(
            thing, pawn, map, priority, pawn.Faction,
            out var targetCell, out var haulDest, true))
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        StoreTarget store;
        ThingOwner destOwner = null;

        if (haulDest is ISlotGroupParent)
        {
            if (IsHopperCell(thing, targetCell, map))
                return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

            store = new StoreTarget(targetCell);
        }
        else if (haulDest is Thing destThing)
        {
            destOwner = destThing.TryGetInnerInteractableThingOwner();
            if (destOwner == null)
                return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

            store = new StoreTarget(destThing);
        }
        else
        {
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);
        }

        var initialCap = store.container == null
            ? CapacityAt(thing, store.cell, map)
            : destOwner.GetCountCanAccept(thing);

        if (initialCap <= 0)
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        return BuildHaulJob(pawn, thing, store, map, forced);
    }

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        return pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
    }

    #endregion

    #region Job building

    /// <summary>
    /// Builds haul job using two-phase algorithm:
    /// Phase 1: From 'first', collect nearby candidates via nearest-neighbor (cluster detection)
    /// Phase 2: From pawn position, build optimal route through candidates via nearest-neighbor
    /// </summary>
    private static Job BuildHaulJob(Pawn pawn, Thing first, StoreTarget store, Map map, bool forced)
    {
        // === Collect all valid haulables ===
        CollectAllHaulables(pawn, first, map);

        // === Phase 1: Build candidate cluster from 'first' using nearest-neighbor ===
        BuildCandidateCluster(first);

        // === Phase 2: Build pawn route through candidates using nearest-neighbor ===
        BuildPawnRoute(pawn.Position, first);

        // Trim to carry capacity
        var carryMass = MassUtility.Capacity(pawn)
            - MassUtility.GearMass(pawn)
            - MassUtility.InventoryMass(pawn);

        TrimToCarryCapacity(carryMass);

        if (_route.Count == 0)
            return HaulAIUtility.HaulToStorageJob(pawn, first, forced);

        // Create job
        var job = JobMaker.MakeJob(SmartHaulJobDefOf.HaulToInventory, null, store);
        job.targetQueueA = new List<LocalTargetInfo>(_route.Count);
        job.targetQueueB = new List<LocalTargetInfo>(4);
        job.countQueue = new List<int>(_route.Count);

        _skipCells = new HashSet<IntVec3>();
        _skipThings = new HashSet<Thing>();

        if (store.container != null)
            _skipThings.Add(store.container);
        else
            _skipCells.Add(store.cell);

        var routeFirst = _route[0];
        var capacity = new Dictionary<StoreTarget, CellAlloc>(4)
        {
            [store] = new CellAlloc(routeFirst, CapacityAt(routeFirst, store.cell, map))
        };

        for (var i = 0; i < _route.Count; i++)
        {
            AllocateThing(capacity, pawn, _route[i], job);
        }

        _skipCells = null;
        _skipThings = null;

        if (job.targetQueueA.Count == 0)
            return HaulAIUtility.HaulToStorageJob(pawn, first, forced);

        return job;
    }

    /// <summary>
    /// Collects all haulables that pawn can potentially pick up.
    /// No distance filter — Phase 1 will select relevant ones.
    /// </summary>
    private static void CollectAllHaulables(Pawn pawn, Thing first, Map map)
    {
        _allHaulables.Clear();

        var dm = map.designationManager;
        var urgentDef = SmartHaulDesignationDefOf.haulUrgently;
        var isUrgent = ModCompatibilityCheck.AllowToolIsActive
            && dm.DesignationOn(first)?.def == urgentDef;

        foreach (var t in map.listerHaulables.ThingsPotentiallyNeedingHauling())
        {
            if (!t.Spawned)
                continue;

            if (t.IsForbidden(pawn))
                continue;

            if (!pawn.CanReserve(t))
                continue;

            if (!Settings.AllowCorpses && t is Corpse)
                continue;

            if (t.IsInValidBestStorage())
                continue;

            if (isUrgent && dm.DesignationOn(t)?.def != urgentDef)
                continue;

            _allHaulables.Add(t);
        }
    }

    /// <summary>
    /// Phase 1: Starting from 'first', collect candidates using nearest-neighbor.
    /// Each step picks the closest item to current position.
    /// Stops when: max candidates reached, or no items within MAX_NEIGHBOR_DISTANCE.
    /// Result: _candidates contains a "cluster" of nearby items.
    /// </summary>
    private static void BuildCandidateCluster(Thing first)
    {
        _candidates.Clear();
        _candidates.Add(first);

        // Remove first from pool
        _allHaulables.Remove(first);

        if (_allHaulables.Count == 0)
            return;

        var current = first.Position;
        var maxDistSq = MAX_NEIGHBOR_DISTANCE * MAX_NEIGHBOR_DISTANCE;

        while (_candidates.Count < MAX_CANDIDATES && _allHaulables.Count > 0)
        {
            var bestIdx = -1;
            var bestDistSq = int.MaxValue;
            var bestId = int.MaxValue;

            // Find nearest to current position
            for (var i = 0; i < _allHaulables.Count; i++)
            {
                var t = _allHaulables[i];
                var distSq = (t.Position - current).LengthHorizontalSquared;

                // Must be within max distance
                if (distSq > maxDistSq)
                    continue;

                // Deterministic tiebreaker
                if (distSq < bestDistSq || (distSq == bestDistSq && t.thingIDNumber < bestId))
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                    bestId = t.thingIDNumber;
                }
            }

            // No more items within range — cluster complete
            if (bestIdx < 0)
                break;

            var best = _allHaulables[bestIdx];
            _candidates.Add(best);
            current = best.Position;

            // O(1) swap-remove
            _allHaulables[bestIdx] = _allHaulables[_allHaulables.Count - 1];
            _allHaulables.RemoveAt(_allHaulables.Count - 1);
        }

        // Clear pool — no longer needed
        _allHaulables.Clear();
    }

    /// <summary>
    /// Phase 2: Build optimal route from pawn through candidates.
    /// Uses nearest-neighbor starting from pawn position.
    /// 'first' is guaranteed to be in _candidates and will be visited.
    /// Result: _route contains ordered pickup sequence.
    /// </summary>
    private static void BuildPawnRoute(IntVec3 pawnPos, Thing first)
    {
        _route.Clear();

        if (_candidates.Count == 0)
            return;

        // WHY: We already have candidates from Phase 1.
        // Now order them by nearest-neighbor FROM PAWN.
        var current = pawnPos;

        while (_candidates.Count > 0)
        {
            var bestIdx = 0;
            var bestDistSq = int.MaxValue;
            var bestId = int.MaxValue;

            for (var i = 0; i < _candidates.Count; i++)
            {
                var t = _candidates[i];
                var distSq = (t.Position - current).LengthHorizontalSquared;

                if (distSq < bestDistSq || (distSq == bestDistSq && t.thingIDNumber < bestId))
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                    bestId = t.thingIDNumber;
                }
            }

            var best = _candidates[bestIdx];
            _route.Add(best);
            current = best.Position;

            // O(1) swap-remove
            _candidates[bestIdx] = _candidates[_candidates.Count - 1];
            _candidates.RemoveAt(_candidates.Count - 1);
        }
    }

    /// <summary>
    /// Removes items from end of _route that exceed maxMass.
    /// </summary>
    private static void TrimToCarryCapacity(float maxMass)
    {
        var mass = 0f;
        var keepCount = 0;

        for (var i = 0; i < _route.Count; i++)
        {
            var t = _route[i];
            var stackMass = t.stackCount * t.GetStatValue(StatDefOf.Mass);

            if (mass + stackMass > maxMass && i > 0)
            {
                var massPerUnit = t.GetStatValue(StatDefOf.Mass);
                if (massPerUnit > 0.001f)
                {
                    var canTake = (int)((maxMass - mass) / massPerUnit);
                    if (canTake > 0)
                        keepCount = i + 1;
                }
                break;
            }

            mass += stackMass;
            keepCount = i + 1;
        }

        if (keepCount < _route.Count)
            _route.RemoveRange(keepCount, _route.Count - keepCount);
    }

    #endregion

    #region Storage allocation

    private static void AllocateThing(
        Dictionary<StoreTarget, CellAlloc> capacity,
        Pawn pawn,
        Thing thing,
        Job job)
    {
        var map = pawn.Map;
        var store = default(StoreTarget);
        var found = false;

        foreach (var kv in capacity)
        {
            var target = kv.Key;
            var alloc = kv.Value;

            var accepts = target.container != null
                ? target.container.TryGetInnerInteractableThingOwner()?.CanAcceptAnyOf(thing) ?? false
                : target.cell.GetSlotGroup(map)?.parent.Accepts(thing) ?? false;

            if (accepts && CanStack(thing, alloc.thing))
            {
                store = target;
                found = true;
                break;
            }
        }

        if (!found)
        {
            var priority = StoreUtility.CurrentStoragePriorityOf(thing);

            if (!TryFindStorage(thing, pawn, map, priority, out var cell, out var dest, out var owner))
                return;

            if (owner == null)
            {
                store = new StoreTarget(cell);
                job.targetQueueB.Add(cell);
                capacity[store] = new CellAlloc(thing, CapacityAt(thing, cell, map));
            }
            else
            {
                store = new StoreTarget((Thing)dest);
                job.targetQueueB.Add((Thing)dest);
                capacity[store] = new CellAlloc(thing, owner.GetCountCanAccept(thing));
            }
        }

        if (!capacity.TryGetValue(store, out var cellAlloc))
            return;

        var count = Math.Min(thing.stackCount, cellAlloc.cap);
        if (count <= 0)
            return;

        job.targetQueueA.Add(thing);
        job.countQueue.Add(count);
        cellAlloc.cap -= count;

        if (cellAlloc.cap <= 0)
            capacity.Remove(store);
    }

    private static bool CanStack(Thing a, Thing b)
    {
        return a == b
            || a.CanStackWith(b)
            || HoldMultipleThings_Support.StackableAt(a, b.Position, a.Map);
    }

    private static bool TryFindStorage(
        Thing t, Pawn carrier, Map map, StoragePriority currentPriority,
        out IntVec3 cell, out IHaulDestination dest, out ThingOwner owner)
    {
        cell = IntVec3.Invalid;
        dest = null;
        owner = null;

        var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];

            if (group.Settings.Priority <= currentPriority)
                continue;

            if (!group.parent.Accepts(t))
                continue;

            foreach (var c in group.CellsList)
            {
                if (_skipCells != null && _skipCells.Contains(c))
                    continue;

                if (StoreUtility.IsGoodStoreCell(c, map, t, carrier, carrier.Faction))
                {
                    cell = c;
                    dest = group.parent;
                    _skipCells?.Add(c);
                    return true;
                }
            }
        }

        var nonSlot = map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder;

        for (var i = 0; i < nonSlot.Count; i++)
        {
            var d = nonSlot[i];

            if (d is ISlotGroupParent)
                continue;

            if (d.GetStoreSettings().Priority <= currentPriority)
                continue;

            if (!d.Accepts(t))
                continue;

            if (d is not Thing thing)
                continue;

            if (_skipThings != null && _skipThings.Contains(thing))
                continue;

            if (thing.IsForbidden(carrier) || !carrier.CanReserveNew(thing))
                continue;

            dest = d;
            owner = thing.TryGetInnerInteractableThingOwner();
            _skipThings?.Add(thing);
            return owner != null;
        }

        return false;
    }

    #endregion

    #region Helpers

    private static bool IsHopperCell(Thing thing, IntVec3 cell, Map map)
    {
        if (!thing.def.IsNutritionGivingIngestible)
            return false;

        if (thing.def.ingestible.preferability is not (FoodPreferability.RawBad or FoodPreferability.RawTasty))
            return false;

        foreach (var t in cell.GetThingList(map))
        {
            if (t.def == ThingDefOf.Hopper)
                return true;
        }

        return false;
    }

    public static int CapacityAt(Thing thing, IntVec3 cell, Map map)
    {
        if (HoldMultipleThings_Support.CapacityAt(thing, cell, map, out var cap))
            return cap;

        return cell.GetItemStackSpaceLeftFor(map, thing.def);
    }

    #endregion
}

public static class SmartHaulDesignationDefOf
{
    public static readonly DesignationDef haulUrgently =
        DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
}