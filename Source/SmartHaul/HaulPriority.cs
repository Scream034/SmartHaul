namespace SmartHaul;

/// <summary>
/// Priority levels for smart haul item pickup ordering.
/// Lower value = higher priority.
/// </summary>
public enum HaulPriority
{
    /// <summary>The target item the pawn was assigned to haul — always first.</summary>
    Target = 0,

    /// <summary>Perishable food (CompRottable + IsIngestible) — save from rotting.</summary>
    PerishableFood = 1,

    /// <summary>Any perishable item (CompRottable) — save from rotting.</summary>
    Perishable = 2,

    /// <summary>Items going to the same stockpile as the target.</summary>
    SameStorage = 3,

    /// <summary>Any other haulable item in radius.</summary>
    Other = 4,
}

/// <summary>
/// Helpers to classify and sort items for smart hauling pickup.
/// </summary>
public static class HaulPriorityHelper
{
    /// <summary>
    /// Returns the haul priority for a given thing relative to a target storage cell.
    /// </summary>
    /// <param name="thing">Item to classify.</param>
    /// <param name="pawn">Pawn doing the hauling.</param>
    /// <param name="targetStorageCell">Best storage cell for the primary haul target.</param>
    /// <param name="targetSlotGroup">Slot group of the primary target's best storage.</param>
    /// <returns>Priority level; lower = picked up sooner.</returns>
    public static HaulPriority GetPriority(
        Thing thing,
        Pawn pawn,
        IntVec3 targetStorageCell,
        SlotGroup? targetSlotGroup)
    {
        if (IsPerishableFood(thing)) return HaulPriority.PerishableFood;
        if (IsPerishable(thing))     return HaulPriority.Perishable;
        if (GoesToSameStorage(thing, pawn, targetSlotGroup)) return HaulPriority.SameStorage;
        return HaulPriority.Other;
    }

    /// <summary>
    /// Sorts candidates for pickup: by priority, then distance from origin, then ID (MP determinism).
    /// </summary>
    /// <param name="candidates">List to sort in-place.</param>
    /// <param name="origin">Position to measure distance from (usually target item position).</param>
    /// <param name="pawn">Pawn doing the hauling.</param>
    /// <param name="targetStorageCell">Best storage cell for the primary haul target.</param>
    /// <param name="targetSlotGroup">Slot group of the primary target's best storage.</param>
    public static void SortCandidates(
        List<Thing> candidates,
        IntVec3 origin,
        Pawn pawn,
        IntVec3 targetStorageCell,
        SlotGroup? targetSlotGroup)
    {
        candidates.Sort((a, b) =>
        {
            var pa = GetPriority(a, pawn, targetStorageCell, targetSlotGroup);
            var pb = GetPriority(b, pawn, targetStorageCell, targetSlotGroup);

            if (pa != pb) return pa.CompareTo(pb);

            // PERF: DistanceToSquared is cheaper than DistanceTo
            var da = (a.Position - origin).LengthHorizontalSquared;
            var db = (b.Position - origin).LengthHorizontalSquared;

            if (da != db) return da.CompareTo(db);

            // CRITICAL: deterministic tiebreak for MP
            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        });
    }

    // --- Private helpers ---

    private static bool IsPerishableFood(Thing thing) =>
        thing.def.IsIngestible && IsPerishable(thing);

    private static bool IsPerishable(Thing thing) =>
        thing.TryGetComp<CompRottable>() != null;

    private static bool GoesToSameStorage(Thing thing, Pawn pawn, SlotGroup? targetSlotGroup)
    {
        if (targetSlotGroup == null) return false;
        if (!StoreUtility.TryFindBestBetterStoreCellFor(
                thing, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out var cell))
            return false;

        return pawn.Map.haulDestinationManager.SlotGroupAt(cell) == targetSlotGroup;
    }
}