using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Bringing a note or picture from a friend, the way the goose brings memes: walk off the nearest side,
    /// wait a moment, then walk back in backwards, dragging the window by its edge. A visiting goose already
    /// starts off-screen and skips the first part.
    /// </summary>
    public sealed class DeliverTask : GooseTaskInfo
    {
        public const string Id = "GooseDeluxe_Deliver";
        internal static DeliveryPayload Pending;

        internal enum Stage { WalkingOff, Waiting, Dragging }

        internal sealed class Data : GooseTaskData
        {
            public DeliveryPayload payload;
            public Stage stage;
            public bool left;
            public float stageStart;
            public float wait;
            public INoteWindow window;
            public Vector2 offset;
        }

        public DeliverTask()
        {
            canBePickedRandomly = false;
            shortName = "Deliver a friend's note";
            description = "GooseDeluxe: drags in a note or picture sent by a friend.";
            taskID = Id;
        }

        public override GooseTaskData GetNewTaskData(GooseEntity g)
        {
            Vector2 size = Deluxe.ScreenSize();
            Data d = new Data { payload = Pending, stageStart = Time.time };
            Pending = null;
            d.left = g.position.x < size.x / 2f;
            if (Deluxe.IsOffscreen(g, 20f))
            {
                d.stage = Stage.Waiting; // a visitor arrives with the note already in its beak
                d.wait = 0.3f;
            }
            else
            {
                g.targetPos = new Vector2(d.left ? -60f : size.x + 60f, M.Lerp(g.position.y, size.y / 2f, 0.4f));
                Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Run);
                d.wait = M.Rand(1.2f, 2.2f);
            }
            return d;
        }

        public override void RunTask(GooseEntity g)
        {
            Data d = g.currentTaskData as Data;
            if (d == null || d.payload == null) { Deluxe.Done(g); return; }
            Vector2 size = Deluxe.ScreenSize();
            float now = Time.time;
            switch (d.stage)
            {
                case Stage.WalkingOff:
                    if (Vector2.Distance(g.position, g.targetPos) < 8f)
                    {
                        d.stage = Stage.Waiting;
                        d.stageStart = now;
                    }
                    else if (now - d.stageStart > 15f) Deluxe.Done(g);
                    break;

                case Stage.Waiting:
                    g.velocity = Vector2.zero;
                    if (now - d.stageStart < d.wait) break;
                    d.window = Deluxe.NoteWindowFactory != null ? Deluxe.NoteWindowFactory(d.payload) : null;
                    if (d.window == null) { Deluxe.Done(g); return; }
                    // the beak holds the edge of the window that faces the screen's inside
                    d.offset = d.left ? new Vector2(d.window.Width, d.window.Height / 2f) : new Vector2(0f, d.window.Height / 2f);
                    float margin = d.window.Width + M.Rand(25f, 35f);
                    float x = d.left ? margin : size.x - margin;
                    x = M.Clamp(x, 40f, size.x - 40f);
                    float y = M.Lerp(g.position.y, size.y / 2f, M.Rand(0.2f, 0.3f));
                    y = M.Clamp(y, d.window.Height / 2f + 50f, size.y - 30f);
                    g.targetPos = new Vector2(x, y);
                    Deluxe.SetSpeed(g, GooseEntity.SpeedTiers.Walk);
                    Follow(g, d);
                    d.window.ShowNote();
                    d.stage = Stage.Dragging;
                    d.stageStart = now;
                    break;

                case Stage.Dragging:
                    if (d.window.IsGone) { Deluxe.Done(g); return; }
                    g.extendingNeck = true;
                    // face the window while walking backwards, like the goose does with memes
                    g.targetDirection = g.targetPos - g.position;
                    Follow(g, d);
                    if (Vector2.Distance(g.position, g.targetPos) < 6f)
                    {
                        g.targetPos = g.position + Vector2.GetFromAngleDegrees(g.direction + 180f) * 40f;
                        Deluxe.Done(g);
                        Deluxe.HonkBy(g);
                    }
                    else if (now - d.stageStart > 25f) Deluxe.Done(g);
                    break;
            }
        }

        private static void Follow(GooseEntity g, Data d)
        {
            Vector2 beak = Deluxe.BeakOf(g);
            d.window.MoveTo((int)(beak.x - d.offset.x), (int)(beak.y - d.offset.y));
        }
    }
}
