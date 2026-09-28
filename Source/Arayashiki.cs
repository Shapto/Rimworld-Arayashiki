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
            pawn.equipment.Remove(current);
            current.Destroy();
            pawn.equipment.AddEquipment(newWeapon);
        }
    }

    /// <summary>
    /// Defines configuration properties for the Arayashiki abilities component, specifying which abilities are
    /// available to the component.
    /// </summary>
    /// <remarks>This class is typically used to assign a set of abilities to an entity via XML or code
    /// configuration. It should be associated with a component of type CompArayashikiAbilities.</remarks>
    public class CompProperties_ArayashikiAbilities : CompProperties
    {
        public List<AbilityDef> abilities;

        public CompProperties_ArayashikiAbilities()
        {
            compClass = typeof(CompArayashikiAbilities);
        }
    }

    /// <summary>
    /// Provides a component that grants or removes specified abilities to a pawn when the associated item is equipped
    /// or unequipped.
    /// </summary>
    /// <remarks>This component is intended to be attached to items that bestow abilities upon being equipped
    /// by a pawn. When the item is equipped, all abilities defined in the component's properties are granted to the
    /// pawn. When the item is unequipped, those abilities are removed. The component requires that both the abilities
    /// list and the pawn's abilities tracker are available for correct operation.</remarks>
    public class CompArayashikiAbilities : ThingComp
    {
        private CompProperties_ArayashikiAbilities Props => (CompProperties_ArayashikiAbilities)props;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (Props.abilities == null || pawn.abilities == null) return;
            foreach (AbilityDef def in Props.abilities) pawn.abilities.GainAbility(def);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (Props.abilities == null || pawn.abilities == null) return;
            foreach (AbilityDef def in Props.abilities) pawn.abilities.RemoveAbility(def);
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
    /// Represents a visual effect for the Arayashiki Finisher Slash, rendering an animated slash with fading and core
    /// effects.
    /// </summary>
    /// <remarks>This class customizes the drawing behavior of a thrown mote to display a slash effect with
    /// specific timing and visual transitions. The effect includes a fading slash and a core that phases in and out
    /// over time. Typically used to visually represent a finishing move or special attack in the game. This class is
    /// not thread-safe.</remarks>
    public class Mote_ArayashikiFinisherSlash : MoteThrown
    {
        private const float SlashFadeIn = 0.08f;    // the slash appears
        private const float CoreStart = 0.25f;      // the black core starts phasing in
        private const float CoreFadeIn = 0.25f;
        private const float HoldSeconds = 1.0f;     // full thickness while the area is pure white
        private const float TotalSeconds = 8.0f;    // fully gone by here
        private const float ThinExponent = 2.0f;    // higher = thins faster at first, leaving a long sliver
        private static readonly bool LineRunsAlongX = false;   // set false if your slash image is a vertical line

        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private static Material coreMat;

        private static Material CoreMat
        {
            get
            {
                if (coreMat == null)
                    coreMat = DefDatabase<ThingDef>.GetNamed("Arayashiki_FinisherSlashMID").graphic.MatSingle;
                return coreMat;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float t = AgeSecs;
            if (t >= TotalSeconds) return;

            float u = Mathf.InverseLerp(HoldSeconds, TotalSeconds, t);
            float thickness = Mathf.Pow(1f - u, ThinExponent);
            if (thickness <= 0.001f) return;

            float slashAlpha = Mathf.Clamp01(t / SlashFadeIn);
            float coreAlpha = Mathf.Clamp01((t - CoreStart) / CoreFadeIn);

            float baseY = def.altitudeLayer.AltitudeFor();
            Quaternion rot = Quaternion.AngleAxis(exactRotation, Vector3.up);
            Vector3 scale = new Vector3(
                Graphic.drawSize.x * (LineRunsAlongX ? 1f : thickness),
                1f,
                Graphic.drawSize.y * (LineRunsAlongX ? thickness : 1f));

            DrawLayer(Graphic.MatSingle, new Vector3(drawLoc.x, baseY + 0.015f, drawLoc.z), rot, scale, slashAlpha);
            if (coreAlpha > 0f)
                DrawLayer(CoreMat, new Vector3(drawLoc.x, baseY + 0.02f, drawLoc.z), rot, scale, coreAlpha);
        }

        private static void DrawLayer(Material mat, Vector3 pos, Quaternion rot, Vector3 scale, float alpha)
        {
            Color c = mat.color;
            c.a *= alpha;
            block.SetColor("_Color", c);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, rot, scale), mat, 0, null, 0, block);
        }
    }

    /// <summary>
    /// Represents a visual effect that displays a temporary, soft-edged flash of color at a specified location,
    /// typically used to highlight an area or event in the game world.
    /// </summary>
    /// <remarks>The flash effect appears with a configurable radius and fades out smoothly after a brief hold
    /// period. The color and softness of the edge are fixed, but the radius can be set by the spawner. This class is
    /// intended for use in scenarios where a non-intrusive, attention-drawing visual cue is needed. Thread safety is
    /// not guaranteed; instances should be manipulated only from the main game thread.</remarks>
    public class Mote_ArayashikiRendFlash : MoteThrown
    {
        public float radius = 7f;                   // in tiles, set by the spawner
        private static readonly Color Tint = new Color(0.7f, 0.7f, 0.7f);   // grey level, 0 = black, 1 = white
        private const float MaxAlpha = 0.9f;   // higher = flatter grey, lower = more of the scene shows through
        private const float HoldSeconds = 1.0f;     // pure color
        private const float FadeSeconds = 2.0f;     // then back to normal
        private const float EdgeSoftness = 0.25f;   // 0 = hard edge, higher = softer rim

        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private static Material whiteMat;

        private static Material WhiteMat
        {
            get
            {
                if (whiteMat == null)
                {
                    const int size = 128;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    var center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = Vector2.Distance(new Vector2(x, y), center) / (size / 2f);
                            float a = Mathf.Clamp01((1f - d) / EdgeSoftness);
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                        }
                    }
                    tex.Apply();
                    whiteMat = new Material(ShaderDatabase.Transparent) { mainTexture = tex };
                }
                return whiteMat;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float t = AgeSecs;
            float alpha = t < HoldSeconds ? 1f : 1f - Mathf.Clamp01((t - HoldSeconds) / FadeSeconds);
            if (alpha <= 0f) return;

            Vector3 pos = new Vector3(drawLoc.x, def.altitudeLayer.AltitudeFor() + 0.005f, drawLoc.z);   // over the pawns, under the slash
            block.SetColor("_Color", new Color(Tint.r, Tint.g, Tint.b, alpha * MaxAlpha));
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(radius * 2f, 1f, radius * 2f));
            Graphics.DrawMesh(MeshPool.plane10, matrix, WhiteMat, 0, null, 0, block);
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
