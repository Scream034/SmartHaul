using System.Linq;

namespace SmartHaul;

/// <summary>
/// Support for IHoldMultipleThings interface.
/// </summary>
public static class HoldMultipleThingsSupport
{
    public static bool CapacityAt(Thing thing, IntVec3 storeCell, Map map, out int capacity)
    {
        capacity = 0;

        var slotParent = map.haulDestinationManager.SlotGroupParentAt(storeCell);
        if (slotParent is ThingWithComps twc)
        {
            var holder = twc.AllComps.OfType<IMultipleThingsHolder>().FirstOrDefault();
            if (holder != null)
                return holder.CapacityAt(thing, storeCell, map, out capacity);
        }

        foreach (var t in storeCell.GetThingList(map))
        {
            if (t is IMultipleThingsHolder holder)
                return holder.CapacityAt(thing, storeCell, map, out capacity);
        }

        return false;
    }

    public static bool StackableAt(Thing thing, IntVec3 storeCell, Map map)
    {
        var slotParent = map.haulDestinationManager.SlotGroupParentAt(storeCell);
        if (slotParent is ThingWithComps twc)
        {
            var holder = twc.AllComps.OfType<IMultipleThingsHolder>().FirstOrDefault();
            if (holder != null)
                return holder.StackableAt(thing, storeCell, map);
        }

        foreach (var t in storeCell.GetThingList(map))
        {
            if (t is IMultipleThingsHolder holder)
                return holder.StackableAt(thing, storeCell, map);
        }

        return false;
    }
}