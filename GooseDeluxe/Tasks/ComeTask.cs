using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>"Сюда!": the goose runs to the mouse cursor and honks at it.</summary>
    public sealed class ComeTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Come";

        private sealed class Data : GooseTaskData
        {
            public float started;
        }

        public ComeTask()
        {
            canBePickedRandomly = false;
            shortName = "Come here";
            description = "GooseDeluxe: runs to the mouse cursor and honks.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Run);
            return new Data { started = Time.time };
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) { Deluxe.Done(g); return; }
            Vector2 cursor = new Vector2(Input.mouseX, Input.mouseY);
            Vector2 size = Deluxe.ScreenSize();
            // stand below and beside the cursor, so the head ends up next to it
            Vector2 target = cursor + new Vector2(g.position.x < cursor.x ? -38f : 38f, 34f);
            target.x = M.Clamp(target.x, 30f, size.x - 30f);
            target.y = M.Clamp(target.y, 60f, size.y - 10f);
            g.targetPos = target;
            if (Vector2.Distance(g.position, target) < 14f)
            {
                Deluxe.Done(g);
                Deluxe.HonkBy(g);
                return;
            }
            if (Time.time - d.started > 10f) Deluxe.Done(g);
        }
    }
}
