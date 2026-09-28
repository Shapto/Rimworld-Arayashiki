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

        /// <summary>
        /// Gets a cached list of all ThingDef objects whose defName starts with "Arayashiki_Bladetrail".
        /// </summary>
        /// <remarks>The returned list is initialized on first access and cached for subsequent calls. The
        /// list reflects the set of matching ThingDef instances available at the time of first access; changes to the
        /// underlying DefDatabase after initialization are not reflected.</remarks>
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
            Vector3 targetPos = target.Thing != null && target.Thing.Spawned ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();

            bool result;
            if (primary)
            {
                result = base.TryCastShot();
                if (result) Arayashiki_Erasure.PayCost(CasterPawn, 10f);
            }
            else
            {
                CasterPawn.rotationTracker.FaceTarget(target);
                CasterPawn.Drawer.Notify_MeleeAttackOn(target.Thing);
                ApplyMeleeDamageToTarget(target);
                result = true;
            }
            SwingEffects(casterPos, targetPos);
            return result;
        }

        public void PlaySwing(LocalTargetInfo target, bool animate = true, bool sound = true)
        {
            if (CasterPawn.Map == null) return;

            Vector3 casterPos = CasterPawn.DrawPos;
            Vector3 targetPos = target.Thing != null && target.Thing.Spawned ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();

            CasterPawn.rotationTracker.FaceTarget(target);
            if (animate)
            {
                CasterPawn.Drawer.Notify_MeleeAttackOn(target.Thing);
                CasterPawn.stances.SetStance(new Stance_Cooldown(12, target, this));
            }

            SwingEffects(casterPos, targetPos, sound);
        }

        private void SwingEffects(Vector3 casterPos, Vector3 targetPos, bool sound = true)
        {
            if (CasterPawn.Map == null) return;

            if (sound)
            {
                SoundDef swingSound = DefDatabase<SoundDef>.GetNamed("Arayashiki_Swing");
                SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(CasterPawn.Position, CasterPawn.Map));
                swingSound.PlayOneShot(soundInfo);
            }

            //bladetrails
            if (BladetrailDefs.Count == 0) return;
            ThingDef chosenDef = BladetrailDefs.RandomElement();

            Mote_ArayashikiBladetrail mote = (Mote_ArayashikiBladetrail)ThingMaker.MakeThing(chosenDef);
            mote.exactPosition = Vector3.Lerp(casterPos, targetPos, 0.75f);
            mote.exactRotation = (targetPos - casterPos).AngleFlat() + Rand.Range(-40f, 40f);
            mote.flipped = Rand.Bool;
            mote.scaleJitter = Rand.Range(0.85f, 1.15f);
            GenSpawn.Spawn(mote, CasterPawn.Position, CasterPawn.Map);
        }

        /// <summary>
        /// Determines whether the specified target is currently valid and within range to be hit by the caster.
        /// </summary>
        /// <remarks>This method returns false if the caster or their equipment is not present, destroyed,
        /// dead, or downed, or if the target is not a valid, alive, and spawned entity. Use this method to verify that
        /// a target can still be affected by actions requiring proximity.</remarks>
        /// <param name="target">The target to check for validity and hittability. Must represent a spawned, non-destroyed object or pawn.</param>
        /// <returns>true if the target is alive, spawned, not destroyed, and within 1.5 units of the caster; otherwise, false.</returns>
        private bool TargetStillHittable(LocalTargetInfo target)
        {
            if (CasterPawn == null || !CasterPawn.Spawned || CasterPawn.Dead || CasterPawn.Downed) return false;
            if (EquipmentSource == null || EquipmentSource.Destroyed) return false;

            Thing t = target.Thing;
            if (t == null || t.Destroyed || !t.Spawned) return false;
            if (t is Pawn p && p.Dead) return false;
            return CasterPawn.Position.DistanceTo(t.Position) <= 1.5f;
        }

        /// <summary>
        /// Performs a sequence of strike actions against the specified target using the provided verb, up to the
        /// specified number of hits or until the target is no longer hittable.
        /// </summary>
        /// <remarks>The method stops striking if the target is no longer hittable before reaching the
        /// specified number of hits. No action is taken if hits is zero.</remarks>
        /// <param name="verb">The verb used to perform each strike against the target. Cannot be null.</param>
        /// <param name="target">The target to be struck. The sequence stops if the target becomes unhittable.</param>
        /// <param name="hits">The maximum number of strikes to attempt. Must be greater than or equal to zero.</param>
        public static void StrikeSequence(Verb_ArayashikiSlash verb, LocalTargetInfo target, int hits)
        {
            for (int i = 0; i < hits; i++)
            {
                if (!verb.TargetStillHittable(target)) break;
                verb.Strike(target, false);
            }
            return;
        }

        /// <summary>
        /// Schedules a sequence of strike actions using the specified verb against a target, with a defined number of
        /// hits and timing intervals.
        /// </summary>
        /// <remarks>Each strike is only performed if the target is still valid and hittable at the
        /// scheduled time. The strikes are scheduled asynchronously and may not occur if the target becomes invalid
        /// before execution.</remarks>
        /// <param name="verb">The verb instance used to perform each strike action. Cannot be null.</param>
        /// <param name="target">The target to be struck by the verb. Must be a valid and hittable target at the time of each strike.</param>
        /// <param name="hits">The total number of strike actions to schedule. Must be greater than zero.</param>
        /// <param name="intervalTicks">The number of game ticks to wait between each consecutive strike.</param>
        /// <param name="startDelayTicks">The number of game ticks to wait before the first strike is performed. Defaults to 0.</param>
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
