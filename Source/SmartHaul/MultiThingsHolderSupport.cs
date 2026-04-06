using System.Linq;

namespace SmartHaul;

/// <summary>
/// Queries IMultipleThingsHolder implementations on storage buildings.
/// Falls back to vanilla behavior if no holder found.
/// </summary>
public static class MultiThingsHolderSupport
{
    /// <summary>Gets capacity at cell from IMultipleThingsHolder, if any.</summary>
    public static bool CapacityAt(Thing thing, IntVec3 storeCell, Map map, out int capacity)
    {
        capacity = 0;

        var holder = FindHolder(storeCell, map);
        return holder != null && holder.CapacityAt(thing, storeCell, map, out capacity);
    }

    /// <summary>Checks if thing can stack at cell via IMultipleThingsHolder.</summary>
    public static bool StackableAt(Thing thing, IntVec3 storeCell, Map map)
    {
        var holder = FindHolder(storeCell, map);
        return holder != null && holder.StackableAt(thing, storeCell, map);
    }

    /// <summary>Finds IMultipleThingsHolder at a cell, checking slot group parent first, then things on cell.</summary>
    private static IMultipleThingsHolder? FindHolder(IntVec3 cell, Map map)
    {
        if (map.haulDestinationManager.SlotGroupParentAt(cell) is ThingWithComps twc)
        {
            var comp = twc.AllComps.OfType<IMultipleThingsHolder>().FirstOrDefault();
            if (comp != null) return comp;
        }

        var thingList = cell.GetThingList(map);
        for (var i = 0; i < thingList.Count; i++)
        {
            if (thingList[i] is IMultipleThingsHolder holder)
                return holder;
        }

        return null;
    }
}