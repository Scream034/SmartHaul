using System.Linq;

namespace SmartHaul;

/// <summary>
/// WorkGiver that builds multi-item haul jobs.
/// </summary>
public class WorkGiver_HaulToInventory : WorkGiver_HaulGeneral
{
    private static readonly List<Thing> _allHaulables = new(64);
    private static readonly List<Thing> _candidates = new(32);
    private static readonly List<Thing> _route = new(32);

    private static HashSet<IntVec3>? _skipCells;
    private static HashSet<Thing>? _skipThings;

    private static readonly Dictionary<int, (Job job, int tick, int thingId)> _jobCache = new();
    private const int CACHE_TICKS = 30;

    #region StoreTarget

    public readonly struct StoreTarget : IEquatable<StoreTarget>
    {
        public readonly IntVec3 Cell;
        public readonly Thing? Container;

        public StoreTarget(IntVec3 c) { Cell = c; Container = null; }
        public StoreTarget(Thing t) { Cell = default; Container = t; }

        public bool Equals(StoreTarget o) =>
            Container == null ? o.Container == null && Cell == o.Cell : Container == o.Container;

        public override bool Equals(object? o) => o is StoreTarget t && Equals(t);
        public override int GetHashCode() => Container?.GetHashCode() ?? Cell.GetHashCode();

        public static implicit operator LocalTargetInfo(StoreTarget t) =>
            t.Container != null ? new LocalTargetInfo(t.Container) : new LocalTargetInfo(t.Cell);
    }

    #endregion

    #region WorkGiver Interface

    public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
        base.ShouldSkip(pawn, forced)
        || pawn.Faction != Faction.OfPlayerSilentFail
        || !Settings.IsAllowedRace(pawn.RaceProps)
        || pawn.GetComp<CompHauledToInventory>() == null
        || pawn.IsQuestLodger()
        || GetGearRatio(pawn) >= Settings.MaximumOccupiedCapacityToConsiderHauling;

    public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
        CanHaul(pawn, thing, forced);

    public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        if (!CanHaul(pawn, thing, forced))
            return Fallback(pawn, thing, forced);

        if (!TryGetStore(pawn, thing, out var store))
            return Fallback(pawn, thing, forced);

        var pawnId = pawn.thingIDNumber;
        var tick = Find.TickManager.TicksGame;

        if (_jobCache.TryGetValue(pawnId, out var cached)
            && cached.thingId == thing.thingIDNumber
            && tick - cached.tick < CACHE_TICKS
            && cached.job != null)
        {
            return cached.job;
        }

        var job = BuildJob(pawn, thing, store, forced);
        _jobCache[pawnId] = (job, tick, thing.thingIDNumber);

        if (_jobCache.Count > 50)
        {
            var stale = _jobCache.Where(kv => tick - kv.Value.tick > CACHE_TICKS * 3).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _jobCache.Remove(k);
        }

