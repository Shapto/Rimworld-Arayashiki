using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;
using static UnityEngine.GraphicsBuffer;

namespace Arayashiki
{
    /// <summary>
    /// Represents a melee attack verb that performs an Arayashiki Slash, spawning a visual blade trail effect when
    /// used.
    /// </summary>
    /// <remarks>This class extends the base melee attack behavior by creating a visual effect at the
    /// attacker's position each time the attack is executed. The effect is chosen randomly from a set of predefined
    /// blade trail variants. Use this verb to provide enhanced visual feedback for special melee attacks.</remarks>
    public class Verb_ArayashikiSlash : Verb_MeleeAttackDamage
    {
        private int swingCount;

        private const int comboExtraHits = 2;

        private const int comboIntervalTicks = 12;

        private const int startDelayTicks = 12;

        private static List<ThingDef> bladetrailDefs;

        private static List<ThingDef> BladetrailDefs
        {
            get
            {
                if (bladetrailDefs == null)
                {
                    bladetrailDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(d => d.defName != null && d.defName.StartsWith("Arayashiki_Bladetrail"))
                        .ToList();
                }
                return bladetrailDefs;
            }
        }

        public bool Strike(LocalTargetInfo target, bool primary)
        {
            currentTarget = target;

            Vector3 casterPos = CasterPawn.DrawPos;
            Vector3 targetPos = CurrentTarget.Thing != null && CurrentTarget.Thing.Spawned ? CurrentTarget.Thing.DrawPos : CurrentTarget.Cell.ToVector3Shifted();

            bool result;

            if (primary)
            {
                result = base.TryCastShot();
                if (result) Arayashiki_Erasure.PayCost(CasterPawn, 10f);
            }
            else
            {
                ApplyMeleeDamageToTarget(target);   // bonus hit: damage only, skips the cooldown-gated vanilla swing
                result = true;
            }
            if (CasterPawn.Map == null) return result;

            //swing sounds
            SoundDef swingSound = DefDatabase<SoundDef>.GetNamed("Arayashiki_Swing");
            SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(CasterPawn.Position, CasterPawn.Map));
            swingSound.PlayOneShot(soundInfo);

            //bladetrails
            if (BladetrailDefs.Count == 0) return result;
            ThingDef chosenDef = BladetrailDefs.RandomElement();
            if (chosenDef == null) return false;

            Mote_ArayashikiBladetrail mote = (Mote_ArayashikiBladetrail)ThingMaker.MakeThing(chosenDef);
            mote.exactPosition = Vector3.Lerp(casterPos, targetPos, 0.75f);
            mote.exactRotation = (targetPos - casterPos).AngleFlat() + Rand.Range(-40f, 40f);
            mote.flipped = Rand.Bool;
            mote.scaleJitter = Rand.Range(0.85f, 1.15f);
            Log.Message($"[Arayashiki] trail {chosenDef.defName} flipped={mote.flipped} primary={primary}");
            GenSpawn.Spawn(mote, CasterPawn.Position, CasterPawn.Map);

            return result;
        }

        private bool TargetStillHittable(LocalTargetInfo target)
        {
            if (CasterPawn == null || !CasterPawn.Spawned || CasterPawn.Dead || CasterPawn.Downed) return false;
            if (EquipmentSource == null || EquipmentSource.Destroyed) return false;

            Thing t = target.Thing;
            if (t == null || t.Destroyed || !t.Spawned) return false;
            if (t is Pawn p && p.Dead) return false;
            return CasterPawn.Position.DistanceTo(t.Position) <= 1.5f;
        }

        public static void StrikeSequence(Verb_ArayashikiSlash verb, LocalTargetInfo target, int hits)
        {
            for (int i = 0; i < hits; i++)
            {
                if (!verb.TargetStillHittable(target)) break;
                verb.Strike(target, false);
            }
            return;
        }

        public static void StrikeSequence(Verb_ArayashikiSlash verb, LocalTargetInfo target, int hits, int intervalTicks, int startDelayTicks = 0)
        {
            var scheduler = Current.Game.GetComponent<GameComponent_ArayashikiDelay>();
            for (int i = 0; i < hits; i++)
            {
                scheduler.Schedule(startDelayTicks + intervalTicks * i, () =>
                {
                    if (verb.TargetStillHittable(target))
                    verb.Strike(target, false);
                });
            }
        }

        /// <summary>
        /// Attempts to perform a swing attack, triggering a combo sequence on every third swing.
        /// </summary>
        /// <remarks>When called, this method increments the swing count. On every third consecutive call,
        /// it triggers a combo by performing two additional swing attacks. The return value reflects only the result of
        /// the initial swing attempt, not the combo swings.</remarks>
        /// <returns>true if the initial swing attack was successfully performed; otherwise, false.</returns>
        protected override bool TryCastShot()
        {
            swingCount++;
            bool isCombo = swingCount >= 4;
            if (isCombo) swingCount = 0;

            LocalTargetInfo target = CurrentTarget;
            bool result = Strike(target, true);

            if (isCombo) StrikeSequence(this, target, comboExtraHits, comboIntervalTicks, 12);

            return result;
        }
    }
}
