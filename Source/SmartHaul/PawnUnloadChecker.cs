namespace SmartHaul;

/// <summary>
/// Checks if pawn should unload hauled inventory and queues unload job if needed.
/// MP-safe: Job is created ONLY after all validations pass to preserve UniqueID determinism.
/// </summary>
public static class PawnUnloadChecker
{
	public static void CheckIfPawnShouldUnloadInventory(Pawn pawn, bool forced = false)
	{
		// === All validations BEFORE JobMaker.MakeJob() ===
		// Critical for MP: JobMaker.MakeJob() consumes UniqueID.
		// If validation fails on one client but not another, IDs desync.

		if (pawn?.Faction != Faction.OfPlayerSilentFail)
		{
			return;
		}

		if (!Settings.IsAllowedRace(pawn.RaceProps))
		{
			return;
		}

		var hauledComp = pawn.GetComp<CompHauledToInventory>();

		if (hauledComp == null)
		{
			return;
		}

		var carriedThings = hauledComp.GetHashSet();

		if (carriedThings == null || carriedThings.Count == 0)
		{
			return;
		}

		var inventory = pawn.inventory?.innerContainer;

		if (inventory == null || inventory.Count == 0)
		{
			return;
		}

		// Determine unload conditions
		var shouldUnload = forced
			|| MassUtility.EncumbrancePercent(pawn) >= 0.90f
			|| carriedThings.Count >= 1;

		// Check for rotting items
		if (!shouldUnload)
		{
			var count = inventory.Count;

			for (var i = 0; i < count; i++)
			{
				var rottable = inventory[i].TryGetComp<CompRottable>();

				if (rottable != null && rottable.TicksUntilRotAtCurrentTemp < 30000)
				{
					shouldUnload = true;
					break;
				}
			}
		}

		// === Job created ONLY when certain it will be used ===
		if (shouldUnload)
		{
			var job = JobMaker.MakeJob(SmartHaulJobDefOf.UnloadYourHauledInventory, pawn);

			if (job.TryMakePreToilReservations(pawn, false))
			{
				pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
			}

			return;
		}

		// Inventory sync sanity check — no Job creation
		if (Find.TickManager.TicksGame % 50 == 0 && inventory.Count < carriedThings.Count)
		{
			Verse.Log.Warning($"[SmartHaul] {pawn} inventory out of sync. Clearing haul index.");
			carriedThings.Clear();
			pawn.inventory.UnloadEverything = true;
		}
	}
}

[DefOf]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
	"Style",
	"IDE1006:Naming Styles",
	Justification = "Must match defName")]
public static class SmartHaulJobDefOf
{
	public static JobDef UnloadYourHauledInventory;
	public static JobDef HaulToInventory;
}