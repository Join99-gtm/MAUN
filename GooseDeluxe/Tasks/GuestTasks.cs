using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>A friend's goose drops by: walks in, honks twice and leaves.</summary>
    public sealed class VisitTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Visit";

        private sealed class Data : GooseTaskData
        {
            public int honks;
            public float stageStart;
            public bool arrived;
        }

        public VisitTask()
        {
            canBePickedRandomly = false;
            shortName = "Visit";
            description = "GooseDeluxe: a friend's goose walks in, honks and leaves.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Vector2 size = Deluxe.ScreenSize();
            bool fromLeft = g.position.x < size.x / 2f;
            float inset = M.Rand(160f, 280f);
            g.targetPos = new Vector2(fromLeft ? inset : size.x - inset, M.Clamp(g.position.y, 80f, size.y - 40f));
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Walk);
            return new Data { stageStart = Time.time };
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) { Deluxe.Done(g); return; }
            float now = Time.time;
            if (!d.arrived)
            {
                if (Vector2.Distance(g.position, g.targetPos) < 10f)
                {
                    d.arrived = true;
                    d.stageStart = now;
                    d.honks = 1;
                    Deluxe.HonkBy(g);
                }
                else if (now - d.stageStart > 15f) Deluxe.Done(g);
                return;
            }
            g.velocity = Vector2.zero;
            if (d.honks == 1 && now - d.stageStart > 0.7f) { d.honks = 2; Deluxe.HonkBy(g); }
            if (now - d.stageStart > 1.8f) Deluxe.Done(g);
        }
    }

    /// <summary>A visiting goose heads back to the nearest side of the screen and disappears.</summary>
    public sealed class LeaveTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Leave";

        internal sealed class Data : GooseTaskData
        {
            public bool gone;
            public float started;
        }

        public LeaveTask()
        {
            canBePickedRandomly = false;
            shortName = "Leave";
            description = "GooseDeluxe: a visiting goose goes home.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Vector2 size = Deluxe.ScreenSize();
            bool left = g.position.x < size.x / 2f;
            g.targetPos = new Vector2(left ? -90f : size.x + 90f, g.position.y);
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Run);
            return new Data { started = Time.time };
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) return;
            if (Vector2.Distance(g.position, g.targetPos) < 12f || Deluxe.IsOffscreen(g, 70f) || Time.time - d.started > 20f)
            {
                d.gone = true;
                g.velocity = Vector2.zero;
            }
        }

        internal static bool IsGone(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            return d != null && d.gone;
        }
    }
}
