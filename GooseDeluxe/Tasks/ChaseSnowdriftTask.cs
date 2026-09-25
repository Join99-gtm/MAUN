using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>Winter fun: the goose charges straight through a snowdrift (it bursts as the goose hits it).</summary>
    public sealed class ChaseSnowdriftTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_ChaseSnowdrift";

        private sealed class Data : GooseTaskData
        {
            public SnowDrift drift;
            public float started;
            public bool aimed;
        }

        public ChaseSnowdriftTask()
        {
            canBePickedRandomly = false; // started by the mod in winter only
            shortName = "Run through a snowdrift";
            description = "GooseDeluxe: in winter the goose charges through a snowdrift.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Charge);
            return new Data { drift = Deluxe.Winter != null ? Deluxe.Winter.PickDrift() : null, started = Time.time };
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null || d.drift == null || Time.time - d.started > 12f) { Deluxe.Done(g); return; }
            if (!d.aimed)
            {
                // aim well past the drift, so the goose runs through it instead of stopping in front
                Vector2 dir = Vector2.Normalize(d.drift.pos - g.position);
                if (dir.x == 0f && dir.y == 0f) dir = new Vector2(1f, 0f);
                Vector2 size = Deluxe.ScreenSize();
                Vector2 target = d.drift.pos + dir * (d.drift.rx * 3f);
                target.x = M.Clamp(target.x, 30f, size.x - 30f);
                target.y = M.Clamp(target.y, 60f, size.y - 20f);
                g.targetPos = target;
                d.aimed = true;
            }
            if (Vector2.Distance(g.position, g.targetPos) < 12f)
            {
                bool hit = d.drift.Kicked;
                Deluxe.Done(g);
                if (hit) Deluxe.HonkBy(g);
            }
        }
    }
}
