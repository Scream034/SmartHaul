using System.Linq;

namespace SmartHaul;

/// <summary>
/// Central hub for all auto-haul triggers.
/// Called by Harmony patches after work completes.
/// </summary>
public static class AutoHaulTriggers
{
    private static bool _isProcessing;
    private const float SEARCH_RADIUS = 7f;

    private static WorkTypeDef? _cookingWorkType;
    private static WorkTypeDef? CookingWorkType =>
        _cookingWorkType ??= DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");

    #region Direct Collection

    /// <summary>
    /// Immediately collects a spawned product into pawn's inventory.
    /// </summary>
    public static bool TryDirectCollect(Pawn pawn, Thing product, WorkTypeDef? workType, bool isRecipeProduct = false)
    {
        if (pawn?.Map == null || product == null || product.Destroyed)
            return false;

        if (!product.Spawned)
            return false;

        if (!CanPawnAutoHaul(pawn, out var reason))
        {
            Log.Verbose($"  TryDirectCollect FAIL: {reason}");
            return false;
        }

        if (product.IsForbidden(pawn))
            return false;

        if (product.IsInValidBestStorage())
            return false;

        if (product is Corpse)
            return false;

        if (IsChunk(product))
            return false;

        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null)
            return false;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null)
            return false;

        // Handle partial pickup if overencumbered
        if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, product, product.stackCount))
        {
            var canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, product);
            if (canTake <= 0)
                return false;

            var split = product.SplitOff(canTake);
            if (TryPickupThing(pawn, split, inventory, comp))
            {
                Log.Info($"  TryDirectCollect OK: {split.LabelShort} x{canTake} (partial)");
                return true;
            }

            GenPlace.TryPlaceThing(split, product.Position, pawn.Map, ThingPlaceMode.Near);
            return false;
        }

        if (!pawn.CanReserve(product))
            return false;

        if (TryPickupThing(pawn, product, inventory, comp))
        {
            Log.Info($"  TryDirectCollect OK: {product.LabelShort}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves a thing from the map into pawn's inventory.
    /// </summary>
    public static bool TryPickupThing(Pawn pawn, Thing thing, ThingOwner inventory, CompHauledToInventory comp)
    {
        if (thing == null || thing.Destroyed) return false;

        var map = pawn.Map;
        var originalPos = thing.Position;
        var thingDef = thing.def;

        if (thing.Spawned)
            thing.DeSpawn();

        if (inventory.TryAdd(thing, true))
        {
            // WHY: TryAdd may merge stacks. Register ALL stacks of this def.
            for (var i = 0; i < inventory.Count; i++)
            {
                if (inventory[i].def == thingDef)
                    comp.RegisterHauledItem(inventory[i]);
            }

            // Remove protection since it's now in inventory
            ThingProtection.Unprotect(thing);
            return true;
        }

        if (map != null)
            GenPlace.TryPlaceThing(thing, originalPos, map, ThingPlaceMode.Near);

        return false;
    }

    /// <summary>
    /// Checks whether inventory should be unloaded now.
    /// For recipe work, uses a lower threshold to allow batch processing.
    /// </summary>
    /// <param name="pawn">Pawn to check.</param>
    /// <param name="workType">Current work type for priority check.</param>
    /// <param name="isRecipeWork">If true, uses recipe threshold instead of forcing unload.</param>
    public static void CheckAndUnload(Pawn pawn, WorkTypeDef? workType, bool isRecipeWork = false)
    {
        if (pawn?.Map == null) return;

        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null || comp.GetHashSet().Count == 0) return;

        if (isRecipeWork)
        {
            // WHY: For recipes (butcher), only unload when inventory is getting heavy.
            // This lets pawn process multiple carcasses before hauling.
            if (IsInventoryAboveThreshold(pawn, Settings.RecipeUnloadThreshold))
                ExecuteHaulOrDrop(pawn, ShouldHaulToStorage(pawn, workType));
        }
        else
        {
            // Non-recipe work: unload when full
            if (IsInventoryNearlyFull(pawn))
                ExecuteHaulOrDrop(pawn, ShouldHaulToStorage(pawn, workType));
        }
    }

    /// <summary>
    /// Checks if pawn inventory is nearly full (90%+ capacity).
    /// </summary>
    public static bool IsInventoryNearlyFull(Pawn pawn)
    {
        var capacity = MassUtility.Capacity(pawn);
        if (capacity <= 0f) return true;
        return (MassUtility.GearMass(pawn) + MassUtility.InventoryMass(pawn)) / capacity >= 0.9f;
    }

    /// <summary>
    /// Checks if pawn inventory exceeds a given threshold.
    /// </summary>
    /// <param name="pawn">Pawn to check.</param>
    /// <param name="threshold">Fraction of capacity (0.0 - 1.0).</param>
    /// <returns>True if above threshold.</returns>
    public static bool IsInventoryAboveThreshold(Pawn pawn, float threshold)
    {
        var capacity = MassUtility.Capacity(pawn);
        if (capacity <= 0f) return true;
        return (MassUtility.GearMass(pawn) + MassUtility.InventoryMass(pawn)) / capacity >= threshold;
    }

    #endregion

    #region Area Scan Triggers

    /// <summary>
    /// Called after deconstruction completes.
    /// </summary>
    public static void OnDeconstructComplete(Pawn pawn, IntVec3 position)
    {
        if (_isProcessing || pawn?.Map == null) return;

        Log.Info($"=== OnDeconstructComplete: {pawn.LabelShort} at {position} ===");

        if (!Settings.HaulAfterDeconstruct) return;
        if (!CanPawnAutoHaul(pawn, out _)) return;

        var shouldGoToStorage = !Settings.DeconstructCheckPriority ||
                                ShouldHaulToStorage(pawn, WorkTypeDefOf.Construction);
        var hasMoreWork = HasJobsOfTypes(pawn, JobDefOf.Deconstruct, JobDefOf.Mine);

        var collected = CollectNearbyIntoInventory(pawn, position, FilterDeconstruct, "deconstruct");
        if (collected == 0) return;

        if (IsInventoryNearlyFull(pawn) || !hasMoreWork)
            ExecuteHaulOrDrop(pawn, shouldGoToStorage);
        else
            Log.Info($"  -> Continuing work, {collected} items in inventory");
    }

    /// <summary>
    /// Called after mining completes.
    /// </summary>
    public static void OnMiningComplete(Pawn pawn, IntVec3 position)
    {
        if (_isProcessing || pawn?.Map == null) return;

        Log.Info($"=== OnMiningComplete: {pawn.LabelShort} at {position} ===");

        if (!Settings.HaulAfterMining) return;
        if (!CanPawnAutoHaul(pawn, out _)) return;

        var shouldGoToStorage = !Settings.MiningCheckPriority ||
                                ShouldHaulToStorage(pawn, WorkTypeDefOf.Mining);
        var hasMoreWork = HasJobsOfTypes(pawn, JobDefOf.Mine);

        var collected = CollectNearbyIntoInventory(pawn, position, FilterMining, "mining");
        if (collected == 0) return;

        if (IsInventoryNearlyFull(pawn) || !hasMoreWork)
            ExecuteHaulOrDrop(pawn, shouldGoToStorage);
        else
            Log.Info($"  -> Continuing mining, {collected} items in inventory");
    }

    /// <summary>
    /// Notifies a nearby idle colonist to help haul remaining items.
    /// </summary>
    public static void NotifyNearbyPawns(Pawn hauler, IntVec3 position, Map? map)
    {
        if (!Settings.NotifyOtherPawns || _isProcessing || map == null) return;

        foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
        {
            if (pawn == hauler) continue;
            if (!CanPawnAutoHaul(pawn, out _)) continue;

            var curJob = pawn.CurJob?.def;
            if (curJob != null && curJob != JobDefOf.Wait_Wander && curJob != JobDefOf.GotoWander)
                continue;

            if ((pawn.Position - position).LengthHorizontalSquared > 900)
                continue;

            TryQueueHaulJob(pawn, position, "cooperation");
            return;
        }
    }

    #endregion

    #region Priority Logic

    /// <summary>
    /// Determines whether pawn should haul to storage (true) or drop at feet (false).
    /// </summary>
    public static bool ShouldHaulToStorage(Pawn pawn, WorkTypeDef? currentWorkType)
    {
        var haulingPrio = pawn.workSettings?.GetPriority(WorkTypeDefOf.Hauling) ?? 0;
        var workPrio = currentWorkType != null
            ? (pawn.workSettings?.GetPriority(currentWorkType) ?? 0)
            : 0;

        Log.Verbose($"  Priority: hauling={haulingPrio}, {currentWorkType?.defName}={workPrio}");

        if (haulingPrio == 0)
            return !Settings.DropWhenHaulingDisabled;

        if (Settings.SmartCleanup && workPrio > 0 && haulingPrio > workPrio)
            return false;

        return true;
    }

    /// <summary>
    /// Executes the haul decision.
    /// </summary>
    public static void ExecuteHaulOrDrop(Pawn pawn, bool shouldGoToStorage)
    {
        if (shouldGoToStorage)
        {
            Log.Info("  -> Queuing unload job");
            QueueUnloadJob(pawn);
        }
        else
        {
            Log.Info("  -> Dropping items at feet");
            DropAllHauledItems(pawn);
        }
    }

    #endregion

    #region Filters

    private static bool FilterDeconstruct(Thing thing)
    {
        if (thing is Corpse) return false;
        if (IsChunk(thing)) return false;

        return Settings.DeconstructMode switch
        {
            Settings.DeconstructHaulMode.All => true,
            Settings.DeconstructHaulMode.NoChunks => true,
            Settings.DeconstructHaulMode.Valuable => thing.MarketValue > 1f,
            _ => true
        };
    }

    private static bool FilterMining(Thing thing)
    {
        if (thing is Corpse) return false;
        if (IsChunk(thing)) return false;
        return true;
    }

    #endregion

    #region Collection

    private static int CollectNearbyIntoInventory(Pawn pawn, IntVec3 center, Func<Thing, bool> filter, string source)
    {
        _isProcessing = true;
        try
        {
            var things = FindHaulableThings(pawn, center, filter);
            Log.Info($"  Found {things.Count} things for {source}");
            if (things.Count == 0) return 0;

            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null) return 0;

            var inventory = pawn.inventory?.innerContainer;
            if (inventory == null) return 0;

            var collected = 0;

            foreach (var thing in things)
            {
                if (!thing.Spawned) continue;

                if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, thing.stackCount))
                {
                    var canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing);
                    if (canTake <= 0) continue;

                    var split = thing.SplitOff(canTake);
                    if (TryPickupThing(pawn, split, inventory, comp))
                    {
                        collected++;
                        Log.Verbose($"    {split.LabelShort} x{canTake}: partial pickup");
                    }
                    else
                    {
                        GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    }
                    break;
                }

                if (!pawn.CanReserve(thing)) continue;

                if (TryPickupThing(pawn, thing, inventory, comp))
                {
                    collected++;
                    Log.Verbose($"    {thing.LabelShort}: picked up");
                }
            }

            return collected;
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private static void DropAllHauledItems(Pawn pawn)
    {
        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null) return;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null) return;

        var trackedItems = comp.GetHashSet().ToList();
        if (trackedItems.Count == 0) return;

        var dropped = 0;
        foreach (var thing in trackedItems)
        {
            if (thing == null || thing.Destroyed) continue;
            if (!inventory.Contains(thing)) continue;

            if (inventory.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _))
                dropped++;
        }

        comp.ClearTracking();
        Log.Info($"  Dropped {dropped} items at {pawn.Position}");
    }

    private static void QueueUnloadJob(Pawn pawn)
    {
        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null || comp.GetHashSet().Count == 0) return;

        var job = JobMaker.MakeJob(SmartHaulJobDefOf.UnloadYourHauledInventory, pawn);
        if (job.TryMakePreToilReservations(pawn, false))
        {
            Log.Info($"  Queuing UnloadYourHauledInventory for {pawn.LabelShort}");
            pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
        }
    }

    private static void TryQueueHaulJob(Pawn pawn, IntVec3 center, string source)
    {
        _isProcessing = true;
        try
        {
            var things = FindHaulableThings(pawn, center, _ => true);
            if (things.Count == 0) return;

            var first = things[0];
            if (!StoreUtility.TryFindBestBetterStorageFor(first, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(first), pawn.Faction, out _, out _, true))
                return;

            var wg = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulToInventory")?.Worker as WorkGiver_HaulToInventory;
            var job = wg?.JobOnThing(pawn, first, false);
            if (job == null) return;

            Log.Info($"  Queuing {source} haul for {pawn.LabelShort}");
            pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    #endregion

    #region Helpers

    private static List<Thing> FindHaulableThings(Pawn pawn, IntVec3 center, Func<Thing, bool> filter)
    {
        var result = new List<Thing>(16);
        var map = pawn.Map;
        if (map == null) return result;

        foreach (var cell in GenRadial.RadialCellsAround(center, SEARCH_RADIUS, true))
        {
            if (!cell.InBounds(map)) continue;

            var thingList = cell.GetThingList(map);
            for (var i = 0; i < thingList.Count; i++)
            {
                var t = thingList[i];
                if (!t.Spawned || !t.def.EverHaulable) continue;
                if (t.IsForbidden(pawn) || !pawn.CanReserve(t)) continue;
                if (t.IsInValidBestStorage()) continue;
                if (!filter(t)) continue;
                result.Add(t);
            }
        }

        return result;
    }

    private static bool HasJobsOfTypes(Pawn pawn, params JobDef[] defs)
    {
        var queue = pawn.jobs?.jobQueue;
        if (queue == null) return false;

        foreach (var qj in queue)
        {
            if (qj.job?.def != null && defs.Contains(qj.job.def))
                return true;
        }
        return false;
    }

    public static bool IsChunk(Thing thing) =>
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.Chunks) == true ||
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.StoneChunks) == true;

    /// <summary>
    /// Validates that pawn can perform auto-hauling.
    /// Uses IsColonistPlayerControlled for MP compatibility —
    /// Faction.OfPlayer only returns the local player's faction.
    /// </summary>
    private static bool CanPawnAutoHaul(Pawn pawn, out string reason)
    {
        reason = "";
        if (pawn.Map == null || pawn.Dead || pawn.Downed) { reason = "invalid state"; return false; }
        // WHY: In MP, Faction.OfPlayer is the LOCAL player's faction only.
        // pawn.Faction.IsPlayer covers all human player factions.
        if (pawn.Faction == null || !pawn.Faction.IsPlayer) { reason = "not player faction"; return false; }
        if (!Settings.IsAllowedRace(pawn.RaceProps)) { reason = "race not allowed"; return false; }
        if (pawn.GetComp<CompHauledToInventory>() == null) { reason = "no comp"; return false; }
        return true;
    }

    #endregion
}