namespace SmartHaul;

/// <summary>
/// Job driver: walk to each queued item, pick it up into inventory,
/// then walk to storage and queue the unload job.
/// Mirrors PUAH approach: pickup loop → goto storage → queue UnloadInventory.
/// </summary>
public sealed class JobDriver_HaulToInventory : JobDriver
{
    private CompHauledToInventory Comp => pawn.TryGetComp<CompHauledToInventory>();

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        if (Comp == null) return false;
        if (job.targetQueueA.NullOrEmpty()) return false;

        pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
        return pawn.Reserve(job.targetQueueA[0], job, 1, -1, null, errorOnFailed);
    }

    public override void Notify_Starting()
    {
        base.Notify_Starting();

        // WHY: Drop collected items if the pawn is interrupted before queuing the Unload job.
        AddFinishAction(condition =>
        {
            var comp = pawn.TryGetComp<CompHauledToInventory>();
            if (comp == null || !comp.HasItems()) return;

            // If the next job is UnloadInventory, we successfully completed HaulToInv.
            var nextJob = pawn.jobs.jobQueue.Peek();
            if (nextJob?.job?.def == SmartHaulJobDefOf.SmartHaul_UnloadInventory)
                return;

            Log.Info($"HaulToInv: {pawn.LabelShort} interrupted ({condition}). Dropping collected items.");
            InventoryCollector.DropAllCollectedItems(pawn);
        });
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        // --- Extract next target from queue ---
        var nextTarget = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);
        yield return nextTarget;

        // --- Walk to item ---
        var gotoItem = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
        gotoItem.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        yield return gotoItem;

        // --- Pick up into inventory ---
        var pickupToil = ToilMaker.MakeToil("SmartHaul_PickupToInv");
        pickupToil.initAction = () =>
        {
            Log.Info($"HaulToInv: {pawn.LabelShort} picking up {job.targetQueueA.Count + 1} items");

            var actor = pawn;
            var thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
            if (thing == null || thing.Destroyed || !thing.Spawned) return;

            var comp = Comp;
            if (comp == null)
            {
                EndJobWith(JobCondition.Errored);
                return;
            }

            Toils_Haul.ErrorCheckForCarry(actor, thing);

            var countToPickUp = Math.Min(
                job.count > 0 ? job.count : thing.stackCount,
                MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));

            if (countToPickUp <= 0) return;

            var splitThing = thing.SplitOff(countToPickUp);

            // WHY: Merge with existing stacks of same def in inventory
            var shouldMerge = false;
            foreach (var tracked in comp.GetHashSet())
            {
                if (tracked?.def == splitThing.def) { shouldMerge = true; break; }
            }

            actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
            comp.RegisterHauledItem(splitThing);

            Log.Info($"  +{countToPickUp} {splitThing.def.label}");

            // WHY: If thing still remains on ground (partial pickup), queue vanilla haul
            // so it doesn't get left behind forever
            if (thing.Spawned && thing.stackCount > 0)
            {
                var remainderJob = HaulAIUtility.HaulToStorageJob(actor, thing, false);
                if (remainderJob?.TryMakePreToilReservations(actor, false) == true)
                    actor.jobs.jobQueue.EnqueueFirst(remainderJob, JobTag.Misc);
            }
        };
        pickupToil.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return pickupToil;

        // --- Loop if more items in queue ---
        yield return Toils_Jump.JumpIf(nextTarget, () =>
            !job.targetQueueA.NullOrEmpty()
            && !MassUtility.IsOverEncumbered(pawn));

        // --- Walk to storage ---
        yield return job.targetB.HasThing
            ? Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
            : Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);

        // --- Queue unload job and finish ---
        var queueUnload = ToilMaker.MakeToil("SmartHaul_QueueUnload");
        queueUnload.initAction = () =>
        {
            var comp = Comp;
            if (comp == null || !comp.HasItems())
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            var unloadJob = JobMaker.MakeJob(
                SmartHaulJobDefOf.SmartHaul_UnloadInventory, job.targetB);

            if (unloadJob.TryMakePreToilReservations(pawn, false))
            {
                pawn.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
                Log.Info($"HaulToInv: {pawn.LabelShort} queued unload");
            }

            EndJobWith(JobCondition.Succeeded);
        };
        queueUnload.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return queueUnload;
    }
}