        return job;
    }

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        var things = pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        var sorted = new List<Thing>(things);
        sorted.Sort((a, b) => GetPriorityScore(a).CompareTo(GetPriorityScore(b)));
        return sorted;
    }

    #endregion

    #region Priority

    private static int GetPriorityScore(Thing t)
    {
        var rot = t.TryGetComp<CompRottable>();
        if (rot != null)
        {
            var ticks = rot.TicksUntilRotAtCurrentTemp;
            if (ticks < Settings.RotUrgentTicks)
                return Math.Clamp(ticks * 1000 / Settings.RotUrgentTicks, 0, 999);
        }

        if (t.def.useHitPoints && t.MaxHitPoints > 0)
        {
            var ratio = (float)t.HitPoints / t.MaxHitPoints;
            if (ratio < Settings.WornThreshold)
                return 1000 + (int)(ratio * 1000);
        }

        if (t.TryGetQuality(out var qc))
            return 2000 + ((int)QualityCategory.Legendary - (int)qc);

        return 3000 + (t.thingIDNumber % 1000);
    }

    #endregion

    #region Validation

    private static bool CanHaul(Pawn pawn, Thing thing, bool forced)
    {
        if (pawn.Faction != Faction.OfPlayerSilentFail) return false;
        if (!Settings.IsAllowedRace(pawn.RaceProps)) return false;
        if (pawn.GetComp<CompHauledToInventory>() == null) return false;
        if (pawn.IsQuestLodger()) return false;
        if (GetGearRatio(pawn) >= Settings.MaximumOccupiedCapacityToConsiderHauling) return false;
        if (!thing.Spawned || thing.IsForbidden(pawn)) return false;
        if (!pawn.CanReserve(thing, 1, -1, null, forced)) return false;
        if (!Settings.AllowCorpses && thing is Corpse) return false;
        if (thing.IsInValidBestStorage()) return false;
        if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1)) return false;
        if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced)) return false;
        if (ThingProtection.IsProtected(thing)) return false;
        if (!StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map,
            StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out _, out _, false)) return false;
        return true;
    }

    private static float GetGearRatio(Pawn p) =>
        MassUtility.Capacity(p) > 0f ? MassUtility.GearMass(p) / MassUtility.Capacity(p) : 1f;

    private static Job Fallback(Pawn pawn, Thing thing, bool forced) =>
        HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

    private static bool TryGetStore(Pawn pawn, Thing thing, out StoreTarget store)
    {
        store = default;
        if (!StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map,
            StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out var cell, out var dest, true))
            return false;

        if (dest is ISlotGroupParent)
        {
            if (IsHopper(thing, cell, pawn.Map)) return false;
            store = new StoreTarget(cell);
        }
        else if (dest is Thing t)
        {
            var owner = t.TryGetInnerInteractableThingOwner();
            if (owner == null || owner.GetCountCanAccept(thing) <= 0) return false;
            store = new StoreTarget(t);
        }
        else return false;

        return true;
    }

    private static bool IsHopper(Thing thing, IntVec3 cell, Map map)
    {
        if (!thing.def.IsNutritionGivingIngestible) return false;
        if (thing.def.ingestible.preferability is not (FoodPreferability.RawBad or FoodPreferability.RawTasty))
            return false;
        return cell.GetThingList(map).Any(t => t.def == ThingDefOf.Hopper);
    }

    #endregion

    #region Job Building

    private static Job BuildJob(Pawn pawn, Thing first, StoreTarget store, bool forced)
    {
        var map = pawn.Map;

        CollectHaulables(pawn, first, map);

        if (forced)
            BuildDirectionalCluster(pawn.Position, first);
        else
            BuildCluster(first);

        BuildRoute(pawn.Position, first);
        TrimToCapacity(pawn, first);

        if (_route.Count == 0)
            return Fallback(pawn, first, forced);

        var job = JobMaker.MakeJob(SmartHaulJobDefOf.HaulToInventory, null, (LocalTargetInfo)store);
        job.targetQueueA = new List<LocalTargetInfo>(_route.Count);
        job.targetQueueB = new List<LocalTargetInfo>(4);
        job.countQueue = new List<int>(_route.Count);

        _skipCells = new HashSet<IntVec3>();
        _skipThings = new HashSet<Thing>();

        if (store.Container != null) _skipThings.Add(store.Container);
        else _skipCells.Add(store.Cell);

        var capacity = new Dictionary<StoreTarget, (Thing t, int cap)>(4)
        {
            [store] = (_route[0], GetCap(_route[0], store.Cell, map))
        };

        foreach (var thing in _route)
            Allocate(capacity, pawn, thing, job);

        _skipCells = null;
        _skipThings = null;

        if (job.targetQueueA.Count > 0 && !job.targetQueueA.Any(t => t.Thing == first))
        {
            job.targetQueueA.Insert(0, first);
            job.countQueue.Insert(0, first.stackCount);
        }

        return job.targetQueueA.Count > 0 ? job : Fallback(pawn, first, forced);
    }

    private static void CollectHaulables(Pawn pawn, Thing first, Map map)
    {
        _allHaulables.Clear();

        if (first.Spawned && !first.IsForbidden(pawn))
            _allHaulables.Add(first);

        var dm = map.designationManager;
        var urgentDef = SmartHaulDesignationDefOf.HaulUrgently;
        var isUrgent = ModCompatibility.AllowToolIsActive && urgentDef != null
            && dm.DesignationOn(first)?.def == urgentDef;

        foreach (var t in map.listerHaulables.ThingsPotentiallyNeedingHauling())
        {
            if (t == first) continue;
            if (!t.Spawned || t.IsForbidden(pawn) || !pawn.CanReserve(t)) continue;
            if (!Settings.AllowCorpses && t is Corpse) continue;
            if (t.IsInValidBestStorage()) continue;
            if (isUrgent && dm.DesignationOn(t)?.def != urgentDef) continue;
            if (ThingProtection.IsProtected(t)) continue;
            _allHaulables.Add(t);
        }
    }

    private static void BuildCluster(Thing first)
    {
        _candidates.Clear();
        _candidates.Add(first);
        _allHaulables.Remove(first);

        if (_allHaulables.Count == 0) return;

        var current = first.Position;
        var maxDistSq = Settings.MaxNeighborDistance * Settings.MaxNeighborDistance;
        var maxItems = Settings.MaxCandidates;

        while (_candidates.Count < maxItems && _allHaulables.Count > 0)
        {
            var bestIdx = -1;
            var bestDist = int.MaxValue;

            for (var i = 0; i < _allHaulables.Count; i++)
            {
                var dist = (_allHaulables[i].Position - current).LengthHorizontalSquared;
                if (dist < bestDist && dist <= maxDistSq)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) break;

            var best = _allHaulables[bestIdx];
            _candidates.Add(best);
            current = best.Position;

            _allHaulables[bestIdx] = _allHaulables[^1];
            _allHaulables.RemoveAt(_allHaulables.Count - 1);
        }

        _allHaulables.Clear();
    }

    private static void BuildDirectionalCluster(IntVec3 pawnPos, Thing first)
    {
        _candidates.Clear();
        _candidates.Add(first);
        _allHaulables.Remove(first);

        if (_allHaulables.Count == 0) return;

        var dirX = first.Position.x - pawnPos.x;
        var dirZ = first.Position.z - pawnPos.z;
        var current = first.Position;
        var maxDistSq = Settings.MaxNeighborDistance * Settings.MaxNeighborDistance;
        var maxItems = Settings.MaxCandidates;

        while (_candidates.Count < maxItems && _allHaulables.Count > 0)
        {
            var bestIdx = -1;
            var bestScore = float.MaxValue;

            for (var i = 0; i < _allHaulables.Count; i++)
            {
                var pos = _allHaulables[i].Position;
                var dist = (pos - current).LengthHorizontalSquared;
                if (dist > maxDistSq) continue;

                var toItemX = pos.x - current.x;
                var toItemZ = pos.z - current.z;
                var dotProduct = dirX * toItemX + dirZ * toItemZ;
                var score = dist + (dotProduct > 0 ? 0 : 100);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0) break;

            var best = _allHaulables[bestIdx];
            _candidates.Add(best);
            current = best.Position;

            _allHaulables[bestIdx] = _allHaulables[^1];
            _allHaulables.RemoveAt(_allHaulables.Count - 1);
        }

        _allHaulables.Clear();
    }

    private static void BuildRoute(IntVec3 pawnPos, Thing first)
    {
        _route.Clear();
        if (_candidates.Count == 0) return;

        var firstIdx = _candidates.IndexOf(first);
        if (firstIdx < 0 && first.Spawned)
        {
            _candidates.Insert(0, first);
            firstIdx = 0;
        }
        else if (firstIdx > 0)
        {
            (_candidates[0], _candidates[firstIdx]) = (_candidates[firstIdx], _candidates[0]);
        }

        _route.Add(_candidates[0]);
        var current = _candidates[0].Position;
        _candidates.RemoveAt(0);

        while (_candidates.Count > 0)
        {
            var bestIdx = 0;
            var bestDist = int.MaxValue;

            for (var i = 0; i < _candidates.Count; i++)
            {
                var dist = (_candidates[i].Position - current).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            _route.Add(_candidates[bestIdx]);
            current = _candidates[bestIdx].Position;

            _candidates[bestIdx] = _candidates[^1];
            _candidates.RemoveAt(_candidates.Count - 1);
        }
    }

    private static void TrimToCapacity(Pawn pawn, Thing first)
    {
        var maxMass = MassUtility.Capacity(pawn) - MassUtility.GearMass(pawn) - MassUtility.InventoryMass(pawn);
        var mass = 0f;
        var keep = 0;

        for (var i = 0; i < _route.Count; i++)
        {
            var m = _route[i].stackCount * _route[i].GetStatValue(StatDefOf.Mass);

            if (mass + m > maxMass && i > 0 && _route[i] != first) break;

            mass += m;
            keep = i + 1;
        }

        if (keep < _route.Count)
            _route.RemoveRange(keep, _route.Count - keep);

        if (!_route.Contains(first) && first.Spawned)
            _route.Insert(0, first);
    }

    #endregion

    #region Allocation

    private static void Allocate(Dictionary<StoreTarget, (Thing t, int cap)> capacity, Pawn pawn, Thing thing, Job job)
    {
        var map = pawn.Map;
        var store = default(StoreTarget);
        var found = false;

        foreach (var (target, alloc) in capacity)
        {
            var accepts = target.Container != null
                ? target.Container.TryGetInnerInteractableThingOwner()?.CanAcceptAnyOf(thing) ?? false
                : target.Cell.GetSlotGroup(map)?.parent.Accepts(thing) ?? false;

            if (accepts && CanStack(thing, alloc.t))
            {
                store = target;
                found = true;
                break;
            }
        }

        if (!found)
        {
            if (!FindStorage(thing, pawn, map, out var cell, out var dest, out var owner))
                return;

            if (owner == null)
            {
                store = new StoreTarget(cell);
                job.targetQueueB.Add(cell);
                capacity[store] = (thing, GetCap(thing, cell, map));
            }
            else
            {
                store = new StoreTarget((Thing)dest!);
                job.targetQueueB.Add((Thing)dest!);
                capacity[store] = (thing, owner.GetCountCanAccept(thing));
            }
        }

        if (!capacity.TryGetValue(store, out var a)) return;

        var count = Math.Min(thing.stackCount, a.cap);
        if (count <= 0) return;

        job.targetQueueA.Add(thing);
        job.countQueue.Add(count);
        capacity[store] = (a.t, a.cap - count);

        if (capacity[store].cap <= 0)
            capacity.Remove(store);
    }

    private static bool CanStack(Thing a, Thing b) =>
        a == b || a.CanStackWith(b) || MultiThingsHolderSupport.StackableAt(a, b.Position, a.Map);

    private static bool FindStorage(Thing t, Pawn p, Map map, out IntVec3 cell, out IHaulDestination? dest, out ThingOwner? owner)
    {
        cell = IntVec3.Invalid;
        dest = null;
        owner = null;

        var prio = StoreUtility.CurrentStoragePriorityOf(t);

        foreach (var g in map.haulDestinationManager.AllGroupsListInPriorityOrder)
        {
            if (g.Settings.Priority <= prio || !g.parent.Accepts(t)) continue;
            foreach (var c in g.CellsList)
            {
                if (_skipCells?.Contains(c) == true) continue;
                if (StoreUtility.IsGoodStoreCell(c, map, t, p, p.Faction))
                {
                    cell = c;
                    dest = g.parent;
                    _skipCells?.Add(c);
                    return true;
                }
            }
        }

        foreach (var d in map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder)
        {
            if (d is ISlotGroupParent || d.GetStoreSettings().Priority <= prio || !d.Accepts(t)) continue;
            if (d is not Thing thing || _skipThings?.Contains(thing) == true) continue;
            if (thing.IsForbidden(p) || !p.CanReserveNew(thing)) continue;
            dest = d;
            owner = thing.TryGetInnerInteractableThingOwner();
            _skipThings?.Add(thing);
            return owner != null;
        }

        return false;
    }

    public static int GetCap(Thing t, IntVec3 c, Map m) =>
        MultiThingsHolderSupport.CapacityAt(t, c, m, out var cap) ? cap : c.GetItemStackSpaceLeftFor(m, t.def);

    #endregion
}

public static class SmartHaulDesignationDefOf
{
    public static readonly DesignationDef? HaulUrgently =
        DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
}