namespace SmartHaul;

/// <summary>
/// Smart haul job driver: walk to each nearby item, pick it up into inventory,
/// then walk to storage and unload everything.
/// targetA = current pickup target (changes per iteration).
/// targetQueueA = remaining items to pick up.
/// countQueue = stack counts per item.
/// targetB = storage destination (cell or thing).
/// </summary>
public sealed class JobDriver_HaulSmart : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        // WHY: Reserve first target; others reserved on approach via queue.
        var target = job.targetA.Thing;
        if (target == null || target.Destroyed) return false;
        return pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
    }

    public override void Notify_Starting()
    {
        base.Notify_Starting();

        // WHY: Drop items immediately if the pawn is interrupted (e.g. falls asleep)
        AddFinishAction(condition =>
        {
            if (InventoryCollector.HasCollectedItems(pawn))
            {
                Log.Info($"HaulSmart: {pawn.LabelShort} interrupted ({condition}). Dropping items.");
                InventoryCollector.DropAllCollectedItems(pawn);
                InventoryCollector.ClearPawnTracking(pawn);
            }
        });
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        // --- Toil 1: Build pickup list and find storage ---
        var buildList = ToilMaker.MakeToil("SmartHaul_BuildList");
        buildList.initAction = () =>
        {
            try
            {
                BuildPickupList(pawn, job);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[SmartHaul] HaulSmart BuildList error: {ex}");
                EndJobWith(JobCondition.Errored);
            }
        };
        buildList.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return buildList;

        // --- Toil 2: Process next target from queue ---
        var processNext = ToilMaker.MakeToil("SmartHaul_ProcessNext");
        processNext.initAction = () =>
        {
            // Dequeue next valid item
            while (job.targetQueueA is { Count: > 0 })
            {
                var nextTarget = job.targetQueueA[0];
                var nextCount = job.countQueue is { Count: > 0 } ? job.countQueue[0] : -1;

                job.targetQueueA.RemoveAt(0);
                if (job.countQueue is { Count: > 0 })
                    job.countQueue.RemoveAt(0);

                var thing = nextTarget.Thing;
                if (thing == null || thing.Destroyed || !thing.Spawned) continue;
                if (thing.IsForbidden(pawn)) continue;
                if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1)) break;

                job.SetTarget(TargetIndex.A, thing);
                job.count = nextCount > 0 ? nextCount : thing.stackCount;

                // WHY: Try to reserve; if someone else got it, skip.
                if (pawn.Reserve(thing, job, 1, -1, null, false))
                    return;
            }

            // No more items — skip to storage toil
            job.targetQueueA?.Clear();
            job.countQueue?.Clear();
        };
        processNext.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return processNext;

        // --- Toil 3: Walk to current target ---
        var gotoItem = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
        gotoItem.AddFailCondition(() =>
        {
            var thing = job.GetTarget(TargetIndex.A).Thing;
            return thing == null || thing.Destroyed || !thing.Spawned;
        });
        yield return gotoItem;

        // --- Toil 4: Pick up item into inventory ---
        var pickup = ToilMaker.MakeToil("SmartHaul_Pickup");
        pickup.initAction = () =>
        {
            try
            {
                PickupCurrentTarget(pawn, job);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[SmartHaul] HaulSmart Pickup error: {ex}");
            }
        };
        pickup.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return pickup;

        // --- Toil 5: Loop back if more items ---
        yield return Toils_Jump.JumpIf(processNext, () =>
            job.targetQueueA is { Count: > 0 }
            && !MassUtility.IsOverEncumbered(pawn));

        // --- Toil 6: Walk to storage ---
        var gotoStorage = ToilMaker.MakeToil("SmartHaul_GotoStorage");
        gotoStorage.initAction = () =>
        {
            if (!InventoryCollector.HasCollectedItems(pawn))
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            var storageCell = FindBestStorageCell(pawn);
            if (!storageCell.IsValid)
            {
                Log.Info($"HaulSmart: no storage for {pawn.LabelShort}, dropping");
                InventoryCollector.DropAllCollectedItems(pawn);
                InventoryCollector.ClearPawnTracking(pawn);
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            pawn.pather.StartPath(storageCell, PathEndMode.ClosestTouch);
        };
        gotoStorage.defaultCompleteMode = ToilCompleteMode.PatherArrival;
        gotoStorage.FailOnDespawnedNullOrForbidden(TargetIndex.B);
        yield return gotoStorage;

        // --- Toil 7: Unload at storage ---
        var unload = ToilMaker.MakeToil("SmartHaul_Unload");
        unload.initAction = () =>
        {
            try
            {
                UnloadAtStorage(pawn);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[SmartHaul] HaulSmart Unload error: {ex}");
                InventoryCollector.DropAllCollectedItems(pawn);
                InventoryCollector.ClearPawnTracking(pawn);
            }
        };
        unload.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return unload;
    }

    #region Build Pickup List

    /// <summary>
    /// Builds the prioritized list of items to pick up.
    /// Stores them in job.targetQueueA + job.countQueue.
    /// Also finds storage and sets job.targetB.
    /// </summary>
    private static void BuildPickupList(Pawn pawn, Job job)
    {
        if (pawn?.Map == null) return;

        var map = pawn.Map;
        var targetItem = job.targetA.Thing;
        if (targetItem == null || targetItem.Destroyed || !targetItem.Spawned)
            return;

        // Find storage for main item
        if (!StoreUtility.TryFindBestBetterStoreCellFor(
                targetItem, pawn, map, StoragePriority.Unstored,
                pawn.Faction, out var targetStorageCell))
        {
            Log.Info($"HaulSmart: no storage for {targetItem.LabelShort}");
            return;
        }

        var targetSlotGroup = map.haulDestinationManager.SlotGroupAt(targetStorageCell);

        // Set storage as targetB
        var storageOwner = targetSlotGroup?.parent as Thing;
        if (storageOwner != null)
            job.SetTarget(TargetIndex.B, storageOwner);
        else
            job.SetTarget(TargetIndex.B, targetStorageCell);

        // Collect neighbors
        var candidates = new List<Thing>(32);
        foreach (var thing in GenRadial.RadialDistinctThingsAround(
            targetItem.Position, map, Settings.HaulPickupRadius, true))
        {
            if (thing == targetItem) continue;
            if (!CanCollectForHaul(pawn, thing)) continue;
            candidates.Add(thing);
        }

        // Sort by priority
        HaulPriorityHelper.SortCandidates(
            candidates, targetItem.Position, pawn, targetStorageCell, targetSlotGroup);

        // Insert target at front
        candidates.Insert(0, targetItem);

        // Build queue — trim by mass capacity
        var maxMass = MassUtility.Capacity(pawn) - MassUtility.GearMass(pawn) - MassUtility.InventoryMass(pawn);
        var totalMass = 0f;

        job.targetQueueA = new List<LocalTargetInfo>(candidates.Count);
        job.countQueue = new List<int>(candidates.Count);

        foreach (var item in candidates)
        {
            var itemMass = item.stackCount * item.GetStatValue(StatDefOf.Mass, true, -1);
            if (totalMass + itemMass > maxMass && job.targetQueueA.Count > 0)
            {
                // WHY: Try partial stack for last item
                var perUnit = item.GetStatValue(StatDefOf.Mass, true, -1);
                var canFit = perUnit > 0f ? (int)((maxMass - totalMass) / perUnit) : 0;
                if (canFit > 0)
                {
                    job.targetQueueA.Add(item);
                    job.countQueue.Add(canFit);
                    totalMass += canFit * perUnit;
                }
                break;
            }

            job.targetQueueA.Add(item);
            job.countQueue.Add(item.stackCount);
            totalMass += itemMass;
        }

        // Pop first item back to targetA
        if (job.targetQueueA.Count > 0)
        {
            job.SetTarget(TargetIndex.A, job.targetQueueA[0].Thing);
            job.count = job.countQueue[0];
            job.targetQueueA.RemoveAt(0);
            job.countQueue.RemoveAt(0);
        }

        Log.Info($"HaulSmart: {pawn.LabelShort} queued {job.targetQueueA.Count + 1} items");
    }

    #endregion

    #region Pickup

    /// <summary>
    /// Picks up the current targetA into pawn inventory.
    /// </summary>
    private static void PickupCurrentTarget(Pawn pawn, Job job)
    {
        var thing = job.GetTarget(TargetIndex.A).Thing;
        if (thing == null || thing.Destroyed || !thing.Spawned) return;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null) return;

        var wantCount = job.count > 0 ? job.count : thing.stackCount;
        var canCarry = MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing);
        var actualCount = Math.Min(Math.Min(wantCount, canCarry), thing.stackCount);

        if (actualCount <= 0) return;

        var def = thing.def;

        if (actualCount < thing.stackCount)
        {
            var split = thing.SplitOff(actualCount);
            if (TryPickupIntoInventory(pawn, split, inventory))
            {
                InventoryCollector.TrackCollectedPublic(pawn, def, actualCount);
                Log.Info($"  +{actualCount} {def.label} (partial)");
            }
            else
            {
                GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
        }
        else
        {
            if (TryPickupIntoInventory(pawn, thing, inventory))
            {
                InventoryCollector.TrackCollectedPublic(pawn, def, actualCount);
                Log.Info($"  +{actualCount} {def.label}");
            }
        }
    }

    #endregion

    #region Unload

    /// <summary>
    /// Unloads all SmartHaul-tracked items from inventory at pawn's current position.
    /// WHY: Pawn already walked to storage (Toil 6), so pawn.Position is at/near storage.
    /// </summary>
    private static void UnloadAtStorage(Pawn pawn)
    {
        if (pawn?.Map == null) return;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null || inventory.Count == 0) return;

        var tracked = InventoryCollector.GetTrackedItems(pawn);
        if (tracked == null || tracked.Count == 0) return;

        var map = pawn.Map;
        var toDrop = BuildDropList(inventory, tracked);
        var dropped = 0;

        foreach (var (thing, count) in toDrop)
        {
            if (thing == null || thing.Destroyed) continue;

            // WHY: Drop at pawn.Position — pawn is already at storage.
            // ThingPlaceMode.Near finds closest valid cell.
            var dropPos = pawn.Position;

            if (count >= thing.stackCount)
            {
                if (inventory.TryDrop(thing, dropPos, map, ThingPlaceMode.Near, out var result))
                {
                    dropped++;
                    InventoryCollector.TryMergeWithNearby(result, map);
                }
            }
            else
            {
                var split = thing.SplitOff(count);
                if (GenPlace.TryPlaceThing(split, dropPos, map, ThingPlaceMode.Near))
                {
                    dropped++;
                    InventoryCollector.TryMergeWithNearby(split, map);
                }
            }
        }

        tracked.Clear();
        InventoryCollector.ClearPawnTracking(pawn);
        Log.Info($"HaulSmart: {pawn.LabelShort} unloaded {dropped} stacks near {pawn.Position}");
    }

    /// <summary>
    /// Builds deterministic list of (thing, count) to drop from inventory.
    /// </summary>
    private static List<(Thing thing, int count)> BuildDropList(
        ThingOwner inventory,
        Dictionary<ThingDef, int> tracked)
    {
        var toDrop = new List<(Thing thing, int count)>(tracked.Count);

        foreach (var kvp in tracked)
        {
            if (kvp.Value <= 0) continue;

            var remaining = kvp.Value;
            var candidates = new List<Thing>(4);

            foreach (var item in inventory)
            {
                if (item.def == kvp.Key)
                    candidates.Add(item);
            }

            // CRITICAL: deterministic order for MP
            candidates.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            foreach (var item in candidates)
            {
                if (remaining <= 0) break;
                var dropCount = Math.Min(item.stackCount, remaining);
                toDrop.Add((item, dropCount));
                remaining -= dropCount;
            }
        }

        return toDrop;
    }

    #endregion

    #region Helpers

    private static IntVec3 FindBestStorageCell(Pawn pawn)
    {
        if (pawn?.Map == null) return IntVec3.Invalid;

        var tracked = InventoryCollector.GetTrackedItems(pawn);
        if (tracked == null) return IntVec3.Invalid;

        foreach (var kvp in tracked)
        {
            if (kvp.Value <= 0) continue;

            foreach (var item in pawn.inventory.innerContainer)
            {
                if (item.def != kvp.Key) continue;
                if (StoreUtility.TryFindBestBetterStoreCellFor(
                        item, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, out var cell))
                    return cell;
            }
        }

        return IntVec3.Invalid;
    }

    private static bool CanCollectForHaul(Pawn pawn, Thing thing)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned) return false;
        if (!thing.def.EverHaulable) return false;
        if (thing.IsForbidden(pawn)) return false;
        if (thing is Corpse) return false;
        if (!Settings.CollectChunks && IsChunk(thing)) return false;
        if (thing.IsInValidBestStorage()) return false;
        if (!pawn.CanReserve(thing)) return false;
        return true;
    }

    private static bool TryPickupIntoInventory(Pawn pawn, Thing thing, ThingOwner inventory)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned) return false;

        thing.DeSpawn();

        if (inventory.TryAdd(thing, true)) return true;

        // WHY: If TryAdd fails, re-spawn to not lose the item
        GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        return false;
    }

    private static bool IsChunk(Thing thing) =>
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.Chunks) == true ||
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.StoneChunks) == true;

    #endregion
}