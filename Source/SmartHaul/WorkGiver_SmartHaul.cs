using System.Linq;

namespace SmartHaul;

/// <summary>
/// WorkGiver that creates multi-item haul jobs.
/// Higher priority than vanilla HaulGeneral — picks up multiple items into inventory.
/// </summary>
public sealed class WorkGiver_SmartHaul : WorkGiver_HaulGeneral
{
    public override bool ShouldSkip(Pawn pawn, bool forced = false)
    {
        if (base.ShouldSkip(pawn, forced)) return true;
        if (!Settings.EnableSmartHauling) return true;
        if (!Settings.SmartHaulForAutoHaul && !forced) return true;
        if (pawn.Faction == null || !pawn.Faction.IsPlayer) return true;
        if (pawn.TryGetComp<CompHauledToInventory>() == null) return true;
        if (pawn.IsQuestLodger()) return true;
        if (GetGearRatio(pawn) >= 0.9f) return true;
        return false;
    }

    public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        if (!thing.Spawned || thing.IsForbidden(pawn)) return false;
        if (!pawn.CanReserve(thing, 1, -1, null, forced)) return false;
        if (thing is Corpse) return false;
        if (thing.IsInValidBestStorage()) return false;
        if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1)) return false;
        if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced)) return false;
        if (!StoreUtility.TryFindBestBetterStorageFor(
                thing, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(thing),
                pawn.Faction, out _, out _, false))
            return false;
        return true;
    }

    public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        if (!HasJobOnThing(pawn, thing, forced))
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        var map = pawn.Map;

        // Find storage for first item
        if (!StoreUtility.TryFindBestBetterStorageFor(
                thing, pawn, map,
                StoreUtility.CurrentStoragePriorityOf(thing),
                pawn.Faction, out var storeCell, out var haulDest, true))
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        // Determine storage target
        LocalTargetInfo storeTarget;
        if (haulDest is ISlotGroupParent)
            storeTarget = storeCell;
        else if (haulDest is Thing destThing)
            storeTarget = destThing;
        else
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        // Build job
        var job = JobMaker.MakeJob(SmartHaulJobDefOf.SmartHaul_HaulToInventory, null, storeTarget);
        job.targetQueueA = new List<LocalTargetInfo>();
        job.countQueue = new List<int>();

        // Collect nearby haulables
        var candidates = new List<Thing>(32);
        candidates.Add(thing);

        foreach (var t in GenRadial.RadialDistinctThingsAround(
            thing.Position, map, Settings.HaulPickupRadius, true))
        {
            if (t == thing) continue;
            if (!t.def.EverHaulable || !t.Spawned) continue;
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t)) continue;
            if (t is Corpse) continue;
            if (!Settings.CollectChunks && IsChunk(t)) continue;
            if (t.IsInValidBestStorage()) continue;
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, false)) continue;
            candidates.Add(t);
        }

        // Sort: perishable food first, then perishable, then same storage, then distance
        if (storeCell.IsValid)
        {
            var targetSlotGroup = map.haulDestinationManager.SlotGroupAt(storeCell);
            HaulPriorityHelper.SortCandidates(
                candidates, thing.Position, pawn, storeCell, targetSlotGroup);
        }
        else
        {
            // CRITICAL: deterministic sort for MP
            candidates.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
        }

        // Trim by mass capacity
        var maxMass = MassUtility.Capacity(pawn)
            - MassUtility.GearMass(pawn)
            - MassUtility.InventoryMass(pawn);
        var totalMass = 0f;

        foreach (var item in candidates)
        {
            var perUnit = item.GetStatValue(StatDefOf.Mass, true, -1);
            var itemMass = item.stackCount * perUnit;

            if (totalMass + itemMass > maxMass && job.targetQueueA.Count > 0)
            {
                // Try partial
                var canFit = perUnit > 0f ? (int)((maxMass - totalMass) / perUnit) : 0;
                if (canFit > 0)
                {
                    job.targetQueueA.Add(item);
                    job.countQueue.Add(canFit);
                }
                break;
            }

            job.targetQueueA.Add(item);
            job.countQueue.Add(item.stackCount);
            totalMass += itemMass;
        }

        if (job.targetQueueA.Count == 0)
            return HaulAIUtility.HaulToStorageJob(pawn, thing, forced);

        // Log.Info($"WorkGiver: {pawn.LabelShort} queued {job.targetQueueA.Count} items");
        return job;
    }

    private static float GetGearRatio(Pawn p) =>
        MassUtility.Capacity(p) > 0f
            ? MassUtility.GearMass(p) / MassUtility.Capacity(p)
            : 1f;

    private static bool IsChunk(Thing thing) =>
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.Chunks) == true ||
        thing.def.thingCategories?.Contains(ThingCategoryDefOf.StoneChunks) == true;
}