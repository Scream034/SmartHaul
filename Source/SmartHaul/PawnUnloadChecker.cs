namespace SmartHaul;

/// <summary>
/// Checks if pawn should unload hauled inventory.
/// </summary>
public static class PawnUnloadChecker
{
    public static void CheckIfPawnShouldUnloadInventory(Pawn pawn, bool prioritize = false)
    {
        if (pawn?.Map == null || pawn.Drafted) return;

        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null || comp.GetHashSet().Count == 0) return;

        var curJob = pawn.CurJobDef;
        if (curJob == SmartHaulJobDefOf.UnloadYourHauledInventory ||
            curJob == SmartHaulJobDefOf.HaulToInventory)
            return;

        var job = JobMaker.MakeJob(SmartHaulJobDefOf.UnloadYourHauledInventory, pawn);
        if (!job.TryMakePreToilReservations(pawn, false)) return;

        if (prioritize)
            pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
        else
            pawn.jobs.jobQueue.EnqueueLast(job, JobTag.Misc);
    }
}