using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Arayashiki
{
    public static class ArayashikiOblivion
    {
        public static void Erase(Pawn victim)
        {
            if (victim == null || victim.Destroyed) return;

            foreach (Pawn other in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
            {
                if (other == victim) continue;

                var memories = other.needs?.mood?.thoughts?.memories;
                if (memories != null)
                {
                    foreach (var m in memories.Memories.Where(t => t.otherPawn == victim).ToList())
                        memories.RemoveMemory(m);
                }

                if (other.relations != null)
                {
                    foreach (var r in other.relations.DirectRelations.Where(x => x.otherPawn == victim).ToList())
                        other.relations.RemoveDirectRelation(r.def, victim);
                }
            }

            //byebye
            victim.Destroy(DestroyMode.Vanish);
        }
    }

    public class ErasingRun : DashAttackRun
    {
        private const int BarrageSlashes = 7;          // extra slashes on top of the main one
        private const int BarrageIntervalTicks = 3;
        private const int EraseDelayTicks = 25;        // how long the flash and slashes get to show before the target vanishes

        public ErasingRun(Pawn wielder, Pawn victim, Verb_ArayashikiSlash verb, float pointCost)
            : base(wielder, victim, verb, pointCost) { }

        protected override int DashAttackCount => 30;
        protected override int DashIntervalTicks => 12;
        protected override int FlurryCount => 0;              
        protected override int FlurryIntervalTicks => 1;     
        protected override int PauseBeforeFinisherTicks => 30;
        protected override int ExtraFreezeTicks => EraseDelayTicks + 10;

        private const int FastestIntervalTicks = 1;
        
        private const int DashRadius = 2;

        protected override IntVec3 ChooseCell(int reach)
        {
            var cells = SlayTheHeavensSequence.OpenCellsAround(victim, reach <= 1 ? 1 : DashRadius);
            if (cells.Count == 0) cells = SlayTheHeavensSequence.OpenCellsAround(victim, 2);
            return cells.Count > 0 ? cells.RandomElement() : IntVec3.Invalid;
        }
        protected override int GetDashInterval(int index)
        {
            float u = index / (float)Mathf.Max(1, DashAttackCount - 1);
            float interval = DashIntervalTicks * Mathf.Pow((float)FastestIntervalTicks / DashIntervalTicks, u);
            return Mathf.Max(FastestIntervalTicks, Mathf.RoundToInt(interval));
        }

        protected override void PlayDashSwing(int index)
        {
            bool full = GetDashInterval(index) >= 4 || index % 3 == 0;
            verb.PlaySwing(victim, animate: full, sound: full);
        }

        protected override void Finisher()
        {
            Map map = victim.Map;
            Vector3 center = victim.DrawPos;
            Pawn target = victim;
            Pawn user = wielder;
            float cost = pointCost;

            float angle = DashAcrossTarget();

            verb.PlaySwing(victim, animate: true, sound: false);
            PlaySoundAt("Arayashiki_Finisher", map, victim.Position);

            SpawnFlash(map, center, 18f);
            SpawnFlash(map, center, 10f);
            SpawnSlash(map, center, angle);

            var scheduler = Current.Game.GetComponent<GameComponent_ArayashikiDelay>();
            for (int i = 1; i <= BarrageSlashes; i++)
            {
                float a = angle + Rand.Range(-80f, 80f);
                Vector3 offset = new Vector3(Rand.Range(-1.5f, 1.5f), 0f, Rand.Range(-1.5f, 1.5f));
                scheduler.Schedule(i * BarrageIntervalTicks, () => SpawnSlash(map, center + offset, a));
            }

            scheduler.Schedule(EraseDelayTicks, () =>
            {
                ArayashikiOblivion.Erase(target);
                Messages.Message("Something is missing.", MessageTypeDefOf.NeutralEvent, false);
                if (!user.Dead && user.Spawned) Arayashiki_Erasure.PayCost(user, cost);
            });
        }
    }
}
