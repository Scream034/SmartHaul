namespace SmartHaul;

/// <summary>
/// ThingComp attached to pawns that tracks items placed into inventory by SmartHaul.
/// Separates SmartHaul items from personal gear/drugs.
/// </summary>
public sealed class CompHauledToInventory : ThingComp
{
    private readonly HashSet<Thing> _hauledItems = new();

    /// <summary>Registers a thing as hauled by SmartHaul.</summary>
    public void RegisterHauledItem(Thing thing)
    {
        if (thing != null)
            _hauledItems.Add(thing);
    }

    /// <summary>Unregisters a thing (after placing at storage).</summary>
    public void UnregisterHauledItem(Thing thing)
    {
        _hauledItems.Remove(thing);
    }

    /// <summary>Returns the tracked set (read-only access for iteration).</summary>
    public HashSet<Thing> GetHashSet() => _hauledItems;

    /// <summary>Clears all tracking.</summary>
    public void ClearTracking() => _hauledItems.Clear();

    /// <summary>True if any tracked items exist in pawn inventory.</summary>
    public bool HasItems()
    {
        if (_hauledItems.Count == 0) return false;

        var pawn = parent as Pawn;
        var inventory = pawn?.inventory?.innerContainer;
        if (inventory == null) return false;

        foreach (var item in _hauledItems)
        {
            if (item != null && !item.Destroyed && inventory.Contains(item))
                return true;
        }
        return false;
    }

    /// <summary>Removes destroyed/null entries.</summary>
    public void CleanupDestroyed()
    {
        _hauledItems.RemoveWhere(t => t == null || t.Destroyed);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        // WHY: Don't save tracked items — they reset on load.
        // Items in inventory persist naturally; tracking is rebuilt.
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            _hauledItems.Clear();
    }
}