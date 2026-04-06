namespace SmartHaul;

/// <summary>
/// Tracks which items in pawn's inventory were picked up for hauling by SmartHaul.
/// <para>
/// Dual tracking system:
/// <list type="bullet">
///   <item><c>_items</c> — exact Thing references for precise identification</item>
///   <item><c>_trackedDefs</c> — ThingDefs to handle stack merging/splitting</item>
/// </list>
/// </para>
/// </summary>
public sealed class CompHauledToInventory : ThingComp
{
    private HashSet<Thing> _items = new();
    private HashSet<ThingDef> _trackedDefs = new();

    /// <summary>
    /// Returns tracked items set, cleaning null/destroyed references first.
    /// </summary>
    public HashSet<Thing> GetHashSet()
    {
        _items.RemoveWhere(static x => x == null || x.Destroyed);
        return _items;
    }

    /// <summary>
    /// Returns tracked ThingDefs. Survives stack merge/split operations.
    /// </summary>
    public HashSet<ThingDef> GetTrackedDefs() => _trackedDefs;

    /// <summary>
    /// Registers a thing as hauled to inventory.
    /// </summary>
    public void RegisterHauledItem(Thing? thing)
    {
        if (thing == null) return;
        _items.Add(thing);
        _trackedDefs.Add(thing.def);
    }

    /// <summary>
    /// Clears all tracking.
    /// </summary>
    public void ClearTracking()
    {
        _items.Clear();
        _trackedDefs.Clear();
    }

    /// <summary>
    /// Removes tracking for a specific def.
    /// </summary>
    public void UntrackDef(ThingDef? def)
    {
        if (def == null) return;
        _trackedDefs.Remove(def);
        _items.RemoveWhere(t => t?.def == def);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Collections.Look(ref _items, "ThingsHauledToInventory", LookMode.Reference);
        Scribe_Collections.Look(ref _trackedDefs, "TrackedDefs", LookMode.Def);
        _items ??= new HashSet<Thing>();
        _trackedDefs ??= new HashSet<ThingDef>();
    }
}