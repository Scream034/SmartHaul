namespace SmartHaul;

/// <summary>
/// JobDef references for SmartHaul custom jobs.
/// </summary>
[DefOf]
public static class SmartHaulJobDefOf
{
    /// <summary>Walk to storage and unload all collected items from inventory (craft/mine).</summary>
    public static JobDef SmartHaul_UnloadToStorage = null!;

    /// <summary>Pick up items into inventory (hauling work).</summary>
    public static JobDef SmartHaul_HaulToInventory = null!;

    /// <summary>Unload inventory items one-by-one to correct storage cells.</summary>
    public static JobDef SmartHaul_UnloadInventory = null!;

    static SmartHaulJobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SmartHaulJobDefOf));
}