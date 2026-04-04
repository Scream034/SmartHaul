using System.Linq;

namespace SmartHaul;

public sealed class JobDriver_HaulToInventory : JobDriver
{
	private CompHauledToInventory _hauledComp;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		_hauledComp = pawn.TryGetComp<CompHauledToInventory>();

		if (_hauledComp == null)
		{
			return false;
		}

		if (job.targetQueueA.NullOrEmpty())
		{
			return false;
		}

		// Reserve what we can — don't fail if some are unavailable
		pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
		pawn.ReserveAsManyAsPossible(job.targetQueueB, job);

		// Need at least the storage target
		return pawn.Reserve(job.targetB, job);
	}

	public override IEnumerable<Toil> MakeNewToils()
	{
		// Main loop: process each thing in queue
		var processNext = ProcessNextTarget();
		yield return processNext;

		// Go to thing (with validation)
		var gotoThing = GotoThingToil(processNext);
		yield return gotoThing;

		// Pick up thing
		var pickupThing = PickupToil(processNext);
		yield return pickupThing;

		// Loop back if more items
		yield return Toils_Jump.JumpIf(processNext, () => !job.targetQueueA.NullOrEmpty());

		// Go to storage
		yield return GotoStorageToil();

		// Queue unload job
		yield return QueueUnloadToil();
	}

	/// <summary>
	/// Extract next valid target from queue. Skips invalid items.
	/// </summary>
	private Toil ProcessNextTarget()
	{
        var toil = new Toil
        {
            initAction = () =>
            {
                // Find next valid target
                while (job.targetQueueA.Count > 0)
                {
                    var target = job.targetQueueA[0];
                    var count = job.countQueue.Count > 0 ? job.countQueue[0] : -1;

                    job.targetQueueA.RemoveAt(0);

                    if (job.countQueue.Count > 0)
                    {
                        job.countQueue.RemoveAt(0);
                    }

                    var thing = target.Thing;

                    // Validate
                    if (!IsValidTarget(thing))
                    {
                        continue;
                    }

                    // Check if we can still carry
                    if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 1))
                    {
                        // Over capacity — go straight to storage
                        break;
                    }

                    // Set as current target
                    job.SetTarget(TargetIndex.A, thing);
                    job.count = count > 0 ? count : thing.stackCount;
                    return;
                }

                // No more valid targets — proceed to storage
                job.targetQueueA.Clear();
                job.countQueue.Clear();
            }
        };

        return toil;
	}

	private bool IsValidTarget(Thing thing)
	{
		if (thing == null)
		{
			return false;
		}

		if (!thing.Spawned)
		{
			return false;
		}

		if (thing.Destroyed)
		{
			return false;
		}

		if (thing.Map != pawn.Map)
		{
			return false;
		}

		if (thing.IsForbidden(pawn))
		{
			return false;
		}

		// Check reservation — someone else might have taken it
		if (!pawn.CanReserve(thing))
		{
			// Try to reserve again
			if (!pawn.Reserve(thing, job, 1, -1, null, false))
			{
				return false;
			}
		}

		return true;
	}

	private Toil GotoThingToil(Toil jumpBackTo)
	{
        var toil = new Toil
        {
            initAction = () =>
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;

                // Re-validate before walking
                if (!IsValidTarget(thing))
                {
                    // Skip to next
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }

                pawn.pather.StartPath(thing, PathEndMode.ClosestTouch);
            },

            defaultCompleteMode = ToilCompleteMode.PatherArrival
        };

        // Don't fail on despawn — we handle it in pickup
        toil.AddFailCondition(() =>
		{
			var thing = job.GetTarget(TargetIndex.A).Thing;

			// Only fail if NO items left AND current is invalid
			if (!IsValidTarget(thing) && job.targetQueueA.NullOrEmpty())
			{
				return true;
			}

			return false;
		});

		return toil;
	}

	private Toil PickupToil(Toil jumpBackTo)
	{
        var toil = new Toil
        {
            initAction = () =>
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;

                // Final validation
                if (!IsValidTarget(thing))
                {
                    // Thing disappeared while walking — skip
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }

                Toils_Haul.ErrorCheckForCarry(pawn, thing);

                // Calculate how much we can pick up
                var wantCount = job.count > 0 ? job.count : thing.stackCount;
                var canCarry = MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing);

                if (ModCompatibilityCheck.CombatExtendedIsActive)
                {
                    canCarry = Math.Min(canCarry, CompatHelper.CanFitInInventory(pawn, thing));
                }

                var actualCount = Math.Min(wantCount, canCarry);
                actualCount = Math.Min(actualCount, thing.stackCount);

                if (actualCount <= 0)
                {
                    // Can't carry — skip
                    pawn.jobs.curDriver.JumpToToil(jumpBackTo);
                    return;
                }

                // Pick up
                var splitThing = thing.SplitOff(actualCount);
                var shouldMerge = _hauledComp.GetHashSet().Any(x => x.def == splitThing.def);
                pawn.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
                _hauledComp.RegisterHauledItem(splitThing);

                if (ModCompatibilityCheck.CombatExtendedIsActive)
                {
                    CompatHelper.UpdateInventory(pawn);
                }
            }
        };

        return toil;
	}

	private Toil GotoStorageToil()
	{
        var toil = new Toil
        {
            initAction = () =>
            {
                // Check if we have anything to unload
                if (_hauledComp.GetHashSet().Count == 0)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var target = job.targetB;

                if (target.HasThing)
                {
                    pawn.pather.StartPath(target.Thing, PathEndMode.ClosestTouch);
                }
                else
                {
                    pawn.pather.StartPath(target.Cell, PathEndMode.ClosestTouch);
                }
            },

            defaultCompleteMode = ToilCompleteMode.PatherArrival
        };

        return toil;
	}

	private Toil QueueUnloadToil()
	{
        var toil = new Toil
        {
            initAction = () =>
            {
                // Check if we have items
                if (_hauledComp == null || _hauledComp.GetHashSet().Count == 0)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var unloadJob = JobMaker.MakeJob(
                    SmartHaulJobDefOf.UnloadYourHauledInventory,
                    job.targetB);

                if (unloadJob.TryMakePreToilReservations(pawn, false))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
                }

                EndJobWith(JobCondition.Succeeded);
            }
        };

        return toil;
	}
}