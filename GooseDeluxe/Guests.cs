using System;
using System.Collections.Generic;
using System.Drawing;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>A friend's goose visiting this screen.</summary>
    internal sealed class Guest
    {
        public GooseEntity Entity;
        public GooseAnimator Animator;
        public GuestLook Look;
        public float Arrived;
        /// <summary>Set once it has left the screen; kept a little longer so its muddy footprints fade out.</summary>
        public float GoneAt = -1f;
    }

    /// <summary>
    /// Spawns and runs visiting geese. A visitor is a second GooseEntity driven by the goose's own
    /// TickGoose/UpdateRig functions and tasks (they all take the goose as a parameter), so it walks,
    /// steps and drags windows exactly like the real one. It never wanders: whenever a task hands it
    /// back to "Wander" it is sent home instead. One visitor at a time.
    /// </summary>
    internal sealed class Guests
    {
        public const float MaxVisitSeconds = 90f;
        private const float FootprintFadeSeconds = FootMark.Lifetime + FootMark.ShrinkTime + 0.5f;

        private readonly List<Guest> list = new List<Guest>();
        private readonly Func<GooseEntity.TickFunction> tick;
        private readonly Func<GooseEntity.UpdateRigFunction> updateRig;
        private readonly Func<GooseAnimator> newAnimator;

        public IList<Guest> All { get { return list; } }

        public Guests(Func<GooseEntity.TickFunction> tick, Func<GooseEntity.UpdateRigFunction> updateRig, Func<GooseAnimator> newAnimator)
        {
            this.tick = tick;
            this.updateRig = updateRig;
            this.newAnimator = newAnimator;
        }

        /// <summary>Someone is on screen (or its footprints still are); new visits wait.</summary>
        public bool Busy
        {
            get
            {
                foreach (Guest g in list) if (g.GoneAt < 0f) return true;
                return false;
            }
        }

        public bool IsGuest(GooseEntity e)
        {
            foreach (Guest g in list) if (ReferenceEquals(g.Entity, e)) return true;
            return false;
        }

        public GooseAnimator AnimatorOf(GooseEntity e)
        {
            foreach (Guest g in list) if (ReferenceEquals(g.Entity, e)) return g.Animator;
            return null;
        }

        /// <summary>A visitor appears just off a random side of the screen and starts <paramref name="taskId"/>.</summary>
        public Guest Spawn(GuestLook look, string taskId, DeliveryPayload payload)
        {
            GooseEntity.TickFunction t = tick();
            GooseEntity.UpdateRigFunction u = updateRig();
            if (t == null || u == null) return null;

            Vector2 size = Deluxe.ScreenSize();
            bool fromLeft = M.RandInt(2) == 0;
            GooseEntity e = new GooseEntity(t, u, (g, gfx) => { });
            e.parameters = new GooseEntity.ParametersTable();
            e.position = new Vector2(fromLeft ? -60f : size.x + 60f, M.Rand(size.y * 0.3f, size.y * 0.75f));
            e.targetPos = e.position;
            e.direction = fromLeft ? 0f : 180f;
            e.rig.feets = new ProceduralFeets();
            Vector2 perp = Vector2.GetFromAngleDegrees(e.direction + 90f);
            e.rig.feets.lFootPos = e.position;
            e.rig.feets.rFootPos = e.position + perp * e.rig.feets.feetDistanceApart;
            e.renderData = new GooseRenderData
            {
                brushGooseWhite = new SolidBrush(FriendProtocol.ToColor(look.White, Color.White)),
                brushGooseOrange = new SolidBrush(FriendProtocol.ToColor(look.Orange, Color.Orange)),
                brushGooseOutline = new SolidBrush(FriendProtocol.ToColor(look.Outline, Color.LightGray)),
            };
            u(e.rig, e.position, e.direction);

            Guest guest = new Guest { Entity = e, Animator = newAnimator(), Look = look, Arrived = Time.time };
            list.Add(guest);
            Deluxe.SetSpeed(e, GooseEntity.SpeedTiers.Walk);
            if (payload != null) DeliverTask.Pending = payload;
            if (!Deluxe.SetTaskOn(e, taskId, false))
            {
                list.Remove(guest);
                return null;
            }
            return guest;
        }

        /// <summary>Runs every visitor for the same number of sub-steps as the player's goose this frame.</summary>
        public void Update(int steps)
        {
            GooseEntity.TickFunction t = tick();
            GooseEntity.UpdateRigFunction u = updateRig();
            float now = Time.time;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Guest g = list[i];
                GooseEntity e = g.Entity;
                if (g.GoneAt >= 0f)
                {
                    if (now - g.GoneAt > FootprintFadeSeconds) list.RemoveAt(i);
                    continue;
                }
                if (t != null && steps > 0)
                {
                    // clicking a visitor must not make it grab the mouse mid-delivery
                    ButtonState saved = Input.leftMouseButton;
                    Input.leftMouseButton.Clicked = false;
                    try { FixedTimestep.RunSteps(t, e, steps); }
                    finally { Input.leftMouseButton = saved; }
                }
                if (u != null) u(e.rig, e.position, e.direction);

                if (Deluxe.IsCurrentTask(e, "Wander")) Deluxe.SetTaskOn(e, LeaveTask.Id, false);
                if (now - g.Arrived > MaxVisitSeconds && !Deluxe.IsCurrentTask(e, LeaveTask.Id)) Deluxe.SetTaskOn(e, LeaveTask.Id, false);
                if (Deluxe.IsCurrentTask(e, LeaveTask.Id) && LeaveTask.IsGone(e)) g.GoneAt = now;
            }
        }

        public void Clear() { list.Clear(); }
    }
}
