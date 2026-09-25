using System;
using System.Drawing;
#if !HEADLESS
using System.Windows.Forms;
#endif
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>Everything the renderer needs for one frame: rig points already transformed to screen space.</summary>
    internal sealed class GoosePose
    {
        public Vector2 pos;
        public float scale = 1f;
        public Vector2 fwd, perp, fwdHead, perpHead;
        public Vector2 underbodyCenter, bodyCenter, neckBase, neckHeadPoint, head1EndPoint, head2EndPoint;
        public Vector2 lFoot, rFoot;
        public float blink;      // 0 open .. 1 closed
        public float beakOpen;   // 0 .. 1
        public float wingOpen;   // 0 .. 1
        public float flap;       // radians
        public float puff;       // extra body thickness, 0 .. ~0.2
        public float squash;     // >0 stretched along fwd, <0 squashed
        public Vector2 eyeLook;  // pupil offset in px
        public bool mouseHeld;
        public float speed01;    // 0 .. 1 of charge speed
        public float headShake;  // px, sideways head jitter (idle "shake" animation)
        public CarryKind carry;  // note/photo in the beak (set by the caller)
        public bool asleep;
    }

    /// <summary>
    /// Derives the animated pose from the goose's state. The goose's own tick still owns movement,
    /// AI and feet; this layer only adds secondary motion on top and never writes back to the goose.
    /// </summary>
    internal sealed class GooseAnimator
    {
        private enum IdleAction { None, Stretch, Shake, Preen }

        private readonly DeluxeConfig cfg;
        private readonly ParticleSystem particles;
        private readonly GoosePose pose = new GoosePose();

        private bool first = true;
        private float renderDir;
        private float headYaw;
        private float bobPhase;
        private float prevSpeed;
        private float squash;
        private float wingOpen;
        private float flapPhase;
        private float nextBlinkTime;
        private float blinkEndTime = -1f;
        private float honkEndTime = -1f;
        private const float HonkDuration = 0.45f;
        private float lastDustTime;
        private int lastTask = int.MinValue;
        private string[] taskIds;
        private int wanderTaskIndex = -1;
        private IdleAction idle = IdleAction.None;
        private float idleStart, idleEnd, nextIdleTime;
        private float idleSide = 1f;
#if HEADLESS
        public static bool HeadlessMouseHeld;
#endif
        private readonly string[] honkWords;
        private float nextZTime;
        private float sleepBlend;

        /// <summary>Visitors honk when they arrive, not when they are created.</summary>
        public bool SilentStart;
        /// <summary>Drawn asleep: eyes shut, head down, floating "z".</summary>
        public bool Asleep;

        public bool HasPose { get { return !first; } }
        public GoosePose Pose { get { return pose; } }

        public GooseAnimator(DeluxeConfig cfg, ParticleSystem particles)
        {
            this.cfg = cfg;
            this.particles = particles;
            honkWords = RussianPack.HonkWords(cfg.Language);
        }

        public void Honk(GooseEntity g, float now)
        {
            TriggerHonk(g, now, cfg.Scale);
        }

        public GoosePose Update(GooseEntity g, float dt, float now)
        {
            float speed = Asleep ? 0f : Vector2.Magnitude(g.velocity);
            sleepBlend = M.Lerp(sleepBlend, Asleep ? 1f : 0f, Math.Min(1f, dt * 3f));
            float walk = g.parameters.WalkSpeed, run = g.parameters.RunSpeed, charge = g.parameters.ChargeSpeed;
            float scale = cfg.Scale;

            if (first)
            {
                first = false;
                renderDir = g.direction;
                prevSpeed = speed;
                nextBlinkTime = now + M.Rand(1f, 3f);
                nextIdleTime = now + M.Rand(6f, 12f);
                try
                {
                    if (API.TaskDatabase != null && API.TaskDatabase.getAllLoadedTaskIDs != null)
                    {
                        taskIds = API.TaskDatabase.getAllLoadedTaskIDs();
                        wanderTaskIndex = Array.IndexOf(taskIds, "Wander");
                    }
                }
                catch { taskIds = null; }
                lastTask = g.currentTask;
                if (!SilentStart) TriggerHonk(g, now, scale); // the goose honks on startup
            }

            DetectTaskChange(g, now, scale);

            // --- direction: the goose snaps, we ease ---
            float diff = M.WrapDeg(g.direction - renderDir);
            float turnRate = cfg.SmoothTurning ? (speed > run ? 16f : 9f) : 1000f;
            renderDir += diff * Math.Min(1f, dt * turnRate);
            Vector2 fwd = Vector2.GetFromAngleDegrees(renderDir);
            Vector2 perp = Vector2.GetFromAngleDegrees(renderDir + 90f);

            // --- waddle ---
            float moving = M.Clamp01(speed / 60f);
            if (cfg.Waddle && speed > 2f)
            {
                float stepInterval = Math.Max(g.stepInterval, 0.05f);
                bobPhase += dt * (float)(Math.PI / stepInterval);
            }
            float bob = cfg.Waddle ? (float)Math.Sin(bobPhase * 2f) * 1.2f * moving : 0f;
            float sway = cfg.Waddle ? (float)Math.Sin(bobPhase) * 2.2f * moving : 0f;
            float headSway = cfg.Waddle ? (float)Math.Sin(bobPhase - 0.7f) * 0.9f * moving : 0f;

            // --- squash & stretch from acceleration ---
            if (cfg.SquashStretch && dt > 0f)
            {
                float accel = (speed - prevSpeed) / dt;
                float target = M.Clamp(accel / 9000f, -0.28f, 0.28f);
                // fast response to impulses, slow relaxation back to neutral
                float rate = Math.Abs(target) > Math.Abs(squash) ? 18f : 7f;
                squash = M.Lerp(squash, target, Math.Min(1f, dt * rate));
                if (Math.Abs(squash) < 0.002f) squash = 0f;
            }
            else squash = 0f;
            prevSpeed = speed;

            // --- honk ---
            bool honking = now < honkEndTime;
            float honkP = honking ? 1f - (honkEndTime - now) / HonkDuration : 0f;
            float honkE = honking ? (float)Math.Sin(honkP * Math.PI) : 0f;
            float beakOpen = cfg.HonkAnimation ? honkE : 0f;
            float puff = cfg.HonkAnimation ? honkE * 0.18f : 0f;

            // --- idle animations ---
            bool isIdle = !Asleep && speed < 8f && !honking && (wanderTaskIndex < 0 || g.currentTask == wanderTaskIndex);
            float stretchExtra = 0f;
            float headShake = 0f;
            float preenYaw = 0f;
            if (cfg.IdleAnimations)
            {
                if (idle == IdleAction.None)
                {
                    if (isIdle && now > nextIdleTime)
                    {
                        int pick = M.RandInt(3);
                        idle = pick == 0 ? IdleAction.Stretch : pick == 1 ? IdleAction.Shake : IdleAction.Preen;
                        idleStart = now;
                        idleEnd = now + (idle == IdleAction.Stretch ? 1.7f : idle == IdleAction.Shake ? 0.6f : 1.5f);
                        idleSide = M.RandInt(2) == 0 ? -1f : 1f;
                        if (idle == IdleAction.Shake && cfg.Particles)
                            particles.SpawnFeathers(g.position + M.Up * 40f * scale, 3, scale, g.renderData.brushGooseWhite.Color, now);
                    }
                    else if (!isIdle) nextIdleTime = Math.Max(nextIdleTime, now + M.Rand(4f, 8f));
                }
                else
                {
                    if (now > idleEnd || !isIdle)
                    {
                        idle = IdleAction.None;
                        nextIdleTime = now + M.Rand(8f, 20f);
                    }
                    else
                    {
                        float p = (now - idleStart) / (idleEnd - idleStart);
                        float e = (float)Math.Sin(p * Math.PI);
                        switch (idle)
                        {
                            case IdleAction.Stretch: // neck up, yawn
                                stretchExtra = e * 11f;
                                beakOpen = Math.Max(beakOpen, e * 0.75f);
                                break;
                            case IdleAction.Shake:   // shake the head, feathers fly
                                headShake = (float)Math.Sin(p * Math.PI * 9f) * 3.2f * e;
                                break;
                            case IdleAction.Preen:   // turn the head back and dip it
                                preenYaw = idleSide * 140f * M.EaseInOut(Math.Min(1f, p * 3f)) * (p > 0.75f ? (1f - p) * 4f : 1f);
                                stretchExtra = -7f * e;
                                break;
                        }
                    }
                }
            }

            // --- head yaw: look at the cursor when idle ---
            Vector2 mouse = new Vector2(Input.mouseX, Input.mouseY);
            Vector2 headApprox = g.position + M.Up * 30f * scale + fwd * 20f * scale;
            Vector2 toMouse = mouse - headApprox;
            float mouseDist = Vector2.Magnitude(toMouse);
            float targetYaw = 0f;
            if (idle == IdleAction.Preen) targetYaw = preenYaw;
            else if (Asleep) targetYaw = 0f;
            else if (cfg.LookAtCursor && isIdle && mouseDist > 25f && mouseDist < 520f)
                targetYaw = M.Clamp(M.WrapDeg(M.AngleDeg(toMouse) - renderDir), -60f, 60f);
            float yawRate = idle == IdleAction.Preen ? 10f : 6f;
            headYaw = M.Lerp(headYaw, targetYaw, Math.Min(1f, dt * yawRate));
            Vector2 fwdHead = Vector2.GetFromAngleDegrees(renderDir + headYaw);
            Vector2 perpHead = Vector2.GetFromAngleDegrees(renderDir + headYaw + 90f);
            Vector2 eyeLook = (!Asleep && cfg.LookAtCursor && mouseDist < 700f && mouseDist > 5f) ? Vector2.Normalize(toMouse) * 1.1f : Vector2.zero;

            // --- blink ---
            float blink = 0f;
            if (cfg.Blink)
            {
                if (now > nextBlinkTime)
                {
                    blinkEndTime = now + 0.14f;
                    nextBlinkTime = now + (M.RandInt(4) == 0 ? 0.35f : M.Rand(2f, 6f));
                }
                if (now < blinkEndTime)
                {
                    float bp = 1f - (blinkEndTime - now) / 0.14f;
                    blink = (float)Math.Sin(bp * Math.PI);
                }
            }

            if (Asleep)
            {
                blink = 1f;
                if (cfg.Particles && now > nextZTime)
                {
                    nextZTime = now + 1.1f;
                    particles.SpawnSleepZ(g.position + M.Up * 40f * scale + Vector2.GetFromAngleDegrees(renderDir) * 18f * scale, scale, now);
                }
            }

            // --- wings ---
            bool charging = speed > run * 0.95f;
            float wingTarget = (cfg.Wings && (charging || honking)) ? 1f : 0f;
            wingOpen = M.Lerp(wingOpen, wingTarget, Math.Min(1f, dt * 10f));
            if (wingOpen > 0.02f) flapPhase += dt * (charging ? 17f : 24f);
            else flapPhase = 0f;

            // --- dust when charging ---
            if (cfg.Particles && charging && now - lastDustTime > 0.055f)
            {
                lastDustTime = now;
                Vector2 foot = M.RandInt(2) == 0 ? g.rig.feets.lFootPos : g.rig.feets.rFootPos;
                particles.SpawnDust(ScaleAbout(foot, g.position, scale), g.velocity, scale, now);
            }

            // --- build the rig (same formulas as the goose, plus our offsets) ---
            float neckLerp = g.rig.neckLerpPercent;
            float neckH = M.Lerp(20f, 10f, neckLerp) + stretchExtra - 9f * sleepBlend; // sleeping: head tucked down
            float neckF = M.Lerp(3f, 16f, neckLerp);
            Vector2 fwdNeck = Vector2.Normalize(Vector2.Lerp(fwd, fwdHead, 0.5f));

            Vector2 bodyOffset = M.Up * bob + perp * sway;
            Vector2 headOffset = M.Up * bob * 0.6f + perp * (headSway + headShake);

            Vector2 underbody = M.Up * 9f + bodyOffset;
            Vector2 body = M.Up * 14f + bodyOffset;
            Vector2 neckBase = body + fwd * 15f;
            Vector2 neckHead = neckBase + fwdNeck * neckF + M.Up * neckH + headOffset;
            Vector2 head1 = neckHead + fwdHead * 3f - M.Up * 1f;
            Vector2 head2 = head1 + fwdHead * 5f;

            pose.pos = g.position;
            pose.scale = scale;
            pose.fwd = fwd; pose.perp = perp; pose.fwdHead = fwdHead; pose.perpHead = perpHead;
            pose.underbodyCenter = Place(underbody, g.position, fwd, squash, scale);
            pose.bodyCenter = Place(body, g.position, fwd, squash, scale);
            pose.neckBase = Place(neckBase, g.position, fwd, squash, scale);
            pose.neckHeadPoint = Place(neckHead, g.position, fwd, squash, scale);
            pose.head1EndPoint = Place(head1, g.position, fwd, squash, scale);
            pose.head2EndPoint = Place(head2, g.position, fwd, squash, scale);
            pose.lFoot = ScaleAbout(g.rig.feets.lFootPos, g.position, scale);
            pose.rFoot = ScaleAbout(g.rig.feets.rFootPos, g.position, scale);
            pose.blink = blink;
            pose.beakOpen = beakOpen;
            pose.wingOpen = wingOpen;
            pose.flap = flapPhase;
            pose.puff = puff;
            pose.squash = squash;
            pose.eyeLook = eyeLook;
            pose.speed01 = M.Clamp01(speed / charge);
            pose.headShake = headShake;
            pose.asleep = Asleep;
            pose.carry = CarryKind.None;
#if HEADLESS
            pose.mouseHeld = HeadlessMouseHeld;
#else
            // Cursor.Clip is the whole screen when nothing clips it; NabMouse clips to a ~15 px box.
            Rectangle clip = Cursor.Clip;
            pose.mouseHeld = clip.Width > 0 && clip.Width <= 64 && clip.Height <= 64;
#endif
            return pose;
        }

        /// <summary>Local offset -> screen: squash/stretch along fwd, then scale about the goose position.</summary>
        private static Vector2 Place(Vector2 local, Vector2 origin, Vector2 fwd, float squash, float scale)
        {
            float along = Vector2.Dot(local, fwd);
            float vert = Vector2.Dot(local, M.Up);
            Vector2 rest = local - fwd * along - M.Up * vert;
            Vector2 sq = fwd * (along * (1f + squash)) + M.Up * (vert * (1f - squash * 0.6f)) + rest;
            return origin + sq * scale;
        }

        private static Vector2 ScaleAbout(Vector2 p, Vector2 origin, float scale)
        {
            return origin + (p - origin) * scale;
        }

        private void DetectTaskChange(GooseEntity g, float now, float scale)
        {
            if (g.currentTask == lastTask) return;
            string oldId = IdOf(lastTask), newId = IdOf(g.currentTask);
            lastTask = g.currentTask;
            // The goose honks (Sound.HONCC) on SetTaskByID, which happens when it goes for the mouse
            // and when it returns to wandering after a collect/nab task. Random task picks are silent.
            bool honk = newId == "NabMouse"
                || (newId == "Wander" && (oldId == "CollectMeme" || oldId == "CollectNotepad" || oldId == "NabMouse" || oldId == "CollectDonateWindow"));
            if (honk) TriggerHonk(g, now, scale);
        }

        private string IdOf(int index)
        {
            if (taskIds == null || index < 0 || index >= taskIds.Length) return "";
            return taskIds[index] ?? "";
        }

        private void TriggerHonk(GooseEntity g, float now, float scale)
        {
            honkEndTime = now + HonkDuration;
            if (!cfg.Particles) return;
            Vector2 beak = g.position + M.Up * 34f * scale + Vector2.GetFromAngleDegrees(renderDir) * 26f * scale;
            if (cfg.HonkText)
                particles.SpawnText(beak + M.Up * 14f * scale, honkWords[M.RandInt(honkWords.Length)], Color.FromArgb(255, 245, 245, 245), now);
            particles.SpawnFeathers(g.position + M.Up * 38f * scale - Vector2.GetFromAngleDegrees(renderDir) * 6f * scale, 2, scale, g.renderData.brushGooseWhite.Color, now);
        }
    }
}
