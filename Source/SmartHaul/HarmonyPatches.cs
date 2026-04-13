using HarmonyLib;

namespace SmartHaul;

/// <summary>
/// Harmony patches for automatic item collection.
/// </summary>
[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    static HarmonyPatches()
    {
        var harmony = new Harmony("paralax034.smartHaul");

        // Mining
        harmony.Patch(
            AccessTools.Method(typeof(Mineable), nameof(Mineable.DestroyMined)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Mineable_DestroyMined_Postfix)));

        // Harvest
        harmony.Patch(
            AccessTools.Method(typeof(Plant), nameof(Plant.PlantCollected)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Plant_PlantCollected_Postfix)));

        // Deconstruct
        harmony.Patch(
            AccessTools.Method(typeof(Building), nameof(Building.Destroy)),
            prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Building_Destroy_Prefix)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Building_Destroy_Postfix)));

        // Floor removal
        harmony.Patch(
            AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.RemoveTopLayer)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(TerrainGrid_RemoveTopLayer_Postfix)));

        // Bill completion (butchering, crafting, cooking)
        harmony.Patch(
            AccessTools.Method(typeof(Toils_Recipe), nameof(Toils_Recipe.FinishRecipeAndStartStoringProduct)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(FinishRecipe_Postfix)));

        // Job starting - detect work transitions
        harmony.Patch(
            AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
            prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(StartJob_Prefix)));

        // Draft - drop items when drafted
        harmony.Patch(
            AccessTools.PropertySetter(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Drafted_Postfix)));

        // Game load - clear tracking
        harmony.Patch(
            AccessTools.Method(typeof(Game), nameof(Game.LoadGame)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Game_LoadGame_Postfix)));

#if DEBUG
        Verse.Log.Message($"[SmartHaul] v{SmartHaulMod.Version} DEBUG loaded");
#else
        Verse.Log.Message($"[SmartHaul] v{SmartHaulMod.Version} loaded");
#endif
    }

    #region Mining

    public static void Mineable_DestroyMined_Postfix(Mineable __instance, Pawn pawn)
    {
        if (!Settings.EnableForMining) return;
        if (pawn == null || !pawn.IsColonistPlayerControlled) return;

        InventoryCollector.CollectNearbyItems(pawn, __instance.Position);
        InventoryCollector.OnWorkDone(pawn, WorkCategory.Extraction);
    }

    #endregion

    #region Harvesting

    public static void Plant_PlantCollected_Postfix(Plant __instance, Pawn by)
    {
        if (!Settings.EnableForHarvest) return;
        if (by == null || !by.IsColonistPlayerControlled) return;

        InventoryCollector.CollectNearbyItems(by, __instance.Position);
        InventoryCollector.OnWorkDone(
            by,
            WorkCategory.Farming,
            forceHandle: Settings.ImmediateHaulAfterFarming);
    }

    #endregion

    #region Deconstruction

    private struct DeconstructContext
    {
        public Pawn? Worker;
        public IntVec3 Position;
        public bool Valid;
    }

    [ThreadStatic]
    private static DeconstructContext _deconstructCtx;

    public static void Building_Destroy_Prefix(Building __instance, DestroyMode mode)
    {
        _deconstructCtx = default;

        if (mode != DestroyMode.Deconstruct) return;
        if (!Settings.EnableForDeconstruct) return;
        if (__instance?.Map == null) return;

        Pawn? worker = null;
        foreach (var pawn in __instance.Map.mapPawns.FreeColonistsSpawned)
        {
            var job = pawn.CurJob;
            if (job?.def == JobDefOf.Deconstruct && job.targetA.Thing == __instance)
            {
                worker = pawn;
                break;
            }
        }

        if (worker == null) return;

        _deconstructCtx = new DeconstructContext
        {
            Worker = worker,
            Position = __instance.Position,
            Valid = true
        };
    }

    public static void Building_Destroy_Postfix()
    {
        if (!_deconstructCtx.Valid) return;

        var worker = _deconstructCtx.Worker;
        var pos = _deconstructCtx.Position;
        _deconstructCtx = default;

        if (worker?.Map == null) return;

        InventoryCollector.CollectNearbyItems(worker, pos);
        InventoryCollector.OnWorkDone(worker, WorkCategory.Extraction);
    }

    #endregion

    #region Floor Removal

    public static void TerrainGrid_RemoveTopLayer_Postfix(TerrainGrid __instance, IntVec3 c)
    {
        if (!Settings.EnableForFloorRemoval) return;

        var map = __instance.map;
        if (map == null) return;

        Pawn? worker = null;
        foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
        {
            var job = pawn.CurJob;
            if (job?.def == JobDefOf.RemoveFloor && job.targetA.Cell == c)
            {
                worker = pawn;
                break;
            }
        }

        if (worker == null) return;

        InventoryCollector.CollectNearbyItems(worker, c);
        InventoryCollector.OnWorkDone(worker, WorkCategory.Extraction);
    }

    #endregion

    #region Bill Completion

    public static void FinishRecipe_Postfix(Toil __result)
    {
        if (__result == null) return;

        var originalInit = __result.initAction;
        __result.initAction = () =>
        {
            var pawn = __result.actor;
            if (pawn == null || !pawn.IsColonistPlayerControlled || !Settings.EnableForCrafting)
            {
                originalInit?.Invoke();
                return;
            }

            // WHY: Save before vanilla initAction — vanilla ends DoBill and starts
            // HaulToCellStorage, changing CurJob and invalidating targetA.Thing.Position
            var workbenchPos = pawn.CurJob?.targetA.Thing?.Position ?? pawn.Position;
            var originalJobDef = pawn.CurJob?.def;

            try
            {
                originalInit?.Invoke();
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[SmartHaul] FinishRecipe original error: {ex.Message}");
                return;
            }

            try
            {
                var carried = pawn.carryTracker?.CarriedThing;
                if (carried != null)
                    pawn.carryTracker!.TryDropCarriedThing(workbenchPos, ThingPlaceMode.Near, out _);

                if (pawn.CurJobDef == JobDefOf.HaulToCell)
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);

                InventoryCollector.CollectNearbyItems(pawn, workbenchPos);
                InventoryCollector.OnWorkDone(
                    pawn,
                    WorkCategory.Crafting,
                    explicitJobDef: originalJobDef,
                    forceHandle: Settings.ImmediateHaulAfterCrafting);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[SmartHaul] FinishRecipe collect error: {ex.Message}");
            }
        };
    }

    #endregion

    #region Draft

    /// <summary>
    /// Drop all collected items when pawn is drafted (R key).
    /// </summary>
    public static void Drafted_Postfix(Pawn_DraftController __instance, bool value)
    {
        // WHY: Only trigger on draft ON, not on undraft
        if (!value) return;

        var pawn = __instance.pawn;
        if (pawn == null || !pawn.IsColonistPlayerControlled) return;

        if (!InventoryCollector.HasCollectedItems(pawn)) return;

        Log.Info($"Draft: {pawn.LabelShort} dropping items");
        InventoryCollector.DropAllCollectedItems(pawn);
        InventoryCollector.ClearPawnTracking(pawn);
    }

    #endregion

    #region Job Transitions

    public static void StartJob_Prefix(Pawn_JobTracker __instance, Job newJob)
    {
        var pawn = __instance.pawn;
        if (pawn == null) return;

        InventoryCollector.OnJobStarting(pawn, newJob?.def);
    }

    #endregion

    #region Game Load

    public static void Game_LoadGame_Postfix()
    {
        InventoryCollector.ClearTracking();
    }

    #endregion
}