namespace SmartHaul;

/// <summary>
/// Combat Extended compatibility stubs.
/// </summary>
internal static class CompatHelper
{
    public static int CanFitInInventory(Pawn pawn, Thing thing) => thing.stackCount;
    public static void UpdateInventory(Pawn pawn) { }
}