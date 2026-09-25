using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GooseDeluxe;
using GooseShared;
using SamEngine;

namespace Tests
{
    /// <summary>A note window that only records what the goose does with it.</summary>
    internal sealed class FakeWindow : INoteWindow
    {
        public readonly DeliveryPayload Payload;
        public readonly List<Point> Moves = new List<Point>();
        public bool Shown;
        public bool Gone;
        public FakeWindow(DeliveryPayload p) { Payload = p; }
        public int Width { get { return 300; } }
        public int Height { get { return 180; } }
        public bool IsGone { get { return Gone; } }
        public void MoveTo(int x, int y) { Moves.Add(new Point(x, y)); }
        public void ShowNote() { Shown = true; }
    }

    /// <summary>
    /// Stand-in for the goose's engine: the real tasks from the mod plus a simple movement model
    /// (straight line toward targetPos at the tier speed, 120 steps per second) and fake API pointers.
    /// </summary>
    internal sealed class FakeWorld
    {
        public readonly List<GooseTaskInfo> Tasks = new List<GooseTaskInfo>();
        public readonly List<FakeWindow> Windows = new List<FakeWindow>();
        public readonly List<string> Notifications = new List<string>();
        public readonly List<string> TaskLog = new List<string>();
        public int Honks;
        public float Now;
        public static readonly Vector2 Screen = new Vector2(1280f, 720f);

        private sealed class NamedTask : GooseTaskInfo
        {
            private readonly Action<GooseEntity> run;
            public NamedTask(string id, Action<GooseEntity> run) { taskID = id; canBePickedRandomly = false; this.run = run; }
            public override GooseTaskData GetNewTaskData(GooseEntity s) { return null; }
            public override void RunTask(GooseEntity s) { if (run != null) run(s); }
        }

        public FakeWorld()
        {
            Tasks.Add(new NamedTask("Wander", null));
            // the goose's own tasks: pretend they are done at once and hand the goose back to Wander
            foreach (string id in new[] { "CollectMeme", "CollectNotepad", "NabMouse", "TrackMud" })
                Tasks.Add(new NamedTask(id, g => SetTask(g, "Wander")));
            Tasks.Add(new ComeTask());
            Tasks.Add(new CarryTask());
            Tasks.Add(new DeliverTask());
            Tasks.Add(new VisitTask());
            Tasks.Add(new LeaveTask());

            API.Goose = new API.GooseFunctionPointers
            {
                setSpeed = (g, tier) => g.currentSpeed = tier == GooseEntity.SpeedTiers.Walk ? 80f : tier == GooseEntity.SpeedTiers.Run ? 200f : 400f,
                setCurrentTaskByID = (g, id, honk) => { if (honk) Honks++; SetTask(g, id); },
                setTaskRoaming = g => SetTask(g, "Wander"),
                playHonckSound = () => Honks++,
            };
            API.TaskDatabase = new API.TaskDatabaseQueryFunctions
            {
                getAllLoadedTaskIDs = () => Tasks.Select(t => t.taskID).ToArray(),
                getTaskIndexByID = id => Tasks.FindIndex(t => t.taskID == id),
            };
            Deluxe.ResetTaskCache();
            Deluxe.Cfg = new DeluxeConfig();
            Deluxe.ScreenSize = () => Screen;
            Deluxe.Notify = (t, m) => Notifications.Add(t + ": " + m);
            Deluxe.NoteWindowFactory = p => { FakeWindow w = new FakeWindow(p); Windows.Add(w); return w; };
            Deluxe.Particles = new ParticleSystem();
            Deluxe.Animator = null;
            Deluxe.IsGuest = g => false;
            Deluxe.AnimatorFor = g => null;
            Deluxe.Carrying = CarryKind.None;
            Deluxe.Sleeping = Deluxe.HiddenForFullscreen = false;
            CarryTask.Pending = null;
            DeliverTask.Pending = null;
            Time.time = Now = 0f;
        }

        public void SetTask(GooseEntity g, string id)
        {
            int i = Tasks.FindIndex(t => t.taskID == id);
            if (i < 0) throw new InvalidOperationException("unknown task " + id);
            TaskLog.Add(id);
            g.currentTask = i;
            g.currentTaskData = Tasks[i].GetNewTaskData(g);
        }

        public string TaskOf(GooseEntity g) { return g.currentTask >= 0 ? Tasks[g.currentTask].taskID : "?"; }

        public GooseEntity NewGoose(Vector2 at)
        {
            GooseEntity g = new GooseEntity(Tick, UpdateRig, (e, gfx) => { });
            g.parameters = new GooseEntity.ParametersTable();
            g.position = g.targetPos = at;
            g.rig.feets = new ProceduralFeets();
            g.rig.feets.lFootPos = g.rig.feets.rFootPos = at;
            g.renderData = new GooseRenderData
            {
                brushGooseWhite = new SolidBrush(Color.White),
                brushGooseOrange = new SolidBrush(Color.Orange),
                brushGooseOutline = new SolidBrush(Color.LightGray),
            };
            API.Goose.setSpeed(g, GooseEntity.SpeedTiers.Walk);
            SetTask(g, "Wander");
            UpdateRig(g.rig, g.position, g.direction);
            return g;
        }

        /// <summary>Stand-in for GooseFunctions.TickGoose: run the task, then move toward the target.</summary>
        public void Tick(GooseEntity g)
        {
            g.extendingNeck = false;
            Tasks[g.currentTask].RunTask(g);
            Vector2 to = g.targetPos - g.position;
            float dist = Vector2.Magnitude(to);
            float step = g.currentSpeed / 120f;
            if (dist <= step)
            {
                g.position = g.targetPos;
                g.velocity = Vector2.zero;
            }
            else
            {
                Vector2 dir = Vector2.Normalize(to);
                g.velocity = dir * g.currentSpeed;
                g.position += dir * step;
                g.direction = M.AngleDeg(dir);
            }
        }

        public void UpdateRig(Rig rig, Vector2 pos, float direction)
        {
            rig.head2EndPoint = pos + Vector2.GetFromAngleDegrees(direction) * 25f + new Vector2(0f, -35f);
        }

        /// <summary>Runs the player's goose for up to <paramref name="seconds"/>, 120 ticks per second.</summary>
        public void Run(GooseEntity g, float seconds, Func<bool> until = null)
        {
            int steps = (int)(seconds * 120f);
            for (int i = 0; i < steps; i++)
            {
                Now += 1f / 120f;
                Time.time = Now;
                Tick(g);
                UpdateRig(g.rig, g.position, g.direction);
                if (until != null && until()) return;
            }
        }
    }
}
