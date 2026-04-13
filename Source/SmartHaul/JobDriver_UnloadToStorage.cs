namespace SmartHaul;

/// <summary>
/// Custom job driver: walk to storage zone and unload all SmartHaul-collected items from inventory.
/// </summary>
public sealed class JobDriver_UnloadToStorage : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    public override IEnumerable<Toil> MakeNewToils()
    {
        // Toil 1: Go to the storage area
        yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.ClosestTouch);

        // Toil 2: Unload all collected items from inventory into storage
        var unloadToil = ToilMaker.MakeToil("SmartHaul_Unload");
        unloadToil.initAction = () => UnloadAllCollectedItems(pawn);
        unloadToil.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return unloadToil;
    }

    public override void Notify_Starting()
    {
        base.Notify_Starting();

        AddFinishAction(condition =>
        {
            if (InventoryCollector.HasCollectedItems(pawn))
            {
                Log.Info($"UnloadToStorage: {pawn.LabelShort} interrupted ({condition}). Dropping items.");
                InventoryCollector.DropAllCollectedItems(pawn);
                InventoryCollector.ClearPawnTracking(pawn);
            }
        });
    }

    /// <summary>
    /// Drops all SmartHaul-tracked items from inventory at pawn's current position.
    /// WHY: Pawn already walked to storage, so pawn.Position is at/near the stockpile.
    /// ThingPlaceMode.Near handles finding valid adjacent cells.
    /// </summary>
    private static void UnloadAllCollectedItems(Pawn pawn)
    {
        if (pawn?.Map == null) return;

        var inventory = pawn.inventory?.innerContainer;
        if (inventory == null || inventory.Count == 0) return;

        var tracked = InventoryCollector.GetTrackedItems(pawn);
        if (tracked == null || tracked.Count == 0) return;

        var map = pawn.Map;
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

        var dropped = 0;
        foreach (var (thing, count) in toDrop)
        {
            if (thing == null || thing.Destroyed) continue;

            // WHY: Drop at pawn.Position — pawn already walked to storage zone.
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
        Log.Info($"  Unloaded {dropped} stacks at {pawn.Position}");
    }
}