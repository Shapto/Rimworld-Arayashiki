using RimWorld;
using System;
using UnityEngine;
using Verse;
using Verse.AI;
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
    /// Represents a melee attack verb that performs an Arayashiki Slash, spawning a visual blade trail effect when
    /// used.
    /// </summary>
    /// <remarks>This class extends the base melee attack behavior by creating a visual effect at the
    /// attacker's position each time the attack is executed. The effect is chosen randomly from a set of predefined
    /// blade trail variants. Use this verb to provide enhanced visual feedback for special melee attacks.</remarks>
    public class Verb_ArayashikiSlash : Verb_MeleeAttackDamage
    {
        protected override bool TryCastShot()
        {
            Vector3 casterPos = CasterPawn.DrawPos;
            Vector3 targetPos = CurrentTarget.Thing != null && CurrentTarget.Thing.Spawned ? CurrentTarget.Thing.DrawPos : CurrentTarget.Cell.ToVector3Shifted();

            bool result = base.TryCastShot();

            string[] suffixes = { "A", "B", "C", "D" };
            ThingDef chosenDef = DefDatabase<ThingDef>.GetNamed("Arayashiki_Bladetrail" + suffixes[Rand.Range(0, 4)]);
            if (chosenDef == null) return false;

            Mote mote = (Mote)ThingMaker.MakeThing(chosenDef);
            mote.exactPosition = CasterPawn.DrawPos;
            mote.exactRotation = (targetPos - casterPos).AngleFlat();
            GenSpawn.Spawn(mote, CasterPawn.Position, CasterPawn.Map);

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

            int radius = 10;

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
}
