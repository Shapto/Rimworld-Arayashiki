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
    public class Arayashiki_Erasure
    {
        private static readonly HashSet<PawnRelationDef> CloseRelations = new HashSet<PawnRelationDef>
        {
            DefDatabase<PawnRelationDef>.GetNamed("Spouse"),
            DefDatabase<PawnRelationDef>.GetNamed("Lover"),
            DefDatabase<PawnRelationDef>.GetNamed("Fiance"),
            DefDatabase<PawnRelationDef>.GetNamed("Parent"),
            DefDatabase<PawnRelationDef>.GetNamed("Child"),
            DefDatabase<PawnRelationDef>.GetNamed("Sibling"),
        };

        /// <summary>
        /// Calculates the absolute value of the opinion or mood offset associated with the specified memory.
        /// </summary>
        /// <param name="t">The memory instance for which to retrieve the offset value. If the memory is social, its opinion offset is
        /// used; otherwise, the mood offset is used. Cannot be null.</param>
        /// <returns>The absolute value of the opinion or mood offset for the specified memory.</returns>
        private static float MemoryValue(Thought_Memory t)
        {
            return t is Thought_MemorySocial s ? Math.Abs(s.OpinionOffset()) : Math.Abs(t.moodOffset);
        }

        /// <summary>
        /// Removes memories from the specified pawn in the order provided, deducting their cost from the remaining
        /// total until the cost is depleted or all memories are erased.
        /// </summary>
        /// <remarks>Only memories up to the available remaining cost are erased. The method updates the
        /// remaining cost to reflect the total cost of erased memories.</remarks>
        /// <param name="wielder">The pawn whose memories are to be erased. Cannot be null.</param>
        /// <param name="ordered">The list of memories to erase, in the order they should be removed. Cannot be null or empty.</param>
        /// <param name="remainingCost">The total cost budget available for erasing memories. This value is reduced by the cost of each memory
        /// erased and updated to reflect the remaining budget.</param>
        private static void EraseMemories(Pawn wielder, List<Thought_Memory> ordered, ref float remainingCost)
        {
            if (ordered.Count == 0) return;
            var tracker = wielder.needs.mood.thoughts.memories;

            foreach (var memory in ordered)
            {
                if (remainingCost <= 0) break;
                remainingCost -= MemoryValue(memory);
                MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{memory.def.LabelCap} - {memory.otherPawn?.LabelShort} erased", Color.yellow);
                tracker.RemoveMemory(memory);
            }
        }

        /// <summary>
        /// Pays a specified cost by progressively erasing the pawn's memories, social opinions, mood thoughts, skills,
        /// traits, and relationships until the cost is fulfilled or no further elements can be removed.
        /// </summary>
        /// <remarks>The method removes non-permanent memories and social thoughts first, prioritizing
        /// those with the highest impact. If the cost remains, it reduces non-melee skills, then removes traits, close
        /// relationships, other relationships, and finally melee skill levels. The process stops when the cost is fully
        /// paid or no further elements are available to erase. This operation is irreversible and may significantly
        /// alter the pawn's personality and social connections.</remarks>
        /// <param name="wielder">The pawn whose memories, skills, traits, and relationships are used to pay the cost. Cannot be null.</param>
        /// <param name="cost">The total cost to pay, in arbitrary units. Must be a non-negative value.</param>
        public static void PayCost(Pawn wielder, float cost)
        {

            var memories = wielder.needs.mood.thoughts.memories.Memories
                 .Where(t => !t.permanent)
                 .OrderByDescending(t => Math.Abs(t.moodOffset))
                 .ToList();

            ///Costs are intentionally allowed to overshoot. The weapon pays for the entire effect.
            float remainingCost = cost;

            var closePawns = new HashSet<Pawn>();
            if (wielder.relations != null)
            {
                foreach (var r in wielder.relations.DirectRelations)
                {
                    if (CloseRelations.Contains(r.def)) closePawns.Add(r.otherPawn);
                }
            }

            //memories
            var precious = memories
                 .Where(t => t.otherPawn != null && closePawns.Contains(t.otherPawn))
                 .OrderByDescending(t => MemoryValue(t))
                 .ToList();

            //opinions
            var opinions = memories
                 .Where(t => t is Thought_MemorySocial && !precious.Contains(t))
                 .OrderByDescending(t => MemoryValue(t))
                 .ToList();
            
            //moods
            var mood = memories
                 .Where(t => !(t is Thought_MemorySocial) && !precious.Contains(t))
                 .OrderByDescending(t => MemoryValue(t))
                 .ToList();

            EraseMemories(wielder, precious, ref remainingCost);
            EraseMemories(wielder, opinions, ref remainingCost);
            EraseMemories(wielder, mood, ref remainingCost);

            //skills that arent melee from highest to lowest
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
            
            //traits
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

            //close relationships
            if (remainingCost > 0 && wielder.relations != null)
            {
                var closeRelations = wielder.relations.DirectRelations
                    .Where(r => CloseRelations.Contains(r.def))
                    .OrderBy(r => Rand.Value)
                    .ToList();

                foreach (var relation in closeRelations)
                {
                    if (remainingCost <= 0) break;
                    remainingCost -= 200;
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{relation.otherPawn?.LabelShort} forgotten", Color.red);
                    wielder.relations.RemoveDirectRelation(relation.def, relation.otherPawn);
                }
            }

            //other relationships
            if (remainingCost > 0 && wielder.relations != null)
            {
                var otherRelations = wielder.relations.DirectRelations
                    .Where(r => !CloseRelations.Contains(r.def))
                    .OrderBy(r => Rand.Value)
                    .ToList();

                foreach (var relation in otherRelations)
                {
                    if (remainingCost <= 0) break;
                    remainingCost -= 50;
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{relation.otherPawn?.LabelShort} forgotten", Color.red);
                    wielder.relations.RemoveDirectRelation(relation.def, relation.otherPawn);
                }
            }

            //melee skill
            if (remainingCost > 0)
            {
                var skill = wielder.skills.GetSkill(SkillDefOf.Melee);
                while (skill.Level > 0 && remainingCost > 0)
                {
                    MoteMaker.ThrowText(wielder.DrawPos, wielder.Map, $"{skill} -1", Color.white);
                    skill.Level--;
                    remainingCost -= 10;
                }
            }
        }
    }
}
