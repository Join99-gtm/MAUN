using System;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Out of nowhere the goose chases the mouse cursor: a few seconds at full speed with its neck stretched,
    /// a honk whenever it catches up, then it loses interest. The mod starts it now and then
    /// (<see cref="RandomChase"/>), or from the menu. Move the mouse in circles and the goose drifts.
    /// </summary>
    public sealed class ChaseCursorTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_ChaseCursor";

        private sealed class Data : GooseTaskData
        {
            public float until;
            public float lastHonk = -10f;
        }

        public ChaseCursorTask()
        {
            canBePickedRandomly = false;
            shortName = "Chase the cursor";
            description = "GooseDeluxe: suddenly chases the mouse cursor for a few seconds.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Charge);
            return new Data { until = Time.time + M.Rand(5f, 8f) };
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) { Deluxe.Done(g); return; }
            Vector2 cursor = new Vector2(Input.mouseX, Input.mouseY);
            Vector2 size = Deluxe.ScreenSize();
            cursor.x = M.Clamp(cursor.x, 20f, size.x - 20f);
            cursor.y = M.Clamp(cursor.y, 40f, size.y - 10f);
            g.targetPos = cursor;
            g.extendingNeck = true;
            float now = Time.time;
            if (Vector2.Distance(g.position, cursor) < 30f && now - d.lastHonk > 1.2f)
            {
                d.lastHonk = now;
                Deluxe.HonkBy(g);
            }
            if (now > d.until) Deluxe.Done(g);
        }
    }

    /// <summary>When the goose next chases the cursor on its own: on average every few minutes, at random.</summary>
    internal sealed class RandomChase
    {
        private readonly Random rng;

        public double NextAt = -1;

        public RandomChase(Random rng = null) { this.rng = rng ?? new Random(); }

        /// <summary>Somewhere between half and one and a half times the average from now.</summary>
        public void Plan(double now, float averageMinutes)
        {
            NextAt = now + Math.Max(0.5, averageMinutes) * 60.0 * (0.5 + rng.NextDouble());
        }

        /// <summary>True once the time has come (and the next time is planned right away).</summary>
        public bool Due(double now, float averageMinutes)
        {
            if (NextAt < 0) { Plan(now, averageMinutes); return false; }
            if (now < NextAt) return false;
            Plan(now, averageMinutes);
            return true;
        }
    }
}
