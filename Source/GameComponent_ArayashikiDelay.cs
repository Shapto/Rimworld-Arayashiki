using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace Arayashiki
{
    /// <summary>
    /// Provides a game component that allows scheduling actions to be executed after a specified number of game ticks.
    /// </summary>
    /// <remarks>This component is intended for use within the game's tick-based update system. Scheduled
    /// actions are executed on the main thread during the component's tick, after the specified delay has elapsed.
    /// Actions are executed in the order they become due. Exceptions thrown by scheduled actions are logged and do not
    /// prevent other actions from executing.</remarks>
    public class GameComponent_ArayashikiDelay : GameComponent
    {
        /// <summary>
        /// Represents a scheduled action and its associated execution tick within the timer system.
        /// </summary>
        private class Entry
        {
            public int dueTick;
            public Action action;
        }

        private readonly List<Entry> pending = new List<Entry>();

        /// <summary>
        /// Initializes a new instance of the GameComponent_ArayashikiDelay class using the specified game instance.
        /// </summary>
        /// <param name="game">The Game instance that this component will be associated with. Cannot be null.</param>
        public GameComponent_ArayashikiDelay(Game game) { }

        /// <summary>
        /// Schedules the specified action to be executed after a given number of game ticks.
        /// </summary>
        /// <remarks>The action will be invoked once after the delay has elapsed, based on the game's tick
        /// counter. If multiple actions are scheduled for the same tick, their execution order is not
        /// guaranteed.</remarks>
        /// <param name="delayTicks">The number of game ticks to wait before executing the action. Must be non-negative.</param>
        /// <param name="action">The action to execute after the specified delay. Cannot be null.</param>
        public void Schedule(int delayTicks, Action action)
        {
            pending.Add(new Entry { dueTick = Find.TickManager.TicksGame + delayTicks, action = action });
        }

        /// <summary>
        /// Processes and executes all pending actions that are due on the current game tick.
        /// </summary>
        /// <remarks>This method is called once per game tick by the game engine. It executes any actions
        /// that have been scheduled for the current tick or earlier. If no actions are pending or due, the method
        /// returns immediately. Exceptions thrown by individual actions are caught and logged; execution of other due
        /// actions continues.</remarks>
        public override void GameComponentTick()
        {
            if (pending.Count == 0) return;

            int now = Find.TickManager.TicksGame;
            List<Entry> due = pending.FindAll(e => e.dueTick <= now);
            if (due.Count == 0) return;
            pending.RemoveAll(e => e.dueTick <= now);

            foreach (Entry e in due)
            {
                try { e.action(); }
                catch (Exception ex) { Log.Error("Arayashiki delayed action failed: " + ex); }
            }
        }
    }
}
