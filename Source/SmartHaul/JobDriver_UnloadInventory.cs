namespace SmartHaul;

/// <summary>
/// Job driver: iteratively pulls items from inventory into carryTracker,
/// walks to the correct storage cell, and places them — one at a time.
/// Mirrors PUAH's UnloadYourHauledInventory approach using vanilla carry toils.
/// </summary>
public sealed class JobDriver_UnloadInventory : JobDriver
{
    private int _countToDrop = -1;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _countToDrop, "countToDrop", -1);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    public override IEnumerable<Toil> MakeNewToils()
    {
        // --- Toil 1: Brief wait (visual feedback) ---
        var begin = Toils_General.Wait(2);
        yield return begin;

        // --- Toil 2: Find storage for next item ---
        yield return FindStorageForNextItem(begin);

        // --- Toil 3: Pull item from inventory into carryTracker ---
        yield return PullItemFromInventory(begin);

        // --- Toil 4: Carry to cell ---
        var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
        yield return carryToCell;

        // --- Toil 5: Place at cell ---
        yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

        // --- Toil 6: Release reservation ---
        yield return ReleaseReservation();

        // --- Toil 7: Loop back ---
        yield return Toils_Jump.Jump(begin);
    }

    public override void Notify_Starting()
    {
        base.Notify_Starting();

        // WHY: If the job is forcefully interrupted (sleep, combat, draft),
        // drop all remaining tracked items so they don't get stuck in inventory forever.
        AddFinishAction(condition =>
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            if (comp != null && comp.HasItems())
            {
                Log.Info($"UnloadInventory: {pawn.LabelShort} interrupted ({condition}), dropping remaining items.");
                InventoryCollector.DropAllCollectedItems(pawn);
            }
        });
    }

    /// <summary>
    /// Finds storage destination for the next tracked item.
    /// Sets targetA = item, targetB = storage cell/thing.
    /// </summary>
    private Toil FindStorageForNextItem(Toil loopBack)
    {
        var toil = ToilMaker.MakeToil("SmartHaul_FindStorage");
        toil.initAction = () =>
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            if (comp == null)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            comp.CleanupDestroyed();
            var trackedItems = comp.GetHashSet();

            var unloadable = FirstUnloadableThing(pawn, trackedItems);
            if (unloadable.Count == 0)
            {
                if (trackedItems.Count == 0)
                    EndJobWith(JobCondition.Succeeded);
                else
                    comp.ClearTracking();
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            if (StoreUtility.TryFindBestBetterStorageFor(
                    unloadable.Thing, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction,
                    out var cell, out var destination))
            {
                job.SetTarget(TargetIndex.A, unloadable.Thing);

                if (cell == IntVec3.Invalid && destination is Thing destThing)
                    job.SetTarget(TargetIndex.B, destThing);
                else
                    job.SetTarget(TargetIndex.B, cell);

                if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
                {
                    Log.Info($"Unload: can't reserve {job.targetB}, dropping {unloadable.Thing.LabelShort}");
                    pawn.inventory.innerContainer.TryDrop(
                        unloadable.Thing, ThingPlaceMode.Near,
                        unloadable.Thing.stackCount, out _);
                    trackedItems.Remove(unloadable.Thing);

                    // FIX: Process next item instead of ending the entire job
                    pawn.jobs.curDriver.JumpToToil(loopBack);
                    return;
                }

                _countToDrop = unloadable.Thing.stackCount;
                Log.Info($"Unload: {pawn.LabelShort} -> {unloadable.Thing.LabelShort} to {job.targetB}");
            }
            else
            {
                Log.Info($"Unload: no storage for {unloadable.Thing.LabelShort}, dropping");
                pawn.inventory.innerContainer.TryDrop(
                    unloadable.Thing, ThingPlaceMode.Near,
                    unloadable.Thing.stackCount, out _);
                trackedItems.Remove(unloadable.Thing);

                // FIX: Process next item instead of ending the entire job
                pawn.jobs.curDriver.JumpToToil(loopBack);
            }
        };
        toil.defaultCompleteMode = ToilCompleteMode.Instant;
        return toil;
    }

    /// <summary>
    /// Transfers item from inventory to carryTracker for vanilla carry toils.
    /// </summary>
    private Toil PullItemFromInventory(Toil loopBack)
    {
        var toil = ToilMaker.MakeToil("SmartHaul_PullFromInv");
        toil.initAction = () =>
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            var thing = job.GetTarget(TargetIndex.A).Thing;

            if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
            {
                if (thing != null)
                    comp?.UnregisterHauledItem(thing);

                pawn.jobs.curDriver.JumpToToil(loopBack);
                return;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                || !thing.def.EverStorable(false))
            {
                pawn.inventory.innerContainer.TryDrop(
                    thing, ThingPlaceMode.Near, _countToDrop, out thing);

                if (thing != null)
                    comp?.UnregisterHauledItem(thing);

                EndJobWith(JobCondition.Succeeded);
                return;
            }

            // WHY: Transfer to carryTracker so vanilla CarryHauledThingToCell works
            pawn.inventory.innerContainer.TryTransferToContainer(
                thing, pawn.carryTracker.innerContainer,
                _countToDrop, out thing);

            job.count = _countToDrop;
            job.SetTarget(TargetIndex.A, thing);

            if (thing != null)
                comp?.UnregisterHauledItem(thing);

            thing?.SetForbidden(false, false);
        };
        toil.defaultCompleteMode = ToilCompleteMode.Instant;
        return toil;
    }

    /// <summary>
    /// Releases reservation on targetB so next iteration can use different cells.
    /// </summary>
    private Toil ReleaseReservation()
    {
        var toil = ToilMaker.MakeToil("SmartHaul_ReleaseRes");
        toil.initAction = () =>
        {
            if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob))
                pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
        };
        toil.defaultCompleteMode = ToilCompleteMode.Instant;
        return toil;
    }

    /// <summary>
    /// Finds the first tracked item that's still in pawn's inventory.
    /// Handles merged stacks (thingID changes after TryAdd merge).
    /// </summary>
    private static ThingCount FirstUnloadableThing(Pawn pawn, HashSet<Thing> trackedItems)
    {
        var inventory = pawn.inventory.innerContainer;

        // CRITICAL: deterministic order for MP — sort by thingIDNumber
        var sorted = new List<Thing>(trackedItems);
        sorted.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        });

        foreach (var thing in sorted)
        {
            if (thing == null) continue;

            if (inventory.Contains(thing))
                return new ThingCount(thing, thing.stackCount);

            // WHY: After merging, the original Thing reference is gone.
            // Find by def instead.
            var stragglerDef = thing.def;
            trackedItems.Remove(thing);

            for (var i = 0; i < inventory.Count; i++)
            {
                if (inventory[i].def == stragglerDef)
                {
                    trackedItems.Add(inventory[i]);
                    return new ThingCount(inventory[i], inventory[i].stackCount);
                }
            }
        }

        return default;
    }
}