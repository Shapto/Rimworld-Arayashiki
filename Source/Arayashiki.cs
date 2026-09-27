using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static RimWorld.FleshTypeDef;

namespace Arayashiki
{
    /// <summary>
    /// Defines the properties for the Arayashiki Swap ability effect, specifying the target thing definition to switch
    /// to when the effect is applied.
    /// </summary>
    public class CompProperties_ArayashikiSwap : CompProperties_AbilityEffect
    {
        public ThingDef switchTo;

        public CompProperties_ArayashikiSwap()
        {
            compClass = typeof(CompAbilityEffect_ArayashikiSwap);
        }
    }

    /// <summary>
    /// Represents a mod extension that defines the sheathed and unsheathed forms of an item for use in modded content.
    /// </summary>
    /// <remarks>This extension is typically attached to a definition to specify alternate ThingDefs
    /// representing different states (such as sheathed or unsheathed) of a weapon or item. It enables modders to
    /// configure item state transitions without hardcoding logic.</remarks>
    public class ModExtension_Arayashiki : DefModExtension
    {
        public ThingDef sheathed;
        public ThingDef unsheathed;
    }

    /// <summary>
    /// Represents an ability effect that swaps the pawn's primary equipment with a specified weapon when the ability is
    /// applied.
    /// </summary>
    /// <remarks>This component is intended for use with abilities that require changing the pawn's weapon to
    /// a predefined type. The weapon to switch to is determined by the associated properties. This effect replaces the
    /// current primary equipment; any existing equipment is destroyed before the new weapon is added.</remarks>
    public class CompAbilityEffect_ArayashikiSwap : CompAbilityEffect
    {
        new CompProperties_ArayashikiSwap Props => (CompProperties_ArayashikiSwap)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = parent.pawn;
            ThingWithComps current = pawn?.equipment?.Primary;
            if (current == null) return;

