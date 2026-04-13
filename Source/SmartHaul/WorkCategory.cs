namespace SmartHaul;

/// <summary>
/// Categorizes pawn jobs for SmartHaul item handling behavior.
/// </summary>
public enum WorkCategory
{
    /// <summary>Not a SmartHaul-managed job.</summary>
    None,

    /// <summary>Harvest, CutPlant — haul to storage when done.</summary>
    Farming,

    /// <summary>Mine, Deconstruct, FloorRemoval — drop in pile when full.</summary>
    Extraction,

    /// <summary>DoBill (crafting, butchering) — haul to storage when done.</summary>
    Crafting,

    /// <summary>HaulToCell, SmartHaul_HaulSmart — haul to storage.</summary>
    Hauling,
}

/// <summary>
/// Helpers for classifying JobDefs into SmartHaul work categories.
/// </summary>
public static class WorkCategoryHelper
{
    /// <summary>Returns the SmartHaul category for a given job definition.</summary>
    public static WorkCategory GetCategory(JobDef? def)
    {
        if (def == null) return WorkCategory.None;

        if (def == JobDefOf.Harvest) return WorkCategory.Farming;
        if (def == JobDefOf.HarvestDesignated) return WorkCategory.Farming;
        if (def == JobDefOf.CutPlant) return WorkCategory.Farming;
        if (def == JobDefOf.CutPlantDesignated) return WorkCategory.Farming;

        if (def == JobDefOf.Mine) return WorkCategory.Extraction;
        if (def == JobDefOf.Deconstruct) return WorkCategory.Extraction;
        if (def == JobDefOf.RemoveFloor) return WorkCategory.Extraction;

        if (def == JobDefOf.DoBill) return WorkCategory.Crafting;

        if (def == JobDefOf.HaulToCell) return WorkCategory.Hauling;
        if (def == SmartHaulJobDefOf.SmartHaul_UnloadToStorage) return WorkCategory.Hauling;
        if (def == SmartHaulJobDefOf.SmartHaul_HaulToInventory) return WorkCategory.Hauling;
        if (def == SmartHaulJobDefOf.SmartHaul_UnloadInventory) return WorkCategory.Hauling;

        return WorkCategory.None;
    }

    /// <summary>
    /// Returns true for transient jobs that should not trigger item handling
    /// (e.g. Wait, Goto — intermediate steps in job chains).
    /// </summary>
    public static bool IsWaitJob(JobDef? def)
    {
        if (def == null) return false;
        return def == JobDefOf.Wait
            || def == JobDefOf.Wait_MaintainPosture
            || def == JobDefOf.Wait_Combat
            || def == JobDefOf.Wait_Wander
            || def == JobDefOf.Wait_SafeTemperature
            || def == JobDefOf.GotoWander
            || def == JobDefOf.Goto
            // WHY: Our own jobs — don't trigger OnJobStarting drop logic
            || def == SmartHaulJobDefOf.SmartHaul_UnloadToStorage
            || def == SmartHaulJobDefOf.SmartHaul_HaulToInventory
            || def == SmartHaulJobDefOf.SmartHaul_UnloadInventory;
    }

    /// <summary>
    /// Returns true for high-priority jobs where collected items must be dropped immediately
    /// (combat, rest, medical emergencies).
    /// </summary>
    public static bool IsCriticalJob(JobDef? def)
    {
        if (def == null) return false;
        return def == JobDefOf.LayDown
            || def == JobDefOf.LayDownResting
            || def == JobDefOf.LayDownAwake
            || def == JobDefOf.FleeAndCower
            || def == JobDefOf.Flee
            || def == JobDefOf.AttackMelee
            || def == JobDefOf.AttackStatic
            || def == JobDefOf.Rescue
            || def == JobDefOf.Arrest
            || def == JobDefOf.TendPatient
            || def == JobDefOf.Ingest
            || def == JobDefOf.Vomit;
    }
}