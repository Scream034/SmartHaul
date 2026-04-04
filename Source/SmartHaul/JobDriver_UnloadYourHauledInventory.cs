using System.Linq;

namespace SmartHaul;

public sealed class JobDriver_UnloadYourHauledInventory : JobDriver
{
	private int _countToDrop = -1;
	private int _unloadDuration = 3;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref _countToDrop, "countToDrop", -1);
	}

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	public override IEnumerable<Toil> MakeNewToils()
	{
		if (ModCompatibilityCheck.ExtendedStorageIsActive)
		{
			_unloadDuration = 20;
		}

		var begin = Toils_General.Wait(_unloadDuration);
		yield return begin;

		var carriedThings = pawn.TryGetComp<CompHauledToInventory>()?.GetHashSet();

		if (carriedThings == null)
		{
			yield break;
		}

		yield return FindTargetOrDrop(carriedThings);
		yield return PullItemFromInventory(carriedThings, begin);

		var releaseReservation = ReleaseReservation();
		var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);

		yield return Toils_Jump.JumpIf(carryToCell, TargetIsCell);

		var carryToContainer = Toils_Haul.CarryHauledThingToContainer();
		yield return carryToContainer;
		yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
		yield return Toils_Haul.JumpToCarryToNextContainerIfPossible(carryToContainer, TargetIndex.B);
		yield return Toils_Jump.Jump(releaseReservation);

		yield return carryToCell;
		yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

		yield return releaseReservation;
		yield return Toils_Jump.Jump(begin);
	}

	private bool TargetIsCell()
	{
		return !TargetB.HasThing;
	}

	private Toil ReleaseReservation()
	{
		return new Toil
		{
			initAction = () =>
			{
				if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob))
				{
					pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
				}
			}
		};
	}

	private Toil PullItemFromInventory(HashSet<Thing> carriedThings, Toil wait)
	{
		return new Toil
		{
			initAction = () =>
			{
				var thing = job.GetTarget(TargetIndex.A).Thing;

				if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
				{
					carriedThings.Remove(thing);
					pawn.jobs.curDriver.JumpToToil(wait);
					return;
				}

				if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
					|| !thing.def.EverStorable(false))
				{
					Log.Message($"{pawn} incapable, dropping {thing}");
					pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, _countToDrop, out _);
					EndJobWith(JobCondition.Succeeded);
					carriedThings.Remove(thing);
					return;
				}

				pawn.inventory.innerContainer.TryTransferToContainer(
					thing,
					pawn.carryTracker.innerContainer,
					_countToDrop,
					out thing);

				job.count = _countToDrop;
				job.SetTarget(TargetIndex.A, thing);
				carriedThings.Remove(thing);

				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					CompatHelper.UpdateInventory(pawn);
				}

				thing.SetForbidden(false, false);
			}
		};
	}

	private Toil FindTargetOrDrop(HashSet<Thing> carriedThings)
	{
		return new Toil
		{
			initAction = () =>
			{
				var unloadableThing = FirstUnloadableThing(pawn, carriedThings);

				if (unloadableThing.Count == 0)
				{
					if (carriedThings.Count == 0)
					{
						EndJobWith(JobCondition.Succeeded);
					}

					return;
				}

				var currentPriority = StoragePriority.Unstored;

				if (!StoreUtility.TryFindBestBetterStorageFor(
					unloadableThing.Thing,
					pawn,
					pawn.Map,
					currentPriority,
					pawn.Faction,
					out var cell,
					out var destination))
				{
					Log.Message($"{pawn} no storage for {unloadableThing.Thing}, dropping");
					pawn.inventory.innerContainer.TryDrop(
						unloadableThing.Thing,
						ThingPlaceMode.Near,
						unloadableThing.Thing.stackCount,
						out _);
					EndJobWith(JobCondition.Succeeded);
					return;
				}

				job.SetTarget(TargetIndex.A, unloadableThing.Thing);

				if (cell == IntVec3.Invalid)
				{
					job.SetTarget(TargetIndex.B, destination as Thing);
				}
				else
				{
					job.SetTarget(TargetIndex.B, cell);
				}

				Log.Message($"{pawn} unloading {unloadableThing.Thing} → {job.targetB}");

				if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
				{
					// Failed to reserve — try to find alternative cell
					if (!TryFindAlternativeCell(unloadableThing.Thing, out var altCell))
					{
						Log.Message($"{pawn} no alternative storage, dropping {unloadableThing.Thing}");
						pawn.inventory.innerContainer.TryDrop(
							unloadableThing.Thing,
							ThingPlaceMode.Near,
							unloadableThing.Thing.stackCount,
							out _);
						EndJobWith(JobCondition.Incompletable);
						return;
					}

					job.SetTarget(TargetIndex.B, altCell);

					if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB))
					{
						Log.Message($"{pawn} failed reserving alternative, dropping {unloadableThing.Thing}");
						pawn.inventory.innerContainer.TryDrop(
							unloadableThing.Thing,
							ThingPlaceMode.Near,
							unloadableThing.Thing.stackCount,
							out _);
						EndJobWith(JobCondition.Incompletable);
						return;
					}
				}

				_countToDrop = unloadableThing.Thing.stackCount;
			}
		};
	}

	/// <summary>
	/// Try to find alternative storage cell when primary is unavailable.
	/// Deterministic: iterates cells in priority order.
	/// </summary>
	private bool TryFindAlternativeCell(Thing thing, out IntVec3 cell)
	{
		var currentPriority = StoragePriority.Unstored;

		return StoreUtility.TryFindBestBetterStoreCellFor(
			thing,
			pawn,
			pawn.Map,
			currentPriority,
			pawn.Faction,
			out cell);
	}

	/// <summary>
	/// Get first unloadable thing from inventory.
	/// Deterministic: sorted by category index then defName.
	/// </summary>
	private static ThingCount FirstUnloadableThing(Pawn pawn, HashSet<Thing> carriedThings)
	{
		var inventory = pawn.inventory.innerContainer;

		// Sort deterministically
		foreach (var thing in carriedThings
			.OrderBy(t => t.def.FirstThingCategory?.index ?? int.MaxValue)
			.ThenBy(t => t.def.defName)
			.ThenBy(t => t.thingIDNumber))
		{
			if (!inventory.Contains(thing))
			{
				// Merged stack — find by def
				var stragglerDef = thing.def;
				carriedThings.Remove(thing);

				for (var i = 0; i < inventory.Count; i++)
				{
					var candidate = inventory[i];

					if (candidate.def == stragglerDef)
					{
						return new ThingCount(candidate, candidate.stackCount);
					}
				}

				continue;
			}

			return new ThingCount(thing, thing.stackCount);
		}

		return default;
	}
}