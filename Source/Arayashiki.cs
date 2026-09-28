using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static RimWorld.FleshTypeDef;
using static RimWorld.MechClusterSketch;

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
    [StaticConstructorOnStartup]
    public class Mote_ArayashikiBladetrail : MoteThrown
    {
        public bool flipped;
        public float scaleJitter = 1f;
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private const float SizeMultiplier = 2.2f;  // overall size
        private const float RippleSpeed = 2f;       // wave speed
        private const float RippleAmount = 0.02f;   // wobble of +-2% size
        private const int Echoes = 1;               // extra fainter copies
        private const float EchoGrowth = 0.10f;     // each echo is this much bigger than the last
        private const float EchoFade = 0.25f;       // each echo's opacity relative to the one before
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float alpha = Alpha;
            if (alpha <= 0f) return;

            Material mat = Graphic.MatSingle;
            Mesh mesh = flipped ? MeshPool.plane10Flip : MeshPool.plane10;
            Color baseColor = mat.color;

            float t = AgeSecs;
            float calm = 1f / (1f + t * 0.15f);   // ripples settle over time

            for (int i = 0; i <= Echoes; i++)
            {
                float wave = Mathf.Sin(t * RippleSpeed - i * 0.9f) * RippleAmount * calm;
                float scale = scaleJitter * SizeMultiplier * (1f + i * EchoGrowth + wave);

                Color c = baseColor;
                c.a *= alpha * Mathf.Pow(EchoFade, i);
                block.SetColor("_Color", c);

                Vector3 pos = drawLoc;
                pos.y -= 0.0005f * i;   // nudge echoes slightly behind the main trail

                Matrix4x4 matrix = default;
                matrix.SetTRS(
                    pos,
                    Quaternion.AngleAxis(exactRotation, Vector3.up),
                    new Vector3(Graphic.drawSize.x * scale, 1f, Graphic.drawSize.y * scale)
                );
                Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0, block);
            }
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
}
