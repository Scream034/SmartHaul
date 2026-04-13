namespace SmartHaul;

/// <summary>
/// Core logic: collect items into pawn inventory, haul or drop based on work type.
/// </summary>
public static class InventoryCollector
{
    // WHY: Use readonly struct instead of record struct for .NET 4.8 compatibility
    private readonly struct WorkState
    {
        public readonly WorkCategory Category;
        public readonly JobDef? LastJobDef;

        public WorkState(WorkCategory category, JobDef? lastJobDef)
        {
            Category = category;
            LastJobDef = lastJobDef;
        }
    }

    private static readonly Dictionary<int, WorkState> _pawnWorkState = new();

    /// <summary>
    /// Tracks how many of each ThingDef we collected per pawn.
    /// Key = pawn.thingIDNumber, Value = (ThingDef -> count collected by SmartHaul).
    /// </summary>
    private static readonly Dictionary<int, Dictionary<ThingDef, int>> _collectedItems = new();

    #region Public API

    /// <summary>
    /// Collects all nearby haulable items into pawn's inventory.
    /// </summary>
    /// <returns>Number of stacks collected.</returns>
    public static int CollectNearbyItems(Pawn pawn, IntVec3 workPos)
    {
        if (pawn?.Map == null) return 0;
        if (!pawn.IsColonistPlayerControlled) return 0;

        var map = pawn.Map;
        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null) return 0;

        Log.Info($"Collect: {pawn.LabelShort} at {workPos}");

        var items = new List<Thing>(16);
        foreach (var thing in GenRadial.RadialDistinctThingsAround(
            workPos, map, Constants.PICKUP_RADIUS, true))
        {
            if (CanCollect(pawn, thing))
                items.Add(thing);
        }

        if (items.Count == 0)
        {
            Log.Info($"  No items found");
            return 0;
        }

        // CRITICAL: Sort by thingIDNumber for MP determinism
        items.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

        Log.Info($"  Found {items.Count} items");

