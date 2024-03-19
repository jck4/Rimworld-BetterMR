using Verse.AI;
using HarmonyLib;
using Verse;
using RimWorld;
using System.Collections.Generic;

public class TargetedMR : Mod
{
    public TargetedMR(ModContentPack content) : base(content)
    {
        Harmony harmony = new Harmony("com.TargetedMR.jck.FindPawnToKillOverride");
        harmony.PatchAll();
    }
}

[HarmonyPatch(typeof(MurderousRageMentalStateUtility), "FindPawnToKill")]
public static class OverrideFindPawnToKillPatch
{
	private static List<Pawn> tmpTargets = new List<Pawn>();
	public static bool Prefix(Pawn pawn, ref Pawn __result)
	{
        if (!pawn.Spawned)
        {
            __result = null;
            return false;
        }

        IReadOnlyList<Pawn> potentialTargets = pawn.Map.mapPawns.AllPawnsSpawned;

		//Get the original potential targets. 
		for (int i = 0; i < potentialTargets.Count; i++)
		{
			Pawn pawn2 = potentialTargets[i];
			if ((pawn2.Faction == pawn.Faction || (pawn2.IsPrisoner && pawn2.HostFaction == pawn.Faction)) && pawn2.RaceProps.Humanlike && pawn2 != pawn && pawn.CanReach(pawn2, PathEndMode.Touch, Danger.Deadly) && (pawn2.CurJob == null || !pawn2.CurJob.exitMapOnArrival))
			{
				tmpTargets.Add(pawn2);
			}
		}
		
		if (tmpTargets.Count == 0)
        {
            __result = null;
            return false;
        }

        // Find the pawn with the lowest opinion
        Pawn target = tmpTargets.MinBy(pawn2 => pawn.relations.OpinionOf(pawn2));
        
        // We really only want to target pawns with bad relations.
        if (pawn.relations.OpinionOf(target) < 0)
        {
            __result = target;
        }
        else
        {
            __result = null;
			//Default method if no valid bad blood ;)
			Log.Message($"Default TryGiveJob condition met in murderous rage.");
			return true;
        }
        
        return false; 
	}
}


[HarmonyPatch(typeof(JobGiver_MurderousRage), "TryGiveJob")]
public static class OverrideTryGiveJobPatch
{
	    public static bool Prefix(Pawn pawn, ref Job __result)
    {
		if (!MurderousRageStartTickTracker.IsTimeToStartMurder(pawn))
        {
            // Not time to start murder yet, delay the job assignment
            __result = null;
            return false;
        }

		if (!(pawn.MentalState is MentalState_MurderousRage mentalState_MurderousRage) || !mentalState_MurderousRage.IsTargetStillValidAndReachable())
		{
            __result = null;
            return false;
		}
		Thing spawnedParentOrMe = mentalState_MurderousRage.target.SpawnedParentOrMe;
        Job job;
        if (pawn.equipment.Primary != null && !pawn.equipment.Primary.def.IsMeleeWeapon)
        {
            // If the pawn has a ranged weapon equipped, assign a job to attack from range
            job = JobMaker.MakeJob(JobDefOf.AttackStatic, spawnedParentOrMe);
        }
        else
        {
            // Otherwise, default to melee attack
            job = JobMaker.MakeJob(JobDefOf.AttackMelee, spawnedParentOrMe);
        }

		job.canBashDoors = true;
		job.killIncappedTarget = true;
		if (spawnedParentOrMe != mentalState_MurderousRage.target)
		{
			job.maxNumStaticAttacks = 2;
		}
		__result = job;
		return false;
    }

}

public static class MurderousRageStartTickTracker
{
    private static Dictionary<Pawn, int> startTicks = new Dictionary<Pawn, int>();

    public static bool IsTimeToStartMurder(Pawn pawn)
    {
        if (!startTicks.ContainsKey(pawn))
        {
            // Set the tick count for 10 seconds from now
            startTicks[pawn] = Find.TickManager.TicksGame + 600;
            ShowWarningMessage(pawn);
            return false;
        }

        if (Find.TickManager.TicksGame >= startTicks[pawn])
        {
            // Time to start murder, remove pawn from tracker
            startTicks.Remove(pawn);
            return true;
        }
        return false;
    }

    private static void ShowWarningMessage(Pawn pawn)
    {
        string messageText = $"Warning: {pawn.Name} has entered a murderous rage. They will begin their onslaught shortly.";
        Messages.Message(messageText, new LookTargets(pawn), MessageTypeDefOf.ThreatBig);
    }
}