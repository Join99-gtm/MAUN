using System;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose says a phrase. On its own it first walks up to the mouse cursor (the user) and stops beside
    /// it; from the menu it just stops where it is. It stands there while it talks, then goes back to
    /// wandering. The talking itself is the controller's <see cref="Speaker"/>: this task tells it when the
    /// goose has arrived (<see cref="Arrived"/>) and asks whether it's still talking (<see cref="StillTalking"/>).
    /// </summary>
    public sealed class SayTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Say";
        public const float ApproachSeconds = 4f, MaxSeconds = 25f;

        /// <summary>Walk up to the cursor first (the next time the task starts).</summary>
        public static bool Approach;
        public static Action Arrived = () => { };
        public static Func<bool> StillTalking = () => false;

        private sealed class Data : GooseTaskData
        {
            public bool approaching;
            public float start, talkSince = -1f;
        }

        public SayTask()
        {
            canBePickedRandomly = false;
            shortName = "Say a phrase";
            description = "GooseDeluxe: walks up to the cursor and says one of its phrases.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Data d = new Data { approaching = Approach, start = Time.time };
            Approach = false;
            Deluxe.SetSpeed(g, d.approaching ? GooseEntity.SpeedTiers.Run : GooseEntity.SpeedTiers.Walk);
            return d;
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) { Deluxe.Done(g); return; }
            float now = Time.time;
            if (d.approaching)
            {
                Vector2 spot = SpotBeside(g.position, new Vector2(Input.mouseX, Input.mouseY), Deluxe.ScreenSize());
                g.targetPos = spot;
                if (Vector2.Distance(g.position, spot) > 14f && now - d.start < ApproachSeconds) return;
                d.approaching = false;
                Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Walk);
            }
            if (d.talkSince < 0f)
            {
                d.talkSince = now;
                Arrived();
            }
            g.targetPos = g.position; // stand still and talk
            if ((!StillTalking() && now - d.talkSince > 0.5f) || now - d.start > MaxSeconds) Deluxe.Done(g);
        }

        /// <summary>A little to the side of the cursor (the side the goose comes from) and below it, so it
        /// doesn't stand on the pointer; with room above for the bubble.</summary>
        public static Vector2 SpotBeside(Vector2 goose, Vector2 cursor, Vector2 screen)
        {
            float side = goose.x <= cursor.x ? -1f : 1f;
            Vector2 p = new Vector2(cursor.x + side * 90f, cursor.y + 60f);
            p.x = M.Clamp(p.x, 40f, Math.Max(40f, screen.x - 40f));
            p.y = M.Clamp(p.y, 150f, Math.Max(150f, screen.y - 20f));
            return p;
        }
    }
}