        var collected = 0;
        foreach (var item in items)
        {
            if (item == null || item.Destroyed || !item.Spawned) continue;

            var countBefore = item.stackCount;
            var def = item.def;

            if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, item, item.stackCount))
            {
                var canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, item);
                if (canTake <= 0) continue;

                var split = item.SplitOff(canTake);
                if (TryPickup(pawn, split, inventory))
                {
                    collected++;
                    TrackCollected(pawn, def, canTake);
                    Log.Info($"    +{canTake} {def.label} (partial)");
                }
                else
                {
                    GenPlace.TryPlaceThing(split, workPos, map, ThingPlaceMode.Near);
                }
            }
            else
            {
                if (TryPickup(pawn, item, inventory))
                {
                    collected++;
                    TrackCollected(pawn, def, countBefore);
                    Log.Info($"    +{countBefore} {def.label}");
                }
            }
        }

        Log.Info($"  Collected {collected} stacks, inv: {GetInventoryFillPercent(pawn):P0}");
        return collected;
    }

    /// <summary>
    /// Called after a work action completes. Updates state and optionally acts immediately.
    /// </summary>
    /// <param name="pawn">Worker pawn.</param>
    /// <param name="category">Work category for item handling logic.</param>
    /// <param name="explicitJobDef">
    /// WHY: For crafting, CurJob changes before OnWorkDone is called.
    /// Pass the original DoBill jobDef explicitly to preserve correct WorkState.
    /// </param>
    /// <param name="forceHandle">
    /// If true, handle items immediately regardless of inventory threshold.
    /// </param>
    public static void OnWorkDone(
        Pawn pawn,
        WorkCategory category,
        JobDef? explicitJobDef = null,
        bool forceHandle = false)
    {
        if (pawn?.Map == null) return;

        var jobDef = explicitJobDef ?? pawn.CurJob?.def;
        _pawnWorkState[pawn.thingIDNumber] = new WorkState(category, jobDef);

        if (!HasCollectedItems(pawn)) return;

        var fillPercent = GetInventoryFillPercent(pawn);
        var shouldAct = forceHandle || fillPercent >= Settings.InventoryThreshold;

        if (shouldAct)
        {
            Log.Info($"  Acting (force={forceHandle}, inv={fillPercent:P0})...");
            HandleItems(pawn, category);
        }
    }

    /// <summary>
    /// Called when pawn starts a new job. Handles collected items if work type changes.
    /// </summary>
    public static void OnJobStarting(Pawn pawn, JobDef? newJobDef)
    {
        if (pawn?.Map == null) return;
        if (!pawn.IsColonistPlayerControlled) return;

        if (WorkCategoryHelper.IsWaitJob(newJobDef)) return;

        if (!HasCollectedItems(pawn))
        {
            _pawnWorkState.Remove(pawn.thingIDNumber);
            return;
        }

        // Critical jobs — drop immediately, no hauling
        if (WorkCategoryHelper.IsCriticalJob(newJobDef))
        {
            Log.Info($"CriticalJob: {pawn.LabelShort} -> {newJobDef?.defName}, dropping");
            DropAllCollectedItems(pawn);
            _pawnWorkState.Remove(pawn.thingIDNumber);
            return;
        }

        var newCategory = WorkCategoryHelper.GetCategory(newJobDef);

        // Transitioning to unmanaged work
        if (newCategory == WorkCategory.None)
        {
            // WHY: Goto/GotoWander are intermediate steps between same-category jobs
            if (newJobDef == JobDefOf.Goto || newJobDef == JobDefOf.GotoWander)
                return;

            if (!_pawnWorkState.TryGetValue(pawn.thingIDNumber, out var lastStateNone))
            {
                Log.Info($"NoState+Unmanaged: {pawn.LabelShort} -> {newJobDef?.defName}, dropping");
                DropAllCollectedItems(pawn);
                return;
            }

            Log.Info($"WorkChange: {pawn.LabelShort} " +
                     $"{lastStateNone.Category}({lastStateNone.LastJobDef?.defName}) " +
                     $"-> {newJobDef?.defName} (unmanaged)");
            HandleItems(pawn, lastStateNone.Category);
            _pawnWorkState.Remove(pawn.thingIDNumber);
            return;
        }

        if (!_pawnWorkState.TryGetValue(pawn.thingIDNumber, out var lastState))
        {
            Log.Info($"NoState+Managed: {pawn.LabelShort} -> {newJobDef?.defName}, dropping");
            DropAllCollectedItems(pawn);
            return;
        }

        // WHY: Same JobDef (Mine→Mine, DoBill→DoBill) — keep accumulating
        if (lastState.LastJobDef == newJobDef)
            return;

        // Different managed job
        Log.Info($"WorkChange: {pawn.LabelShort} " +
                 $"{lastState.LastJobDef?.defName} -> {newJobDef?.defName}");
        HandleItems(pawn, lastState.Category);
        _pawnWorkState.Remove(pawn.thingIDNumber);
    }

    /// <summary>
    /// Drops only items collected by SmartHaul (from both states), leaving personal items untouched.
    /// </summary>
    public static void DropAllCollectedItems(Pawn pawn)
    {
        if (pawn?.Map == null) return;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null || inventory.Count == 0) return;

        var map = pawn.Map;
        var dropPos = pawn.Position;
        var dropped = 0;

        // 1. Drop from CompHauledToInventory
        var comp = pawn.TryGetComp<CompHauledToInventory>();
        if (comp != null && comp.HasItems())
        {
            // CRITICAL: Copy to list to avoid collection modified exception during drops
            var compItems = new List<Thing>(comp.GetHashSet());
            compItems.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            foreach (var item in compItems)
            {
                if (item == null || item.Destroyed || !inventory.Contains(item)) continue;

                if (inventory.TryDrop(item, dropPos, map, ThingPlaceMode.Near, out var result))
                {
                    dropped++;
                    TryMergeWithNearby(result, map);
                }
            }
            comp.ClearTracking();
        }

        // 2. Drop from _collectedItems (Pre-haul state)
        if (_collectedItems.TryGetValue(pawn.thingIDNumber, out var tracked) && tracked.Count > 0)
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

                candidates.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

                foreach (var item in candidates)
                {
                    if (remaining <= 0) break;
                    var dropCount = Math.Min(item.stackCount, remaining);
                    toDrop.Add((item, dropCount));
                    remaining -= dropCount;
                }
            }

            foreach (var (thing, count) in toDrop)
            {
                if (thing == null || thing.Destroyed) continue;

                if (count >= thing.stackCount)
                {
                    if (inventory.TryDrop(thing, dropPos, map, ThingPlaceMode.Near, out var result))
                    {
                        dropped++;
                        TryMergeWithNearby(result, map);
                    }
                }
                else
                {
                    var split = thing.SplitOff(count);
                    if (GenPlace.TryPlaceThing(split, dropPos, map, ThingPlaceMode.Near))
                    {
                        dropped++;
                        TryMergeWithNearby(split, map);
                    }
                }
            }

            tracked.Clear();
        }

        if (dropped > 0)
            Log.Info($"  Dropped {dropped} stacks at {dropPos}");
    }

    /// <summary>
    /// Checks if pawn has any items tracked by SmartHaul or CompHauledToInventory.
    /// </summary>
    public static bool HasCollectedItems(Pawn pawn)
    {
        if (pawn == null) return false;

        var comp = pawn.TryGetComp<CompHauledToInventory>();
        if (comp != null && comp.HasItems()) return true;

        if (!_collectedItems.TryGetValue(pawn.thingIDNumber, out var dict)) return false;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null) return false;

        foreach (var kvp in dict)
        {
            if (kvp.Value <= 0) continue;
            foreach (var item in inventory)
            {
                if (item.def == kvp.Key)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the tracked item dictionary for a pawn, or null if none.
    /// Used by <see cref="JobDriver_UnloadToStorage"/> and <see cref="JobDriver_HaulSmart"/>.
    /// </summary>
    public static Dictionary<ThingDef, int>? GetTrackedItems(Pawn pawn)
    {
        if (pawn == null) return null;
        _collectedItems.TryGetValue(pawn.thingIDNumber, out var dict);
        return dict;
    }

    /// <summary>
    /// Public accessor for tracking collected items from external job drivers.
    /// </summary>
    /// <param name="pawn">The pawn carrying the item.</param>
    /// <param name="def">ThingDef of the collected item.</param>
    /// <param name="count">Count collected.</param>
    public static void TrackCollectedPublic(Pawn pawn, ThingDef def, int count) =>
        TrackCollected(pawn, def, count);

    /// <summary>Clears all tracking for a specific pawn.</summary>
    public static void ClearPawnTracking(Pawn pawn)
    {
        if (pawn == null) return;
        _pawnWorkState.Remove(pawn.thingIDNumber);
        _collectedItems.Remove(pawn.thingIDNumber);
        pawn.TryGetComp<CompHauledToInventory>()?.ClearTracking();
    }

    /// <summary>Clears all tracking data (e.g. on game load).</summary>
    public static void ClearTracking()
    {
        _pawnWorkState.Clear();
        _collectedItems.Clear();
    }

    /// <summary>Merges a dropped thing with adjacent identical stacks on the same cell.</summary>
    internal static void TryMergeWithNearby(Thing thing, Map map)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned) return;
        if (thing.def.stackLimit <= 1) return;

        var pos = thing.Position;
        foreach (var other in pos.GetThingList(map))
        {
            if (other == thing) continue;
            if (other.def != thing.def) continue;
            if (!other.CanStackWith(thing)) continue;

            other.TryAbsorbStack(thing, true);
            if (thing.Destroyed) return;
        }
    }

    #endregion

    #region Private: Logic

    private static void HandleItems(Pawn pawn, WorkCategory category)
    {
        if (category == WorkCategory.Farming
            || category == WorkCategory.Crafting
            || category == WorkCategory.Hauling)
        {
            if (TryStartHauling(pawn))
            {
                Log.Info($"  -> Hauling to storage");
            }
            else
            {
                Log.Info($"  -> No storage found, dropping");
                DropAllCollectedItems(pawn);
            }
        }
        else
        {
            Log.Info($"  -> Dropping pile (Extraction)");
            DropAllCollectedItems(pawn);
        }
    }

    /// <summary>
    /// Transfers tracked items to CompHauledToInventory and queues UnloadInventory job.
    /// WHY: UnloadInventory uses vanilla carry toils (carryTracker + PlaceHauledThingInCell)
    /// which correctly place items into storage cells, not just drop near pawn.
    /// </summary>
    private static bool TryStartHauling(Pawn pawn)
    {
        if (pawn?.Map == null) return false;

        if (!_collectedItems.TryGetValue(pawn.thingIDNumber, out var tracked))
            return false;

        // Check at least one item has valid storage
        var hasStorage = false;
        foreach (var kvp in tracked)
        {
            if (kvp.Value <= 0) continue;
            foreach (var item in pawn.inventory.innerContainer)
            {
                if (item.def != kvp.Key) continue;
                if (StoreUtility.TryFindBestBetterStoreCellFor(
                        item, pawn, pawn.Map, StoragePriority.Unstored,
                        pawn.Faction, out _))
                {
                    hasStorage = true;
                    break;
                }
            }
            if (hasStorage) break;
        }

        if (!hasStorage) return false;

        // WHY: Transfer tracking from InventoryCollector's static dict to CompHauledToInventory.
        // UnloadInventory JobDriver reads from CompHauledToInventory, not _collectedItems.
        var comp = pawn.TryGetComp<CompHauledToInventory>();
        if (comp == null) return false;

        foreach (var kvp in tracked)
        {
            if (kvp.Value <= 0) continue;

            var remaining = kvp.Value;
            foreach (var item in pawn.inventory.innerContainer)
            {
                if (item.def != kvp.Key) continue;
                comp.RegisterHauledItem(item);
                remaining -= item.stackCount;
                if (remaining <= 0) break;
            }
        }

        tracked.Clear();

        var unloadJob = JobMaker.MakeJob(SmartHaulJobDefOf.SmartHaul_UnloadInventory);
        if (!unloadJob.TryMakePreToilReservations(pawn, false))
        {
            comp.ClearTracking();
            return false;
        }

        Log.Info($"    Queuing UnloadInventory for {pawn.LabelShort}");
        pawn.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        return true;
    }

    #endregion

    #region Private: Tracking

    private static void TrackCollected(Pawn pawn, ThingDef def, int count)
    {
        var id = pawn.thingIDNumber;
        if (!_collectedItems.TryGetValue(id, out var dict))
        {
            dict = new Dictionary<ThingDef, int>(4);
            _collectedItems[id] = dict;
        }
        dict.TryGetValue(def, out var existing);
        dict[def] = existing + count;
    }

    #endregion

    #region Private: Helpers

    private static bool CanCollect(Pawn pawn, Thing thing)
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

    private static bool TryPickup(Pawn pawn, Thing thing, ThingOwner inventory)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned) return false;

        thing.DeSpawn();

        if (inventory.TryAdd(thing, true)) return true;

        // WHY: If TryAdd fails, re-spawn to not lose the item
        GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        return false;
    }

    private static float GetInventoryFillPercent(Pawn pawn)
    {
        var capacity = MassUtility.Capacity(pawn);
        if (capacity <= 0f) return 1f;
        return (MassUtility.GearMass(pawn) + MassUtility.InventoryMass(pawn)) / capacity;
    }

    private static bool IsChunk(Thing thing) =>
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.Chunks) == true ||
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.StoneChunks) == true;

    #endregion
}