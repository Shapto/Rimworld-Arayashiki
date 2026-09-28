using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Arayashiki
{
    public class CompProperties_DashAbility : CompProperties_AbilityEffect
    {
        public float pointCost;
        public Type runClass;

        public CompProperties_DashAbility()
        {
            compClass = typeof(CompAbilityEffect_DashAbility);
        }
    }

    public class CompAbilityEffect_DashAbility : CompAbilityEffect
    {
        new CompProperties_DashAbility Props => (CompProperties_DashAbility)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn wielder = parent.pawn;
            Pawn victim = target.Pawn;
            if (wielder == null || victim == null || wielder.Map == null || Props.runClass == null) return;
            if (!SlayTheHeavensSequence.HasRoom(victim)) return;

            Verb_ArayashikiSlash verb = wielder.equipment?.PrimaryEq?.AllVerbs
                .OfType<Verb_ArayashikiSlash>().FirstOrDefault();
            if (verb == null) return;

            var run = (DashAttackRun)Activator.CreateInstance(Props.runClass, wielder, victim, verb, Props.pointCost);
            run.Begin();
        }
    }
}
