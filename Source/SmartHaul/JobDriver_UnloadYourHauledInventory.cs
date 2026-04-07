using System.Linq;

namespace SmartHaul;

/// <summary>
/// Unloads hauled items from inventory to appropriate storage.
/// Only unloads items tracked by CompHauledToInventory.
/// </summary>
public sealed class JobDriver_UnloadYourHauledInventory : JobDriver
{
    private int _countToDrop = -1;
    private int _unloadDuration = 3;

    // WHY: Cache last known GOOD storage position per ThingDef
    private static readonly Dictionary<ThingDef, IntVec3> _lastKnownStoragePos = new(32);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _countToDrop, "countToDrop", -1);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    public override IEnumerable<Toil> MakeNewToils()
    {
        if (ModCompatibility.ExtendedStorageIsActive)
            _unloadDuration = 20;

        var comp = pawn.TryGetComp<CompHauledToInventory>();
        if (comp == null) yield break;

        var begin = Toils_General.Wait(_unloadDuration);
        yield return begin;

        yield return FindTargetOrDrop(comp, begin);
        yield return PullItemFromInventory(comp, begin);

        var releaseReservation = ReleaseReservation();
        var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);

        yield return Toils_Jump.JumpIf(carryToCell, () => !TargetB.HasThing);

        var carryToContainer = Toils_Haul.CarryHauledThingToContainer();
        yield return carryToContainer;
        yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
        yield return Toils_Haul.JumpToCarryToNextContainerIfPossible(carryToContainer, TargetIndex.B);
        yield return Toils_Jump.Jump(releaseReservation);

        yield return carryToCell;
        yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

        yield return releaseReservation;
        yield return Toils_Jump.Jump(begin);
    }

    private Toil ReleaseReservation()
    {
        return new Toil
        {
            initAction = () =>
            {
                if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob))
                    pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
            }
        };
    }

    private Toil FindTargetOrDrop(CompHauledToInventory comp, Toil loopBack)
    {
        return new Toil
        {
            initAction = () =>
            {
                var inventory = pawn.inventory?.innerContainer;
                if (inventory == null)
                {
                    comp.ClearTracking();
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                SyncTrackedItems(comp, inventory);

                var trackedItems = comp.GetHashSet();
                Log.Info($"[Unload] FindTargetOrDrop: {trackedItems.Count} tracked items");

                Thing? unloadable = null;
                foreach (var thing in trackedItems)
                {
                    if (thing != null && !thing.Destroyed && inventory.Contains(thing))
                    {
                        unloadable = thing;
                        break;
                    }
                }

                if (unloadable == null)
                {
                    Log.Info("[Unload] No unloadable items found, ending job");
                    comp.ClearTracking();
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Log.Info($"[Unload] Trying to find storage for {unloadable.LabelShort} x{unloadable.stackCount}");

                // Try to find ideal storage
                if (StoreUtility.TryFindBestBetterStorageFor(
                        unloadable, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, out var cell, out var destination))
                {
                    Log.Info($"[Unload] Found storage at {cell} / {destination}");

                    if (cell.IsValid)
                        _lastKnownStoragePos[unloadable.def] = cell;

                    job.SetTarget(TargetIndex.A, unloadable);
                    job.SetTarget(TargetIndex.B,
                        cell == IntVec3.Invalid ? (LocalTargetInfo)(Thing)destination! : cell);

                    if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                    {
                        if (!TryFindAlternativeCell(unloadable, out var altCell)
                            || !pawn.Map.reservationManager.Reserve(pawn, job, altCell))
                        {
                            DropAtFeet(unloadable, comp, inventory, loopBack);
                            return;
                        }
                        job.SetTarget(TargetIndex.B, altCell);
                        _lastKnownStoragePos[unloadable.def] = altCell;
                    }

                    _countToDrop = unloadable.stackCount;
                    return;
                }

                // Fallback: try last known position for this def
                if (Settings.DropNearStorage && TryUseLastKnownPosition(unloadable))
                    return;

                // Final fallback: drop at feet
                DropAtFeet(unloadable, comp, inventory, loopBack);
            }
        };
    }

    /// <summary>
    /// Tries to use cached storage position for this thing's def.
    /// </summary>
    private bool TryUseLastKnownPosition(Thing thing)
    {
        if (!_lastKnownStoragePos.TryGetValue(thing.def, out var lastPos))
            return false;

        if (!lastPos.IsValid || !lastPos.InBounds(pawn.Map))
            return false;

        if (!pawn.CanReach(lastPos, PathEndMode.ClosestTouch, Danger.Some))
            return false;

        // WHY: Only use if zone still accepts this thing type
        var slotGroup = lastPos.GetSlotGroup(pawn.Map);
        if (slotGroup?.parent?.Accepts(thing) != true)
            return false;

        Log.Info($"[Unload] Using last known storage pos {lastPos} for {thing.LabelShort}");
        job.SetTarget(TargetIndex.A, thing);
        job.SetTarget(TargetIndex.B, lastPos);
        _countToDrop = thing.stackCount;
        return true;
    }

    /// <summary>
    /// Drops thing at pawn's feet and continues to next item.
    /// </summary>
    private void DropAtFeet(Thing thing, CompHauledToInventory comp, ThingOwner inventory, Toil loopBack)
    {
        Log.Info($"[Unload] No storage for {thing.LabelShort}, dropping at feet");

        var thingDef = thing.def;
        comp.GetHashSet().Remove(thing);

        inventory.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, thing.stackCount, out _);

        var hasMoreTracked = comp.GetHashSet().Any(t =>
            t != null && !t.Destroyed && t.def == thingDef && inventory.Contains(t));

        if (!hasMoreTracked)
            comp.UntrackDef(thingDef);

        if (comp.GetHashSet().Count > 0)
        {
            Log.Info($"[Unload] {comp.GetHashSet().Count} items remaining, looping back");
            pawn.jobs.curDriver.JumpToToil(loopBack);
        }
        else
        {
            Log.Info("[Unload] All items processed, ending job");
            comp.ClearTracking();
            EndJobWith(JobCondition.Succeeded);
        }
    }

    private static void SyncTrackedItems(CompHauledToInventory comp, ThingOwner inventory)
    {
        var trackedItems = comp.GetHashSet();
        var staleDefs = new HashSet<ThingDef>();

        foreach (var item in trackedItems)
        {
            if (item == null || item.Destroyed || !inventory.Contains(item))
            {
                if (item?.def != null) staleDefs.Add(item.def);
            }
        }

        trackedItems.RemoveWhere(t => t == null || t.Destroyed || !inventory.Contains(t));

        if (staleDefs.Count == 0 || trackedItems.Count == 0)
        {
            if (trackedItems.Count == 0) comp.ClearTracking();
            return;
        }

        foreach (var def in staleDefs)
        {
            if (trackedItems.Any(t => t?.def == def)) continue;

            for (var i = 0; i < inventory.Count; i++)
            {
                if (inventory[i].def == def && !trackedItems.Contains(inventory[i]))
                {
                    comp.RegisterHauledItem(inventory[i]);
                    break;
                }
            }
        }
    }

    private Toil PullItemFromInventory(CompHauledToInventory comp, Toil wait)
    {
        return new Toil
        {
            initAction = () =>
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;
                var inventory = pawn.inventory?.innerContainer;

                if (thing == null || inventory == null || !inventory.Contains(thing))
                {
                    pawn.jobs.curDriver.JumpToToil(wait);
                    return;
                }

                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    || !thing.def.EverStorable(false))
                {
                    comp.GetHashSet().Remove(thing);
                    inventory.TryDrop(thing, ThingPlaceMode.Near, _countToDrop, out _);

                    if (!comp.GetHashSet().Any(t => t?.def == thing.def))
                        comp.UntrackDef(thing.def);

                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var thingDef = thing.def;
                comp.GetHashSet().Remove(thing);

                inventory.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, _countToDrop, out thing);

                job.count = _countToDrop;
                job.SetTarget(TargetIndex.A, thing);

                if (!comp.GetHashSet().Any(t => t?.def == thingDef))
                    comp.UntrackDef(thingDef);

                if (ModCompatibility.CombatExtendedIsActive)
                    CompatHelper.UpdateInventory(pawn);

                thing!.SetForbidden(false, false);
            }
        };
    }

    private bool TryFindAlternativeCell(Thing thing, out IntVec3 cell) =>
        StoreUtility.TryFindBestBetterStoreCellFor(
            thing, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out cell);

    /// <summary>
    /// Clears cached storage positions. Call on map change.
    /// </summary>
    public static void ClearCache() => _lastKnownStoragePos.Clear();
}