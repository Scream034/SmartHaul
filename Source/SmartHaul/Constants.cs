namespace SmartHaul;

/// <summary>
/// Constants for the Inventory Collect & Drop system.
/// </summary>
public static class Constants
{
    /// <summary>Radius to pickup items after work action.</summary>
    public const float PICKUP_RADIUS = 2.9f;

    /// <summary>Default inventory fill threshold for auto-action (80%).</summary>
    public const float DEFAULT_INVENTORY_THRESHOLD = 0.8f;

    /// <summary>Default pickup radius for smart hauling.</summary>
    public const float DEFAULT_HAUL_PICKUP_RADIUS = 10f;

    /// <summary>Minimum configurable haul pickup radius.</summary>
    public const float MIN_HAUL_PICKUP_RADIUS = 3f;

    /// <summary>Maximum configurable haul pickup radius.</summary>
    public const float MAX_HAUL_PICKUP_RADIUS = 20f;
}