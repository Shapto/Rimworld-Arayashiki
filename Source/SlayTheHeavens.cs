using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;
using static HarmonyLib.Code;

namespace Arayashiki
{
   public class SlayTheHeavens : DashAttackRun
   {
       private const float FlashRadius = 9f;

       public SlayTheHeavens(Pawn wielder, Pawn victim, Verb_ArayashikiSlash verb, float pointCost) : base(wielder, victim, verb, pointCost) { }

       protected override int DashAttackCount => 8;
       protected override int DashIntervalTicks => 6;
       protected override int FlurryCount => 24;
       protected override int FlurryIntervalTicks => 2;
       protected override int PauseBeforeFinisherTicks => 45;
       protected override int AxisReach => 2;

       protected override void Finisher()
       {
           Map map = victim.Map;
           Vector3 center = victim.DrawPos;
           string victimName = victim.LabelShortCap;

           float angle = DashAcrossTarget();

           verb.PlaySwing(victim, animate: true, sound: false);
           PlaySoundAt("Arayashiki_Finisher", map, victim.Position);
           SpawnFlash(map, center, FlashRadius);
           SpawnSlash(map, center, angle);

           victim.Kill(null);
           Messages.Message($"{victimName} has been severed.", MessageTypeDefOf.NeutralEvent, false);
           Arayashiki_Erasure.PayCost(wielder, pointCost);
       }
   }

    public static class SlayTheHeavensSequence
    {
        public static bool HasRoom(Pawn victim)
        {
            return OpenCellsAround(victim, 1).Any() || OpenCellsAround(victim, 2).Any();
        }

        public static List<IntVec3> OpenCellsAround(Pawn victim, int radius)
        {
            Map map = victim.Map;
            return GenRadial.RadialCellsAround(victim.Position, radius, false)
                .Where(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null)
                .ToList();
        }

        public static void Dash(Pawn wielder, IntVec3 to)
        {
            Map map = wielder.Map;
            Vector3 from = wielder.DrawPos;
            Vector3 end = to.ToVector3Shifted();

            int puffs = Mathf.Max(2, Mathf.RoundToInt((end - from).magnitude * 2f));
            for (int i = 0; i <= puffs; i++)
            {
                Vector3 p = Vector3.Lerp(from, end, i / (float)puffs);
                FleckMaker.ThrowDustPuff(p, map, 1.2f);
            }

            wielder.pather.StopDead();
            wielder.Position = to;
            wielder.Notify_Teleported(false, true);
        }
    }
}
