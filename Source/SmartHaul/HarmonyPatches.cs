using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SmartHaul;

/// <summary>
/// All Harmony patches for SmartHaul mod.
/// </summary>
[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    #region Static Fields

    private static readonly HashSet<int> _expandedJobs = [];
    private static Func<Pawn, Thing, bool, Job>? _haulToInventoryJob;

    // Butcher product cache
    private static readonly Dictionary<int, List<Thing>> _butcherProducts = [];

    // Pending delayed triggers
    private static readonly Dictionary<int, (Pawn pawn, IntVec3 pos, int tick)> _pendingDeconstructs = [];
    private static readonly Dictionary<int, (Pawn pawn, IntVec3 pos, int tick)> _pendingMining = [];

    // Haul overlay cache
    private static readonly Dictionary<Thing, Pawn> _haulCache = new(64);
    private static int _lastCacheTick = -1;

    private static WorkTypeDef? _cookingWorkType;
    private static WorkTypeDef? CookingWorkType =>
        _cookingWorkType ??= DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");

    #endregion

    #region Initialization

    static HarmonyPatches()
    {
        var harmony = new Harmony("paralax034.rimworld.smartHaul.main");

#if DEBUG
        Harmony.DEBUG = true;
        Log.TraceLevel = 4;
        Log.Info("DEBUG build - Harmony.DEBUG enabled");
#endif

        PatchCoreSystems(harmony);
        PatchAutoHaulTriggers(harmony);
        PatchOverlay(harmony);

        Log.Info("[SmartHaul] v1.1.1 loaded!");
    }

    private static void PatchCoreSystems(Harmony harmony)
    {
        if (!ModCompatibility.CombatExtendedIsActive)
        {
            harmony.Patch(
                AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.GetMaxAllowedToPickUp),
                    [typeof(Pawn), typeof(ThingDef)]),
                prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(MaxAllowedToPickUpPrefix)));

            harmony.Patch(
                AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.CanPickUp)),
                prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CanBeMadeToDropStuff)));
        }

        harmony.Patch(
            AccessTools.Method(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.TryGiveJob)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(DropUnusedInventory_PostFix)));

        harmony.Patch(
            AccessTools.Method(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.MakeNewToils)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(JobDriver_HaulToCell_PostFix)));

        harmony.Patch(
            AccessTools.Method(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Pawn_InventoryTracker_PostFix)));

        harmony.Patch(
            AccessTools.Method(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.Drop)),
            prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Drop_Prefix)));

        harmony.Patch(
            AccessTools.Method(typeof(JobGiver_Idle), nameof(JobGiver_Idle.TryGiveJob)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(IdleJoy_Postfix)));

        harmony.Patch(
            AccessTools.Method(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.DrawThingRow)),
            transpiler: new HarmonyMethod(typeof(HarmonyPatches), nameof(GearTabHighlightTranspiler)));

        harmony.Patch(
            AccessTools.Method(typeof(WorkGiver_Haul), nameof(WorkGiver_Haul.ShouldSkip)),
            prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(SkipCorpses_Prefix)));

        harmony.Patch(
            AccessTools.Method(typeof(JobGiver_Haul), nameof(JobGiver_Haul.TryGiveJob)),
            transpiler: new HarmonyMethod(typeof(HarmonyPatches), nameof(JobGiver_Haul_TryGiveJob_Transpiler)));

        harmony.Patch(
            AccessTools.Method(typeof(Game), nameof(Game.LoadGame)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Game_LoadGame_Postfix)));
    }

    private static void PatchAutoHaulTriggers(Harmony harmony)
    {
        TryPatch(harmony, typeof(Building), nameof(Building.Destroy),
            prefix: nameof(Building_Destroy_Prefix), label: "Building.Destroy");

        TryPatch(harmony, typeof(GenRecipe), "PostProcessProduct",
            postfix: nameof(GenRecipe_PostProcessProduct_Postfix), label: "GenRecipe.PostProcessProduct");

        TryPatch(harmony, typeof(Toils_Recipe), "FinishRecipeAndStartStoringProduct",
            postfix: nameof(FinishRecipe_Postfix), label: "Toils_Recipe.FinishRecipeAndStartStoringProduct");

        TryPatch(harmony, typeof(Mineable), nameof(Mineable.DestroyMined),
            prefix: nameof(Mineable_DestroyMined_Prefix), label: "Mineable.DestroyMined");

        TryPatch(harmony, typeof(JobDriver_AffectFloor), "MakeNewToils",
            postfix: nameof(AffectFloor_MakeNewToils_Postfix), label: "JobDriver_AffectFloor.MakeNewToils");

        TryPatch(harmony, typeof(Plant), nameof(Plant.PlantCollected),
            postfix: nameof(Plant_PlantCollected_Postfix), label: "Plant.PlantCollected");

        TryPatch(harmony, typeof(JobDriver_PlantWork), "MakeNewToils",
            postfix: nameof(PlantWork_MakeNewToils_Postfix), label: "JobDriver_PlantWork.MakeNewToils");

        harmony.Patch(
            AccessTools.Method(typeof(TickManager), nameof(TickManager.DoSingleTick)),
            postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(TickManager_DoSingleTick_Postfix)));
    }

    private static void PatchOverlay(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(GenMapUI), nameof(GenMapUI.DrawThingLabel),
                [typeof(Thing), typeof(string), typeof(Color)]),
            prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(DrawThingLabel_Prefix)));

        TryPatch(harmony, typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.DrawLinesBetweenTargets),
            postfix: nameof(DrawLinesBetweenTargets_Postfix),
            label: "Pawn_JobTracker.DrawLinesBetweenTargets");
    }

    private static void TryPatch(Harmony harmony, Type type, string methodName,
        string? prefix = null, string? postfix = null, string label = "")
    {
        try
        {
            var method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                Verse.Log.Warning($"[SmartHaul] Method not found: {label}");
                return;
            }

            harmony.Patch(method,
                prefix: prefix != null ? new HarmonyMethod(typeof(HarmonyPatches), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(HarmonyPatches), postfix) : null);

            Verse.Log.Message($"[SmartHaul] Patched {label}");
        }
        catch (Exception e)
        {
            Verse.Log.Warning($"[SmartHaul] Failed to patch {label}: {e.Message}");
        }
    }

    #endregion

    #region Tick Processing

    /// <summary>
    /// Process pending triggers and cleanup protections.
    /// </summary>
    public static void TickManager_DoSingleTick_Postfix()
    {
        var tick = Find.TickManager.TicksGame;
        const int DELAY = 3;

        // Process pending deconstructs/mining
        ProcessPending(_pendingDeconstructs, tick, DELAY, AutoHaulTriggers.OnDeconstructComplete);
        ProcessPending(_pendingMining, tick, DELAY, AutoHaulTriggers.OnMiningComplete);

        // Cleanup protections
        ThingProtection.Tick();
    }

    private static void ProcessPending(
        Dictionary<int, (Pawn pawn, IntVec3 pos, int tick)> pending,
        int currentTick, int delay,
        Action<Pawn, IntVec3> callback)
    {
        if (pending.Count == 0) return;

        var toRemove = new List<int>();
        foreach (var kvp in pending)
        {
            if (currentTick - kvp.Value.tick < delay) continue;

            // Protect items at spawn location
            ThingProtection.ProtectRadius(kvp.Value.pos, kvp.Value.pawn.Map);

            callback(kvp.Value.pawn, kvp.Value.pos);
            toRemove.Add(kvp.Key);
        }
        foreach (var key in toRemove) pending.Remove(key);
    }

    #endregion

    #region Deconstruct

    public static void Building_Destroy_Prefix(Building __instance, DestroyMode mode)
    {
        if (mode != DestroyMode.Deconstruct) return;
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

        Log.Info($"[Deconstruct] Building: {__instance.LabelShort} at {__instance.Position}");
        ThingProtection.ProtectRadius(__instance.Position, __instance.Map);
        _pendingDeconstructs[__instance.thingIDNumber] = (worker, __instance.Position, Find.TickManager.TicksGame);
    }

    #endregion

    #region Mining

    public static void Mineable_DestroyMined_Prefix(Mineable __instance, Pawn pawn)
    {
        if (pawn == null || !pawn.IsColonistPlayerControlled) return;
        if (__instance?.Map == null) return;
        if (!Settings.HaulAfterMining) return;

        Log.Info($"[Mining] Rock: {__instance.LabelShort} at {__instance.Position}");
        ThingProtection.ProtectRadius(__instance.Position, __instance.Map);
        _pendingMining[__instance.thingIDNumber] = (pawn, __instance.Position, Find.TickManager.TicksGame);
    }

    #endregion

    #region Butcher/Crafting

    public static void GenRecipe_PostProcessProduct_Postfix(Thing __result, RecipeDef recipeDef, Pawn worker)
    {
        if (worker == null || recipeDef == null || __result == null) return;
        if (!worker.IsColonistPlayerControlled) return;
        if (!Settings.HaulAfterButcher) return;

        var isButcher = recipeDef.specialProducts?.Contains(SpecialProductType.Butchery) == true
                     || (recipeDef.defName?.ToLower().Contains("butcher") ?? false)
                     || (recipeDef.defName?.ToLower().Contains("smash") ?? false);

        if (!isButcher) return;

        Log.Info($"[Butcher] Product: {__result.LabelShort} by {worker.LabelShort}");

        var pawnId = worker.thingIDNumber;
        if (!_butcherProducts.ContainsKey(pawnId))
            _butcherProducts[pawnId] = [];

        _butcherProducts[pawnId].Add(__result);
        ThingProtection.Protect(__result);
    }

    public static void FinishRecipe_Postfix(Toil __result)
    {
        if (__result == null) return;

        var originalInit = __result.initAction;
        __result.initAction = () =>
        {
            originalInit?.Invoke();

            var pawn = __result.actor;
            if (pawn == null || !pawn.IsColonistPlayerControlled) return;

            var pawnId = pawn.thingIDNumber;
            if (!_butcherProducts.TryGetValue(pawnId, out var products) || products.Count == 0)
                return;

            Log.Info($"[Butcher] Recipe finished, processing {products.Count} products");

            var workType = CookingWorkType ?? WorkTypeDefOf.Crafting;
            var effectiveWorkType = Settings.ButcherCheckPriority ? workType : null;

            foreach (var product in products.ToList())
            {
                if (product == null || product.Destroyed)
                {
                    ThingProtection.Unprotect(product);
                    continue;
                }

                ThingProtection.Unprotect(product);

                if (pawn.carryTracker?.CarriedThing == product)
                {
                    if (pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out var dropped))
                        AutoHaulTriggers.TryDirectCollect(pawn, dropped, effectiveWorkType, isRecipeProduct: true);
                }
                else if (product.Spawned)
                {
                    AutoHaulTriggers.TryDirectCollect(pawn, product, effectiveWorkType, isRecipeProduct: true);
                }
            }

            _butcherProducts.Remove(pawnId);

            if (pawn.CurJobDef == JobDefOf.HaulToCell)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);

            // WHY: isRecipeWork=true means "only unload if heavy enough"
            // This lets pawn batch-process multiple carcasses
            AutoHaulTriggers.CheckAndUnload(pawn, effectiveWorkType, isRecipeWork: true);
        };
    }

    /// <summary>
    /// Clears caches when loading a game.
    /// </summary>
    public static void Game_LoadGame_Postfix()
    {
        JobDriver_UnloadYourHauledInventory.ClearCache();
        ThingProtection.Reset();
    }

    #endregion

    #region Harvest

    public static void PlantWork_MakeNewToils_Postfix(JobDriver __instance, ref IEnumerable<Toil> __result)
    {
        if (!IsHarvestJob(__instance.job?.def)) return;

        var driver = __instance;
        var pawn = driver.pawn;
        var job = driver.job;

        if (pawn != null && job != null && pawn.IsColonistPlayerControlled && Settings.HaulAfterHarvest)
        {
            var jobId = job.loadID;
            if (_expandedJobs.Add(jobId))
            {
                ExpandHarvestRoute(pawn, job);

                if (_expandedJobs.Count > 200)
                    _expandedJobs.Clear();
            }
        }

        __result = WrapToilsWithFinishAction(driver, __result);
    }

    public static void Plant_PlantCollected_Postfix(Plant __instance, Pawn by)
    {
        if (by == null || !by.IsColonistPlayerControlled) return;
        if (!Settings.HaulAfterHarvest) return;
        if (__instance?.def?.plant?.harvestedThingDef == null) return;

        var harvestDef = __instance.def.plant.harvestedThingDef;
        var workType = Settings.HarvestCheckPriority ? WorkTypeDefOf.Growing : null;

        var map = by.Map;
        if (map == null) return;

        var collected = 0;
        foreach (var cell in GenRadial.RadialCellsAround(by.Position, 3f, true))
        {
            if (!cell.InBounds(map)) continue;
            var thingList = cell.GetThingList(map);
            for (var i = thingList.Count - 1; i >= 0; i--)
            {
                var thing = thingList[i];
                if (!thing.Spawned || thing.def != harvestDef || thing.IsForbidden(by)) continue;

                ThingProtection.Protect(thing);

                if (AutoHaulTriggers.TryDirectCollect(by, thing, workType))
                    collected++;
            }
        }

        if (AutoHaulTriggers.IsInventoryNearlyFull(by))
        {
            var curJob = by.CurJob;
            var hasMore = (curJob?.targetQueueA?.Count ?? 0) > 0 || HasMoreHarvestJobsQueued(by);
            if (!hasMore)
                AutoHaulTriggers.ExecuteHaulOrDrop(by, AutoHaulTriggers.ShouldHaulToStorage(by, workType));
        }
    }

    private static void ExpandHarvestRoute(Pawn pawn, Job job)
    {
        var map = pawn.Map;
        if (map == null) return;

        var queue = job.targetQueueA;
        if (queue == null || queue.Count == 0) return;

        var firstCell = queue[0].Cell;
        var room = firstCell.GetRoom(map);
        var zone = firstCell.GetZone(map) as Zone_Growing;
        ThingDef? wantedDef = zone != null ? WorkGiver_Grower.CalculateWantedPlantDef(firstCell, map) : null;

        var existing = new HashSet<Thing>();
        foreach (var t in queue)
            if (t.HasThing) existing.Add(t.Thing);
        if (job.targetA.HasThing) existing.Add(job.targetA.Thing);

        var availableMass = GetAvailableCarryMass(pawn);
        var currentMass = 0f;
        foreach (var t in queue)
            if (t.Thing is Plant p) currentMass += EstimatePlantHarvestMass(p);
        if (job.targetA.Thing is Plant pa) currentMass += EstimatePlantHarvestMass(pa);

        var maxDist = Settings.MaxNeighborDistance;
        var maxDistSq = maxDist * maxDist;
        var maxPlants = Settings.MaxCandidates;
        var vanillaCount = queue.Count;

        var candidates = new List<Plant>(64);
        var searchCenter = queue[0].Cell;

        foreach (var cell in GenRadial.RadialCellsAround(searchCenter, maxDist * 1.5f, true))
        {
            if (!cell.InBounds(map)) continue;
            if (room != null && cell.GetRoom(map) != room) continue;

            var plant = cell.GetPlant(map);
            if (plant == null || existing.Contains(plant)) continue;
            if (!IsValidHarvestTarget(plant, pawn, wantedDef)) continue;

            candidates.Add(plant);
        }

        if (candidates.Count == 0) return;

        var current = queue[^1].Cell;
        var added = 0;

        while (candidates.Count > 0 && queue.Count < maxPlants)
        {
            var bestIdx = -1;
            var bestDistSq = int.MaxValue;

            for (var i = 0; i < candidates.Count; i++)
            {
                var distSq = (candidates[i].Position - current).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestDistSq > maxDistSq) break;

            var best = candidates[bestIdx];
            var plantMass = EstimatePlantHarvestMass(best);

            if (currentMass + plantMass > availableMass)
            {
                candidates.RemoveAt(bestIdx);
                continue;
            }

            if (!pawn.Reserve(best, job, 1, -1, null, false))
            {
                candidates.RemoveAt(bestIdx);
                continue;
            }

            queue.Add(best);
            existing.Add(best);
            currentMass += plantMass;
            current = best.Position;
            added++;

            candidates.RemoveAt(bestIdx);
        }

        if (added > 0)
            Log.Info($"[SmartHarvest] Expanded route: {vanillaCount} -> {queue.Count} plants, mass={currentMass:F1}/{availableMass:F1}");
    }

    private static bool IsHarvestJob(JobDef? def) =>
        def == JobDefOf.Harvest || def == JobDefOf.HarvestDesignated;

    private static bool IsValidHarvestTarget(Plant plant, Pawn pawn, ThingDef? wantedDef)
    {
        if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature) return false;
        if (!plant.CanYieldNow()) return false;
        if (plant.IsForbidden(pawn)) return false;
        if (!pawn.CanReserve(plant, 1, -1, null, false)) return false;
        if (wantedDef != null && plant.def != wantedDef) return false;
        if (!plant.def.plant.autoHarvestable) return false;
        if (plant.TryGetComp(out CompPlantPreventCutting comp) && comp.PreventCutting) return false;
        if (!PlantUtility.PawnWillingToCutPlant_Job(plant, pawn)) return false;
        return true;
    }

    private static float EstimatePlantHarvestMass(Plant? plant)
    {
        if (plant == null) return 0f;
        var hd = plant.def.plant?.harvestedThingDef;
        if (hd == null) return 0f;
        return plant.YieldNow() * hd.GetStatValueAbstract(StatDefOf.Mass);
    }

    private static float GetAvailableCarryMass(Pawn pawn)
    {
        var cap = MassUtility.Capacity(pawn);
        var cur = MassUtility.GearMass(pawn) + MassUtility.InventoryMass(pawn);
        return Math.Max(0f, cap - cur);
    }

    private static IEnumerable<Toil> WrapToilsWithFinishAction(JobDriver driver, IEnumerable<Toil> toils)
    {
        var isFirst = true;
        foreach (var toil in toils)
        {
            if (isFirst)
            {
                isFirst = false;
                toil.AddFinishAction(() =>
                {
                    var pawn = driver.pawn;
                    if (pawn == null || !pawn.IsColonistPlayerControlled) return;

                    var comp = pawn.GetComp<CompHauledToInventory>();
                    if (comp == null || comp.GetHashSet().Count == 0) return;

                    var curJob = pawn.CurJob;
                    var hasMoreInThisJob = curJob?.targetQueueA != null && curJob.targetQueueA.Count > 0;
                    var hasMoreOtherJobs = HasMoreHarvestJobsQueued(pawn);

                    if (hasMoreInThisJob || hasMoreOtherJobs) return;

                    Log.Info($"[Harvest] All done for {pawn.LabelShort}, flushing inventory");
                    var workType = Settings.HarvestCheckPriority ? WorkTypeDefOf.Growing : null;
                    AutoHaulTriggers.ExecuteHaulOrDrop(pawn, AutoHaulTriggers.ShouldHaulToStorage(pawn, workType));
                });
            }
            yield return toil;
        }
    }

    private static bool HasMoreHarvestJobsQueued(Pawn pawn)
    {
        var queue = pawn.jobs?.jobQueue;
        if (queue == null) return false;
        foreach (var qj in queue)
            if (IsHarvestJob(qj.job?.def)) return true;
        return false;
    }

    #endregion

    #region Floor Removal

    public static void AffectFloor_MakeNewToils_Postfix(JobDriver __instance, ref IEnumerable<Toil> __result)
    {
        if (__instance is not JobDriver_RemoveFloor) return;

        var driver = __instance;
        var toils = __result.ToList();

        toils.Add(new Toil
        {
            initAction = () =>
            {
                var pawn = driver.pawn;
                if (pawn == null || !pawn.IsColonistPlayerControlled) return;

                var pos = driver.job?.targetA.Cell ?? pawn.Position;
                ThingProtection.ProtectRadius(pos, pawn.Map);
                AutoHaulTriggers.OnDeconstructComplete(pawn, pos);
            },
            defaultCompleteMode = ToilCompleteMode.Instant
        });

        __result = toils;
    }

    #endregion

    #region Overlay

    public static void DrawThingLabel_Prefix(Thing thing, ref string text)
    {
        if (!Settings.ShowHaulOverlay || thing == null) return;

        UpdateHaulCache(thing.Map);

        if (_haulCache.TryGetValue(thing, out var hauler) && hauler != null && Settings.ShowPawnName)
        {
            var name = hauler.LabelShort;
            var max = Settings.PawnNameMaxChars;
            if (name.Length > max) name = name[..(max - 2)] + "..";
            text = text + "\n" + name;
        }
    }

    public static void DrawLinesBetweenTargets_Postfix(Pawn_JobTracker __instance)
    {
        var pawn = __instance.pawn;
        if (pawn?.Map == null || !pawn.IsColonistPlayerControlled || !pawn.Spawned) return;

        DrawQueueLines(pawn, __instance.curJob, true);

        foreach (var qj in __instance.jobQueue)
            DrawQueueLines(pawn, qj.job, false);
    }

    private static void DrawQueueLines(Pawn pawn, Job? job, bool isCurrent)
    {
        if (job == null) return;

        var queue = job.targetQueueA;
        if (queue == null || queue.Count == 0) return;

        if (job.def != SmartHaulJobDefOf.HaulToInventory
            && job.def != JobDefOf.Harvest
            && job.def != JobDefOf.HarvestDesignated)
            return;

        Vector3 current;

        if (isCurrent)
        {
            if (pawn.pather?.curPath != null)
                current = pawn.pather.Destination.CenterVector3;
            else if (job.targetA.IsValid && job.targetA.HasThing
                     && job.targetA.Thing.Spawned && job.targetA.Thing.Map == pawn.Map)
                current = job.targetA.CenterVector3;
            else
                current = pawn.Position.ToVector3Shifted();
        }
        else
        {
            var first = queue[0];
            if (!first.IsValid) return;
            if (first.HasThing && (!first.Thing.Spawned || first.Thing.Map != pawn.Map)) return;
            current = first.CenterVector3;
        }

        var startIdx = isCurrent ? 0 : 1;

        for (var i = startIdx; i < queue.Count; i++)
        {
            var target = queue[i];
            if (!target.IsValid) continue;
            if (target.HasThing && (!target.Thing.Spawned || target.Thing.Map != pawn.Map)) continue;

            var next = target.CenterVector3;
            GenDraw.DrawLineBetween(current, next, AltitudeLayer.Item.AltitudeFor());
            current = next;
        }

        if (job.def == SmartHaulJobDefOf.HaulToInventory && job.targetB.IsValid)
        {
            Vector3 storePos;
            if (job.targetB.HasThing && job.targetB.Thing.Spawned)
                storePos = job.targetB.Thing.DrawPos;
            else
                storePos = job.targetB.Cell.ToVector3Shifted();

            GenDraw.DrawLineBetween(current, storePos, AltitudeLayer.Item.AltitudeFor());
        }
    }

    private static void UpdateHaulCache(Map? map)
    {
        var tick = Find.TickManager.TicksGame;
        if (tick == _lastCacheTick || map == null) return;

        _lastCacheTick = tick;
        _haulCache.Clear();

        foreach (var pawn in map.mapPawns.AllPawnsSpawned)
        {
            if (!Settings.OverlayAllFactions && pawn.Faction != Faction.OfPlayer) continue;

            var job = pawn.CurJob;
            if (job?.def != SmartHaulJobDefOf.HaulToInventory) continue;

            if (job.targetA.Thing is { Spawned: true } t)
                _haulCache.TryAdd(t, pawn);

            if (job.targetQueueA == null) continue;
            foreach (var target in job.targetQueueA)
            {
                if (target.Thing is { Spawned: true } qt)
                    _haulCache.TryAdd(qt, pawn);
            }
        }
    }

    #endregion

    #region Core Patches

    private static bool Drop_Prefix(Pawn pawn, Thing thing)
    {
        var comp = pawn.GetComp<CompHauledToInventory>();
        return comp == null || !comp.GetHashSet().Contains(thing);
    }

    private static void Pawn_InventoryTracker_PostFix(Pawn_InventoryTracker __instance, Thing item) =>
        __instance.pawn?.GetComp<CompHauledToInventory>()?.GetHashSet().Remove(item);

    private static void JobDriver_HaulToCell_PostFix(JobDriver_HaulToCell __instance)
    {
        var pawn = __instance.pawn;
        var comp = pawn?.GetComp<CompHauledToInventory>();
        if (comp == null) return;

        if (__instance.job.haulMode == HaulMode.ToCellStorage
            && pawn!.Faction != null && pawn.Faction.IsPlayer
            && Settings.IsAllowedRace(pawn.RaceProps)
            && (Settings.AllowCorpses || pawn.carryTracker.CarriedThing is not Corpse)
            && comp.GetHashSet().Count > 0)
        {
            PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn, true);
        }
    }

    public static void IdleJoy_Postfix(Pawn pawn) =>
        PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn, true);

    public static void DropUnusedInventory_PostFix(Pawn pawn) =>
        PawnUnloadChecker.CheckIfPawnShouldUnloadInventory(pawn);

    public static bool MaxAllowedToPickUpPrefix(Pawn pawn, ref int __result)
    {
        __result = int.MaxValue;
        return pawn.IsQuestLodger();
    }

    public static bool CanBeMadeToDropStuff(Pawn pawn, ref bool __result)
    {
        __result = !pawn.IsQuestLodger();
        return false;
    }

    public static bool SkipCorpses_Prefix(WorkGiver_Haul __instance, ref bool __result, Pawn pawn)
    {
        if (__instance is not WorkGiver_HaulCorpses) return true;
        if (Settings.AllowCorpses || pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count < 1)
        {
            __result = true;
            return false;
        }
        return true;
    }

    public static IEnumerable<CodeInstruction> JobGiver_Haul_TryGiveJob_Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob));
        var replacement = AccessTools.Method(typeof(HarmonyPatches), nameof(HaulToStorageJobByRace));
        return instructions.MethodReplacer(original, replacement);
    }

    public static Job HaulToStorageJobByRace(Pawn p, Thing t, bool forced) =>
        Settings.IsAllowedRace(p.RaceProps)
            ? GetHaulToInventoryJob(p, t, forced)
            : HaulAIUtility.HaulToStorageJob(p, t, forced);

    private static Job GetHaulToInventoryJob(Pawn p, Thing t, bool forced)
    {
        _haulToInventoryJob ??=
            ((WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("HaulToInventory").Worker).JobOnThing;
        return _haulToInventoryJob(p, t, forced);
    }

    public static IEnumerable<CodeInstruction> GearTabHighlightTranspiler(
        IEnumerable<CodeInstruction> instructions, MethodBase method)
    {
        var colorWhite = AccessTools.PropertyGetter(typeof(Color), nameof(Color.white));
        var done = false;

        foreach (var i in instructions)
        {
            if (!done && i.Calls(colorWhite))
            {
                yield return FishTranspiler.This;
                yield return FishTranspiler.CallPropertyGetter(typeof(ITab_Pawn_Gear),
                    nameof(ITab_Pawn_Gear.SelPawnForGear));
                yield return FishTranspiler.Argument(method, "thing");
                yield return FishTranspiler.Call(GetColorForHauled);
                done = true;
            }
            else yield return i;
        }
    }

    private static Color GetColorForHauled(Pawn pawn, Thing thing) =>
        pawn.GetComp<CompHauledToInventory>()?.GetHashSet().Contains(thing) == true
            ? Color.Lerp(Color.grey, Color.red, 0.5f)
            : Color.white;

    #endregion
}