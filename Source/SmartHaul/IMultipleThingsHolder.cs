namespace SmartHaul;

/// <summary>
/// Interface for storage mods that hold multiple thing stacks per cell.
/// Implemented by mods like Extended Storage, LWM's Deep Storage, etc.
/// </summary>
public interface IMultipleThingsHolder
{
    /// <summary>Checks capacity for a thing at a cell. Returns true if this holder manages that cell.</summary>
    bool CapacityAt(Thing thing, IntVec3 storeCell, Map map, out int capacity);

    /// <summary>Checks if a thing can stack at a cell. Returns true if this holder manages that cell.</summary>
    bool StackableAt(Thing thing, IntVec3 storeCell, Map map);
}