            ThingWithComps newWeapon = (ThingWithComps)ThingMaker.MakeThing(Props.switchTo);
            pawn.equipment.DestroyEquipment(current);
            pawn.equipment.AddEquipment(newWeapon);
        }
    }

    /// <summary>
    /// Represents a thrown visual effect for an "Arayashiki Bladetrail" with support for flipping and scale jitter
    /// effects.
    /// </summary>
    /// <remarks>This class customizes the rendering of the bladetrail effect by allowing horizontal flipping
    /// and random scale variation. It is intended for use in visual effects where dynamic orientation and scaling are
    /// required. Inherits from MoteThrown and overrides the drawing behavior to achieve these effects.</remarks>
    public class Mote_ArayashikiBladetrail : MoteThrown
    {
        public bool flipped;
        public float scaleJitter = 1f;
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Material mat = Graphic.MatSingle;
            float xScale = Graphic.drawSize.x * scaleJitter * (flipped ? -1f : 1f);
            float zScale = Graphic.drawSize.y * scaleJitter;

            Matrix4x4 matrix = default;
            matrix.SetTRS(
                drawLoc,
                Quaternion.AngleAxis(exactRotation, Vector3.up),
                new Vector3(xScale, 1f, zScale)
            );
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
    }

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
        protected bool DoOneSwing()
        {
            Vector3 casterPos = CasterPawn.DrawPos;
            Vector3 targetPos = CurrentTarget.Thing != null && CurrentTarget.Thing.Spawned ? CurrentTarget.Thing.DrawPos : CurrentTarget.Cell.ToVector3Shifted();

            swingCount++;
            bool isCombo = swingCount >= 3;
            if (isCombo) swingCount = 0;

            bool result = base.TryCastShot();

            if (result) Arayashiki_Erasure.PayCost(CasterPawn, 10f);

            //swing sounds
            SoundDef swingSound = DefDatabase<SoundDef>.GetNamed("Arayashiki_Swing");
            SoundInfo soundInfo = SoundInfo.InMap(new TargetInfo(CasterPawn.Position, CasterPawn.Map));
            swingSound.PlayOneShot(soundInfo);

            //bladetrails
            string[] suffixes = { "A", "B", "C", "D" };
            ThingDef chosenDef = DefDatabase<ThingDef>.GetNamed("Arayashiki_Bladetrail" + suffixes[Rand.Range(0, 4)]);
            if (chosenDef == null) return false;

            Mote_ArayashikiBladetrail mote = (Mote_ArayashikiBladetrail)ThingMaker.MakeThing(chosenDef);
            mote.exactPosition = Vector3.Lerp(casterPos, targetPos, 0.75f);
            mote.exactRotation = (targetPos - casterPos).AngleFlat() + Rand.Range(-40f, 40f);
            mote.flipped = Rand.Bool;
            mote.scaleJitter = Rand.Range(0.85f, 1.15f);
            GenSpawn.Spawn(mote, CasterPawn.Position, CasterPawn.Map);

            return result;
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
            bool isCombo = swingCount >= 3;
            if (isCombo) swingCount = 0;

            bool result = DoOneSwing();
            if (isCombo)
            {
                DoOneSwing();
                DoOneSwing();
            }

            return result;
        }
    }

    /// <summary>
    /// Represents a specialized injury hediff that tracks its initial severity and enforces a minimum severity
    /// threshold when healed.
    /// </summary>
    /// <remarks>This class extends the behavior of a standard injury by recording the severity at the time
    /// the wound is added and ensuring that healing cannot reduce the severity below a predefined minimum value. This
    /// can be used to model wounds that cannot be fully healed or that retain a lasting effect.</remarks>
    public class Hediff_ArayashikiWound : Hediff_Injury
    {
        public float initialSeverity;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            initialSeverity = Severity;
        }

        public override void Heal(float amount)
        {
            base.Heal(amount);
            float floor = 0.05f;
            if (Severity <= floor)
            {
                Severity = floor;
            }
        }
    }

    /// <summary>
    /// Provides a HediffComp that periodically resets the severity of an Arayashiki wound if a nearby pawn is wielding
    /// a specified weapon. Inherits behavior from HediffComp_TendDuration.
    /// </summary>
    /// <remarks>This component checks every 250 ticks for pawns within a fixed radius who are wielding either
    /// the 'sheathed' or 'unsheathed' weapon defined in the ModExtension_Arayashiki. If such a pawn is found, the
    /// wound's severity is reset to its initial value. This behavior is specific to the Arayashiki wound mechanic and
    /// relies on the parent Hediff being a Hediff_ArayashikiWound.</remarks>
    public class HediffComp_TendArayashiki : HediffComp_TendDuration
    {
        private int ticksSinceCheck;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            ticksSinceCheck++;
            if (ticksSinceCheck < 250) return;
            ticksSinceCheck = 0;

            int radius = 6;

            ModExtension_Arayashiki mod = parent.def.GetModExtension<ModExtension_Arayashiki>();
            if (mod == null) return;
            Pawn victim = parent.pawn;
            if (victim == null) return;
            foreach (Pawn pawn in victim.Map.mapPawns.AllPawnsSpawned)
            {
                if((pawn?.equipment?.Primary?.def == mod.unsheathed || pawn?.equipment?.Primary?.def == mod.sheathed) && ((victim.Position.DistanceTo(pawn.Position) <= radius)))
                {
                    Hediff_ArayashikiWound wound = (Hediff_ArayashikiWound)parent;
                    parent.Severity = wound.initialSeverity;
                    tendTicksLeft = 0;
                }
            }
        }
    }

    /// <summary>
    /// Provides methods for applying the cost of Arayashiki Erasure to a pawn by removing memories, reducing skill
    /// levels, or removing traits as necessary.
    /// </summary>
    /// <remarks>This class is intended for use in scenarios where a pawn must pay a psychological or
    /// skill-based cost, such as in game mechanics involving memory erasure or personality alteration. All members are
    /// static and thread safety is not guaranteed.</remarks>
    public static class Arayashiki_Erasure
    {
        public static void PayCost(Pawn wielder, float cost)
        {
            var candidates = wielder.needs.mood.thoughts.memories.Memories
                 .Where(t => !t.permanent)
                 .OrderByDescending(t => Math.Abs(t.moodOffset))
                 .ToList();

            float remainingCost = cost;

            foreach (var candidate in candidates)
            {
                if (remainingCost <= 0) break;
                remainingCost -= Math.Abs(candidate.moodOffset);
                wielder.needs.mood.thoughts.memories.RemoveMemory(candidate);
                MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, "Memory erased", Color.magenta);
            }
            if (remainingCost > 0)
            {
                var skillCandidates = wielder.skills.skills
                    .Where(s => s.Level > 0 && s.def != SkillDefOf.Melee)
                    .OrderByDescending(s => s.Level)
                    .ToList();

                foreach (var skill in skillCandidates)
                {
                    if (remainingCost <= 0) break;
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{skill} -1", Color.magenta);
                    skill.Level--;
                    remainingCost -= 10;
                }
            }
            if (remainingCost > 0)
            {
                var traitCandidates = wielder.story.traits.allTraits
                    .OrderBy(t => Rand.Value)
                    .ToList();

                foreach (var trait in traitCandidates)
                {
                    if (remainingCost <= 0) break;
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{trait} erased", Color.red);
                    wielder.story.traits.RemoveTrait(trait);
                    remainingCost -= 100;
                }
            }
            if (remainingCost > 0)
            {
                var skill = wielder.skills.GetSkill(SkillDefOf.Melee);
                while (skill.Level > 0 && remainingCost > 0)
                {
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{skill} -1", Color.magenta);
                    skill.Level--;
                    remainingCost -= 10;
                }
            }
        }
    }
}
