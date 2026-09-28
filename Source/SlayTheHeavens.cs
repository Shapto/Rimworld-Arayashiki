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
    /// <summary>
    /// Defines configuration properties for the Slay the Heavens ability effect.
    /// </summary>
    /// <remarks>This class is used to specify settings for the Slay the Heavens ability, including the point
    /// cost required to activate the effect. It is typically used in conjunction with ability definitions to control
    /// gameplay balance.</remarks>
    public class CompProperties_SlayTheHeavens : CompProperties_AbilityEffect
    {
        public float pointCost = 50f;

        public CompProperties_SlayTheHeavens()
        {
            compClass = typeof(CompAbilityEffect_SlayTheHeavens);
        }
    }

    /// <summary>
    /// Represents an ability effect that initiates the Slay the Heavens sequence between the ability user and the
    /// target pawn.
    /// </summary>
    /// <remarks>This component is intended for use with abilities that require a special sequence to be
    /// triggered between two pawns. The effect will only be applied if both the ability user and the target are valid
    /// pawns on a map, and if the target can participate in the Slay the Heavens sequence. The point cost for the
    /// sequence is determined by the associated ability properties.</remarks>
    public class CompAbilityEffect_SlayTheHeavens : CompAbilityEffect
    {
        new CompProperties_SlayTheHeavens Props => (CompProperties_SlayTheHeavens)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn wielder = parent.pawn;
            Pawn victim = target.Pawn;
            if (wielder == null || victim == null || wielder.Map == null) return;

            if (!SlayTheHeavensSequence.HasRoom(victim)) return;

            SlayTheHeavensSequence.Start(wielder, victim, Props.pointCost);
        }
    }

    /// <summary>
    /// Represents the execution of the "Slay the Heavens" special attack sequence between two pawns, coordinating
    /// movement, visual effects, and attack timing.
    /// </summary>
    /// <remarks>This class manages the full sequence of the "Slay the Heavens" finisher, including dashing,
    /// flurry attacks, visual effects, and the final strike. It is intended to be used as a one-off orchestrator for a
    /// single attack event. The sequence is initiated by calling the Begin method. The class assumes that both the
    /// wielder and victim pawns are alive, spawned, and on the same map at the time of execution. Thread safety is not
    /// guaranteed.</remarks>
    public class SlayTheHeavensRun
    {
        private const int DashAttackCount = 8;
        private const int DashIntervalTicks = 6;        // time between attacks
        private const int PauseBeforeStrikeTicks = 45;   // the pause before the final strike
        private const int FlurryCount = 24;
        private const int FlurryIntervalTicks = 2;
        private const int AxisReach = 2;
        private readonly float pointCost;
        private const float FlashRadius = 9f;
        private IntVec3 axisStep;
        private int sideIndex;
        private static List<ThingDef> slashDefs;
        private readonly Pawn wielder;
        private readonly Pawn victim;
        private readonly Verb_ArayashikiSlash verb;

        public SlayTheHeavensRun(Pawn wielder, Pawn victim, Verb_ArayashikiSlash verb, float pointCost)
        {
            this.wielder = wielder;
            this.victim = victim;
            this.verb = verb;
            this.pointCost = pointCost;
        }

        private static List<ThingDef> SlashDefs
        {
            get
            {
                if (slashDefs == null)
                {
                    slashDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(d => d.defName != null
                                 && d.defName.StartsWith("Arayashiki_FinisherSlash")
                                 && !d.defName.EndsWith("MID"))
                        .ToList();
                }
                return slashDefs;
            }
        }

        private void SpawnFinisherSlash(Map map, Vector3 center, float angle)
        {
            if (SlashDefs.Count == 0) return;

            var mote = (Mote_ArayashikiFinisherSlash)ThingMaker.MakeThing(SlashDefs.RandomElement());
            mote.exactPosition = center;
            mote.exactRotation = angle;
            GenSpawn.Spawn(mote, center.ToIntVec3(), map);
        }

        private void SpawnRendFlash(Map map, Vector3 center)
        {
            var flash = (Mote_ArayashikiRendFlash)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Arayashiki_RendFlash"));
            flash.exactPosition = center;
            flash.radius = FlashRadius;
            GenSpawn.Spawn(flash, center.ToIntVec3(), map);
        }

        private void FreezeTarget(int ticks)
        {
            victim.pather?.StopDead();
            victim.stances.stunner.StunFor(ticks, wielder, false, false);
        }

        private bool StillValid =>
            !wielder.Dead && !victim.Dead && wielder.Spawned && victim.Spawned && wielder.Map == victim.Map;

        public void Begin()
        {
            axisStep = ComputeAxisStep();
            var scheduler = Current.Game.GetComponent<GameComponent_ArayashikiDelay>();
            int t = 0;

            for (int i = 0; i < DashAttackCount; i++)
            {
                scheduler.Schedule(t, DashAttack);
                t += DashIntervalTicks;
            }

            scheduler.Schedule(t, FinalDash);
            scheduler.Schedule(t + FlurryIntervalTicks, PlayFlurrySound);
            for (int i = 0; i < FlurryCount; i++)
            {
                int index = i;
                scheduler.Schedule(t + FlurryIntervalTicks * (i + 1), () => FlurrySwing(index));
            }

            t += FlurryIntervalTicks * (FlurryCount + 1);
            scheduler.Schedule(t + PauseBeforeStrikeTicks, FinalStrike);
            FreezeTarget(t + PauseBeforeStrikeTicks + 10);
        }

        private IntVec3 ComputeAxisStep()
        {
            Vector3 dir = victim.DrawPos - wielder.DrawPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return new IntVec3(1, 0, 0);

            dir.Normalize();
            IntVec3 step = new IntVec3(Mathf.RoundToInt(dir.x), 0, Mathf.RoundToInt(dir.z));
            return step == IntVec3.Zero ? new IntVec3(1, 0, 0) : step;
        }

        private static bool IsOpen(IntVec3 c, Map map)
        {
            return c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null;
        }

        private IntVec3 PickCell(int maxReach = AxisReach)
        {
            Map map = victim.Map;
            int sign = (sideIndex % 2 == 0) ? 1 : -1;
            sideIndex++;

            for (int reach = maxReach; reach >= 1; reach--)
            {
                IntVec3 c = victim.Position + axisStep * (sign * reach);
                if (IsOpen(c, map)) return c;
            }

            var cells = SlayTheHeavensSequence.OpenCellsAround(victim, 1);
            if (cells.Count == 0) cells = SlayTheHeavensSequence.OpenCellsAround(victim, 2);
            return cells.Count > 0 ? cells.RandomElement() : IntVec3.Invalid;
        }

        private void DashAttack()
        {
            if (!StillValid) return;
            IntVec3 cell = PickCell();
            if (!cell.IsValid) return;

            SlayTheHeavensSequence.Dash(wielder, cell);
            verb.PlaySwing(victim);
        }

        private void FlurrySwing(int index)
        {
            if (!StillValid) return;

            bool accent = index % 3 == 0;   // full animation and sound on every third swing only
            verb.PlaySwing(victim, animate: accent, sound: false);
        }

        private void FinalDash()
        {
            if (!StillValid) return;
            IntVec3 cell = PickCell();
            if (!cell.IsValid) return;

            SlayTheHeavensSequence.Dash(wielder, cell);
            wielder.rotationTracker.FaceTarget(victim);
        }

        private void PlayFinisherSound()
        {
            SoundDef finisher = DefDatabase<SoundDef>.GetNamed("Arayashiki_Finisher");
            finisher.PlayOneShot(SoundInfo.InMap(new TargetInfo(victim.Position, victim.Map)));
        }

        private void PlayFlurrySound()
        {
            if (!StillValid) return;

            SoundDef flurry = DefDatabase<SoundDef>.GetNamed("Arayashiki_Flurry");
            flurry.PlayOneShot(SoundInfo.InMap(new TargetInfo(victim.Position, victim.Map)));
        }

        private void FinalStrike()
        {
            if (!StillValid) return;

            Map map = victim.Map;
            Vector3 center = victim.DrawPos;

            Vector3 from = wielder.DrawPos;
            IntVec3 cell = PickCell(1);
            if (cell.IsValid) SlayTheHeavensSequence.Dash(wielder, cell);

            Vector3 dashDir = wielder.DrawPos - from;
            float angle = dashDir.sqrMagnitude > 0.01f ? dashDir.AngleFlat() : axisStep.ToVector3().AngleFlat();

            verb.PlaySwing(victim, animate: true, sound: false);
            PlayFinisherSound();
            SpawnRendFlash(map, center);
            SpawnFinisherSlash(map, center, angle);

            victim.Kill(null);
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

        public static void Start(Pawn wielder, Pawn victim, float pointCost)
        {
            Verb_ArayashikiSlash verb = wielder.equipment?.PrimaryEq?.AllVerbs
                .OfType<Verb_ArayashikiSlash>().FirstOrDefault();
            if (verb == null) return;

            new SlayTheHeavensRun(wielder, victim, verb, pointCost).Begin();
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
