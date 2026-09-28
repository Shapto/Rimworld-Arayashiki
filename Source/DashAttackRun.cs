using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Arayashiki
{
    public abstract class DashAttackRun
    {
        protected readonly Pawn wielder;
        protected readonly Pawn victim;
        protected readonly Verb_ArayashikiSlash verb;
        protected readonly float pointCost;

        private IntVec3 axisStep;
        private int sideIndex;

        protected abstract int DashAttackCount { get; }
        protected abstract int DashIntervalTicks { get; }
        protected abstract int FlurryCount { get; }
        protected abstract int FlurryIntervalTicks { get; }
        protected abstract int PauseBeforeFinisherTicks { get; }

        protected virtual int AxisReach => 2;
        protected virtual int FlurryAccentEvery => 3; 
        protected virtual int FlurryTrailEvery => 2;
        protected virtual int ExtraFreezeTicks => 0;
        protected virtual string FlurrySoundName => "Arayashiki_Flurry";

        protected abstract void Finisher();

        protected DashAttackRun(Pawn wielder, Pawn victim, Verb_ArayashikiSlash verb, float pointCost)
        {
            this.wielder = wielder;
            this.victim = victim;
            this.verb = verb;
            this.pointCost = pointCost;
        }

        protected virtual int GetDashInterval(int index) => DashIntervalTicks;

        protected virtual void PlayDashSwing(int index) => verb.PlaySwing(victim);

        protected bool StillValid =>
            !wielder.Dead && !victim.Dead && wielder.Spawned && victim.Spawned && wielder.Map == victim.Map;

        public void Begin()
        {
            axisStep = ComputeAxisStep();

            var scheduler = Current.Game.GetComponent<GameComponent_ArayashikiDelay>();
            int t = 0;

            for (int i = 0; i < DashAttackCount; i++)
            {
                int index = i;
                scheduler.Schedule(t, () => DashAttack(index));
                t += GetDashInterval(i);
            }

            scheduler.Schedule(t, FinalDash);   // staging dash beside the target

            if (FlurryCount > 0)
            {
                scheduler.Schedule(t + FlurryIntervalTicks, PlayFlurrySound);
                for (int i = 0; i < FlurryCount; i++)
                {
                    int index = i;   // copy for the lambda
                    scheduler.Schedule(t + FlurryIntervalTicks * (i + 1), () => FlurrySwing(index));
                }
                t += FlurryIntervalTicks * (FlurryCount + 1);
            }

            scheduler.Schedule(t + PauseBeforeFinisherTicks, FinalStrike);

            FreezeTarget(t + PauseBeforeFinisherTicks + 10 + ExtraFreezeTicks);
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

        private IntVec3 PickAlongAxis(int maxReach)
        {
            Map map = victim.Map;
            int sign = (sideIndex % 2 == 0) ? 1 : -1;
            sideIndex++;

            for (int reach = maxReach; reach >= 1; reach--)
            {
                IntVec3 c = victim.Position + axisStep * (sign * reach);
                if (IsOpen(c, map)) return c;
            }

            return PickRandomAround();
        }

        protected IntVec3 PickRandomAround()
        {
            var cells = SlayTheHeavensSequence.OpenCellsAround(victim, 1);
            if (cells.Count == 0) cells = SlayTheHeavensSequence.OpenCellsAround(victim, 2);
            return cells.Count > 0 ? cells.RandomElement() : IntVec3.Invalid;
        }

        // where the next dash lands Default: back and forth along one line through the target
        protected virtual IntVec3 ChooseCell(int reach) => PickAlongAxis(reach);

        private void DashAttack(int index)
        {
            if (!StillValid) return;
            IntVec3 cell = ChooseCell(AxisReach);
            if (!cell.IsValid) return;

            SlayTheHeavensSequence.Dash(wielder, cell);
            PlayDashSwing(index);
        }

        private void FinalDash()
        {
            if (!StillValid) return;
            IntVec3 cell = ChooseCell(AxisReach);
            if (!cell.IsValid) return;

            SlayTheHeavensSequence.Dash(wielder, cell);
            wielder.rotationTracker.FaceTarget(victim);
        }

        protected float DashAcrossTarget()
        {
            Vector3 from = wielder.DrawPos;
            IntVec3 cell = ChooseCell(1);
            if (cell.IsValid) SlayTheHeavensSequence.Dash(wielder, cell);

            Vector3 dir = wielder.DrawPos - from;
            return dir.sqrMagnitude > 0.01f ? dir.AngleFlat() : axisStep.ToVector3().AngleFlat();
        }

        private void FlurrySwing(int index)
        {
            if (!StillValid) return;

            bool accent = (index + 1) % FlurryAccentEvery == 0;
            bool trail = (index + 1) % FlurryTrailEvery == 0;
            verb.PlaySwing(victim, animate: accent, sound: false);
        }

        private void PlayFlurrySound()
        {
            if (!StillValid) return;
            PlaySoundAt(FlurrySoundName, victim.Map, victim.Position);
        }

        private void FreezeTarget(int ticks)
        {
            victim.pather?.StopDead();
            victim.stances.stunner.StunFor(ticks, wielder, false, false);
        }

        private void FinalStrike()
        {
            if (!StillValid) return;
            Finisher();
        }

        protected static void PlaySoundAt(string defName, Map map, IntVec3 cell)
        {
            SoundDef def = DefDatabase<SoundDef>.GetNamed(defName);
            def.PlayOneShot(SoundInfo.InMap(new TargetInfo(cell, map)));
        }

        private static List<ThingDef> slashDefs;

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

        protected static void SpawnSlash(Map map, Vector3 center, float angle)
        {
            if (map == null || SlashDefs.Count == 0) return;

            var mote = (Mote_ArayashikiFinisherSlash)ThingMaker.MakeThing(SlashDefs.RandomElement());
            mote.exactPosition = center;
            mote.exactRotation = angle;
            GenSpawn.Spawn(mote, center.ToIntVec3(), map);
        }

        protected static void SpawnFlash(Map map, Vector3 center, float radius)
        {
            if (map == null) return;

            var flash = (Mote_ArayashikiRendFlash)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Arayashiki_RendFlash"));
            flash.exactPosition = center;
            flash.radius = radius;
            GenSpawn.Spawn(flash, center.ToIntVec3(), map);
        }
    }
}
