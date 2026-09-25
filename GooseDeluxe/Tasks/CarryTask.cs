using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Sending to a friend: the goose grabs the note (or photo) in its beak, runs off the nearest side of
    /// the screen, waits there until the upload has finished and comes back empty-beaked (or, if the
    /// message didn't go through, still holding it).
    /// </summary>
    public sealed class CarryTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Carry";
        internal static CarryJob Pending;

        internal enum Stage { ToEdge, Offscreen, Return }

        internal sealed class Data : GooseTaskData
        {
            public CarryJob job;
            public Stage stage;
            public bool left;
            public float y;
            public float stageStart;
        }

        public CarryTask()
        {
            canBePickedRandomly = false;
            shortName = "Carry a note to a friend";
            description = "GooseDeluxe: runs off-screen with a note for a friend's goose.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Vector2 size = Deluxe.ScreenSize();
            Data d = new Data { job = Pending ?? new CarryJob { State = CarryJob.Sent }, stageStart = Time.time };
            Pending = null;
            d.left = g.position.x < size.x / 2f;
            d.y = M.Clamp(g.position.y, 80f, size.y - 40f);
            g.targetPos = new Vector2(d.left ? -80f : size.x + 80f, d.y);
            Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Run);
            Deluxe.Carrying = d.job.Kind;
            return d;
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null) { Deluxe.Carrying = CarryKind.None; Deluxe.Done(g); return; }
            Vector2 size = Deluxe.ScreenSize();
            float now = Time.time;
            switch (d.stage)
            {
                case Stage.ToEdge:
                    if (Vector2.Distance(g.position, g.targetPos) < 12f || Deluxe.IsOffscreen(g, 40f))
                    {
                        d.stage = Stage.Offscreen;
                        d.stageStart = now;
                        Deluxe.Carrying = CarryKind.None; // handed over behind the edge of the screen
                    }
                    else if (now - d.stageStart > 15f) Finish(g, d);
                    break;

                case Stage.Offscreen:
                    g.velocity = Vector2.zero;
                    bool settled = d.job.State != CarryJob.Sending || now - d.stageStart > 15f;
                    if (now - d.stageStart > 1.2f && settled)
                    {
                        bool ok = d.job.State == CarryJob.Sent;
                        Deluxe.Carrying = ok ? CarryKind.None : d.job.Kind;
                        if (!ok) Deluxe.Notify("Гусь вернулся с запиской", "Не получилось отправить " + d.job.To + ": нет связи с сервером.");
                        g.targetPos = new Vector2(d.left ? 110f : size.x - 110f, d.y);
                        Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Walk);
                        d.stage = Stage.Return;
                        d.stageStart = now;
                    }
                    break;

                case Stage.Return:
                    if (Vector2.Distance(g.position, g.targetPos) < 12f || now - d.stageStart > 15f)
                    {
                        bool ok = d.job.State == CarryJob.Sent;
                        Finish(g, d);
                        if (ok) Deluxe.HonkBy(g);
                    }
                    break;
            }
        }

        private static void Finish(GooseEntity g, Data d)
        {
            Deluxe.Carrying = CarryKind.None;
            Deluxe.Done(g);
        }
    }
}
