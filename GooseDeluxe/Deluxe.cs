using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    internal enum CarryKind { None, Note, Photo }

    /// <summary>
    /// State shared between the entry point, the goose tasks and the UI. Everything here is used on the
    /// goose's UI thread, except <see cref="UiQueue"/>, which background threads use to hand work back.
    /// Nothing in this file touches WinForms, so the headless tests compile it as is.
    /// </summary>
    internal static class Deluxe
    {
        public static DeluxeConfig Cfg = new DeluxeConfig();
        public static string ModDir;
        public static string GooseDir;
        public static GooseEntity Goose;
        public static ParticleSystem Particles;
        public static GooseAnimator Animator;
        public static FriendService Friends;
        public static WinterScene Winter;

        /// <summary>What the goose holds in its beak while running off to a friend.</summary>
        public static CarryKind Carrying;
        /// <summary>Manual pause: the goose's engine is frozen and it is drawn asleep.</summary>
        public static bool Sleeping;
        /// <summary>A fullscreen app is in front: the engine is frozen and nothing is drawn.</summary>
        public static bool HiddenForFullscreen;

        /// <summary>Size of the goose's playground (its main window), in screen pixels.</summary>
        public static Func<Vector2> ScreenSize = () => new Vector2(1280f, 720f);
        public static Func<DeliveryPayload, INoteWindow> NoteWindowFactory;
        /// <summary>Tray balloon (title, text); a no-op until the tray exists.</summary>
        public static Action<string, string> Notify = (t, m) => { };

        /// <summary>Work for the UI thread, drained by the mod's timer (never inside the goose's paint).</summary>
        public static readonly ConcurrentQueue<Action> UiQueue = new ConcurrentQueue<Action>();

        private static string[] taskIds;
        private static readonly object logLock = new object();

        public static void Log(string message)
        {
            try
            {
                string dir = ModDir ?? Path.GetDirectoryName(typeof(Deluxe).Assembly.Location);
                string path = Path.Combine(dir, "GooseDeluxe.log");
                lock (logLock)
                {
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void RunUiQueue()
        {
            Action a;
            int n = 0;
            while (n++ < 64 && UiQueue.TryDequeue(out a))
            {
                try { a(); }
                catch (Exception ex) { Log("UI action failed: " + ex); }
            }
        }

        public static int TaskIndex(string id)
        {
            if (taskIds == null)
            {
                try
                {
                    if (API.TaskDatabase == null || API.TaskDatabase.getAllLoadedTaskIDs == null) return -1;
                    taskIds = API.TaskDatabase.getAllLoadedTaskIDs();
                }
                catch { return -1; }
            }
            return Array.IndexOf(taskIds, id);
        }

        /// <summary>For tests: forget the cached task list.</summary>
        public static void ResetTaskCache() { taskIds = null; }

        // ---- per-goose helpers (the player's goose and visiting geese share the same tasks) ----

        /// <summary>True for geese that came to visit from a friend's computer.</summary>
        public static Func<GooseEntity, bool> IsGuest = g => false;
        /// <summary>The animator that draws a given goose (each goose has its own).</summary>
        public static Func<GooseEntity, GooseAnimator> AnimatorFor = g => Animator;

        public static bool IsCurrentTask(GooseEntity g, string id)
        {
            int index = TaskIndex(id);
            return g != null && index >= 0 && g.currentTask == index;
        }

        public static bool IsCurrentTask(string id) { return IsCurrentTask(Goose, id); }

        /// <summary>Switches a goose to a task by ID. Never passes an unknown ID to the goose (that pops a MessageBox).</summary>
        public static bool SetTaskOn(GooseEntity g, string id, bool honk)
        {
            if (g == null || TaskIndex(id) < 0 || API.Goose == null || API.Goose.setCurrentTaskByID == null) return false;
            API.Goose.setCurrentTaskByID(g, id, honk);
            return true;
        }

        public static bool SetTask(string id, bool honk) { return SetTaskOn(Goose, id, honk); }

        /// <summary>A task is over: the player's goose goes back to wandering, a visitor heads home.</summary>
        public static void Done(GooseEntity g)
        {
            if (g == null) return;
            if (IsGuest(g)) { SetTaskOn(g, LeaveTask.Id, false); return; }
            if (API.Goose != null && API.Goose.setTaskRoaming != null) API.Goose.setTaskRoaming(g);
        }

        public static void ToWander() { Done(Goose); }

        public static void SetSpeed(GooseEntity g, GooseEntity.SpeedTiers tier)
        {
            if (API.Goose != null && API.Goose.setSpeed != null) API.Goose.setSpeed(g, tier);
        }

        /// <summary>Honk sound (respects the goose's mute setting) plus the beak/feathers/text animation.</summary>
        public static void HonkBy(GooseEntity g)
        {
            if (g == null) return;
            try
            {
                if (API.Goose != null && API.Goose.playHonckSound != null) API.Goose.playHonckSound();
            }
            catch (Exception ex) { Log("Honk sound failed: " + ex.Message); }
            GooseAnimator a = AnimatorFor(g);
            if (a != null) a.Honk(g, Time.time);
        }

        public static void Honk() { HonkBy(Goose); }

        /// <summary>Where a goose's beak is on screen: the last drawn pose if we draw it, else the goose's own rig.</summary>
        public static Vector2 BeakOf(GooseEntity g)
        {
            if (g == null) return Vector2.zero;
            GooseAnimator a = AnimatorFor(g);
            if (a != null && a.HasPose) return a.Pose.head2EndPoint;
            return g.rig.head2EndPoint;
        }

        public static bool IsOffscreen(GooseEntity g, float margin)
        {
            Vector2 size = ScreenSize();
            return g.position.x < -margin || g.position.x > size.x + margin;
        }
    }
}
