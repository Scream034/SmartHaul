namespace SmartHaul;

/// <summary>
/// Manages temporary protection of things from being hauled by other pawns.
/// Used to prevent "stealing" of items that a worker is about to collect.
/// <para>
/// Protection is automatically cleaned up every 120 ticks (~2 seconds).
/// </para>
/// </summary>
public static class ThingProtection
{
    private static readonly HashSet<int> _protectedThings = new(32);
    private static int _lastCleanupTick;
    private const int CLEANUP_INTERVAL = 120;

    /// <summary>
    /// Protects a thing from being hauled by other pawns.
    /// </summary>
    /// <param name="thing">Thing to protect.</param>
    public static void Protect(Thing? thing)
    {
        if (thing != null)
            _protectedThings.Add(thing.thingIDNumber);
    }

    /// <summary>
    /// Removes protection from a thing.
    /// </summary>
    /// <param name="thing">Thing to unprotect.</param>
    public static void Unprotect(Thing? thing)
    {
        if (thing != null)
            _protectedThings.Remove(thing.thingIDNumber);
    }

    /// <summary>
    /// Checks if thing is protected.
    /// </summary>
    public static bool IsProtected(Thing? thing) =>
        thing != null && _protectedThings.Contains(thing.thingIDNumber);

    /// <summary>
    /// Protects all haulable items in radius around position.
    /// </summary>
    /// <param name="center">Center position.</param>
    /// <param name="map">Map.</param>
    /// <param name="radius">Search radius (default 2).</param>
    public static void ProtectRadius(IntVec3 center, Map? map, float radius = 2f)
    {
        if (map == null) return;

        foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
        {
            if (!cell.InBounds(map)) continue;

            var things = cell.GetThingList(map);
            for (var i = 0; i < things.Count; i++)
            {
                if (things[i].def.EverHaulable)
                    _protectedThings.Add(things[i].thingIDNumber);
            }
        }
    }

    /// <summary>
    /// Called every tick. Cleans up stale protections periodically.
    /// </summary>
    public static void Tick()
    {
        if (_protectedThings.Count == 0) return;

        var tick = Find.TickManager.TicksGame;
        if (tick - _lastCleanupTick >= CLEANUP_INTERVAL)
        {
            _protectedThings.Clear();
            _lastCleanupTick = tick;
        }
    }

    /// <summary>
    /// Clears all protections. Call on map change or game reset.
    /// </summary>
    public static void Reset() => _protectedThings.Clear();
}