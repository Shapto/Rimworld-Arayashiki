using RimWorld;
using System;
using UnityEngine;
using Verse;

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
}
