using System.Linq;

namespace SmartHaul;

/// <summary>
/// Job driver that picks up multiple items into inventory, then walks to storage.
/// </summary>
public sealed class JobDriver_HaulToInventory : JobDriver
{
    private CompHauledToInventory? HauledComp => pawn.TryGetComp<CompHauledToInventory>();

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        if (HauledComp == null) return false;
        if (job.targetQueueA.NullOrEmpty()) return false;

        pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
        pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
        return pawn.Reserve(job.targetB, job);
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        var processNext = ProcessNextTarget();
        yield return processNext;

        yield return GotoThingToil(processNext);
        yield return PickupToil(processNext);
        yield return Toils_Jump.JumpIf(processNext, () => !job.targetQueueA.NullOrEmpty());
        yield return GotoStorageToil();
        yield return QueueUnloadToil();
    }

    private Toil ProcessNextTarget()
    {
        return new Toil
        {
            initAction = () =>
            {
                while (job.targetQueueA.Count > 0)
                {
                    var target = job.targetQueueA[0];
                    var count = job.countQueue.Count > 0 ? job.countQueue[0] : -1;

                    job.targetQueueA.RemoveAt(0);
                    if (job.countQueue.Count > 0) job.countQueue.RemoveAt(0);

                    var thing = target.Thing;
                    if (!IsValidTarget(thing)) continue;
                    if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1)) break;

                    job.SetTarget(TargetIndex.A, thing);
                    job.count = count > 0 ? count : thing.stackCount;
                    return;
                }

                job.targetQueueA.Clear();
                job.countQueue.Clear();
            }
        };
    }

    private bool IsValidTarget(Thing? thing)
    {
        if (thing == null || !thing.Spawned || thing.Destroyed) return false;
        if (thing.Map != pawn.Map) return false;
        if (thing.IsForbidden(pawn)) return false;
        return pawn.CanReserve(thing) || pawn.Reserve(thing, job, 1, -1, null, false);
    }

    private Toil GotoThingToil(Toil jumpBackTo)
    {
        var toil = new Toil
        {
            initAction = () =>
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;
                if (!IsValidTarget(thing))
                {
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }
                pawn.pather.StartPath(thing, PathEndMode.ClosestTouch);
            },
            defaultCompleteMode = ToilCompleteMode.PatherArrival
        };

        toil.AddFailCondition(() =>
        {
            var thing = job.GetTarget(TargetIndex.A).Thing;
            return !IsValidTarget(thing) && job.targetQueueA.NullOrEmpty();
        });

        return toil;
    }

    private Toil PickupToil(Toil jumpBackTo)
    {
        return new Toil
        {
            initAction = () =>
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;
                if (!IsValidTarget(thing))
                {
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }

                var comp = HauledComp;
                if (comp == null)
                {
                    Log.Warning($"[SmartHaul] {pawn.LabelShort} has no CompHauledToInventory");
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                Toils_Haul.ErrorCheckForCarry(pawn, thing);

                var wantCount = job.count > 0 ? job.count : thing.stackCount;
                var canCarry = MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing);

                if (ModCompatibility.CombatExtendedIsActive)
                    canCarry = Math.Min(canCarry, CompatHelper.CanFitInInventory(pawn, thing));

                var actualCount = Math.Min(Math.Min(wantCount, canCarry), thing.stackCount);
                if (actualCount <= 0)
                {
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }

                var splitThing = thing.SplitOff(actualCount);
                var shouldMerge = comp.GetHashSet().Any(x => x?.def == splitThing.def);
                pawn.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
                comp.RegisterHauledItem(splitThing);

                ThingProtection.Unprotect(thing);

                if (ModCompatibility.CombatExtendedIsActive)
                    CompatHelper.UpdateInventory(pawn);
            }
        };
    }

    private Toil GotoStorageToil()
    {
        return new Toil
        {
            initAction = () =>
            {
                var comp = HauledComp;
                if (comp == null || comp.GetHashSet().Count == 0)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var target = job.targetB;
                pawn.pather.StartPath(
                    target.HasThing ? (LocalTargetInfo)target.Thing : target.Cell,
                    PathEndMode.ClosestTouch);
            },
            defaultCompleteMode = ToilCompleteMode.PatherArrival
        };
    }

    private Toil QueueUnloadToil()
    {
        return new Toil
        {
            initAction = () =>
            {
                var comp = HauledComp;
                if (comp == null || comp.GetHashSet().Count == 0)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var unloadJob = JobMaker.MakeJob(SmartHaulJobDefOf.UnloadYourHauledInventory, job.targetB);
                if (unloadJob.TryMakePreToilReservations(pawn, false))
                    pawn.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);

                if (!job.targetQueueA.NullOrEmpty() && job.targetQueueA.Count > 0)
                {
                    var lastPos = job.targetQueueA[^1].Thing?.Position ?? pawn.Position;
                    AutoHaulTriggers.NotifyNearbyPawns(pawn, lastPos, pawn.Map);
                }

                EndJobWith(JobCondition.Succeeded);
            }
        };
    }
}