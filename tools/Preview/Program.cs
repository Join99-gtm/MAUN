using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GooseDeluxe;
using GooseShared;
using SamEngine;

// Drives GooseAnimator + GooseRenderer with a fake GooseEntity through a handful of scenarios and
// writes one PNG per scenario plus a contact sheet. Usage: dotnet run -- <outDir>
internal static class Program
{
    private const int W = 300, H = 240;
    private static readonly Color Background = Color.FromArgb(255, 86, 120, 150);

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "preview-out";
        Directory.CreateDirectory(outDir);
        List<KeyValuePair<string, Bitmap>> frames = new List<KeyValuePair<string, Bitmap>>();

        frames.Add(Scenario("01-idle", HatStyle.None, Idle));
        frames.Add(Scenario("02-walk", HatStyle.None, Walk));
        frames.Add(Scenario("03-charge", HatStyle.None, Charge));
        frames.Add(Scenario("04-honk", HatStyle.None, Honk));
        frames.Add(Scenario("05-look-at-cursor", HatStyle.None, LookAtCursor));
        frames.Add(Scenario("06-blink", HatStyle.None, Blink));
        frames.Add(Scenario("07-yawn", HatStyle.None, Yawn));
        frames.Add(Scenario("08-preen", HatStyle.None, Preen));
        frames.Add(Scenario("09-mouse-held", HatStyle.None, MouseHeld));
        frames.Add(Scenario("10-tophat", HatStyle.TopHat, Idle));
        frames.Add(Scenario("11-party", HatStyle.Party, Walk));
        frames.Add(Scenario("12-santa", HatStyle.Santa, Idle));
        frames.Add(Scenario("13-walk-left", HatStyle.None, WalkLeft));
        frames.Add(Scenario("14-stop-squash", HatStyle.None, StopSquash));

        foreach (KeyValuePair<string, Bitmap> f in frames)
            f.Value.Save(Path.Combine(outDir, f.Key + ".png"), ImageFormat.Png);

        int cols = 4, rows = (frames.Count + cols - 1) / cols;
        using (Bitmap sheet = new Bitmap(W * cols, H * rows))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font font = new Font("DejaVu Sans", 9f, FontStyle.Bold))
        {
            g.Clear(Background);
            for (int i = 0; i < frames.Count; i++)
            {
                int x = (i % cols) * W, y = (i / cols) * H;
                g.DrawImage(frames[i].Value, x, y);
                g.DrawString(frames[i].Key, font, Brushes.White, x + 6, y + 4);
                g.DrawRectangle(Pens.DarkSlateGray, x, y, W - 1, H - 1);
            }
            sheet.Save(Path.Combine(outDir, "contact-sheet.png"), ImageFormat.Png);
        }
        Console.WriteLine("Wrote " + frames.Count + " frames to " + outDir);
        return 0;
    }

    // ---- scenario plumbing ----

    private sealed class Sim
    {
        public GooseEntity goose;
        public GooseAnimator animator;
        public GooseRenderer renderer;
        public ParticleSystem particles;
        public float now;
        public const float Dt = 1f / 60f;

        public GoosePose Step()
        {
            now += Dt;
            GoosePose p = animator.Update(goose, Dt, now);
            particles.Update(Dt, now);
            return p;
        }

        public void SetFeet()
        {
            Vector2 perp = Vector2.GetFromAngleDegrees(goose.direction + 90f);
            goose.rig.feets.lFootPos = goose.position - perp * 6f;
            goose.rig.feets.rFootPos = goose.position + perp * 6f;
        }
    }

    private static Sim NewSim(HatStyle hat)
    {
        DeluxeConfig cfg = new DeluxeConfig();
        cfg.Scale = 2.4f;
        cfg.Hat = hat;
        Sim s = new Sim();
        s.particles = new ParticleSystem();
        s.animator = new GooseAnimator(cfg, s.particles);
        s.renderer = new GooseRenderer(cfg);
        GooseEntity g = new GooseEntity(delegate { }, delegate { }, delegate { });
        g.parameters = new GooseEntity.ParametersTable();
        g.renderData = new GooseRenderData();
        g.renderData.brushGooseWhite = new SolidBrush(Color.White);
        g.renderData.brushGooseOrange = new SolidBrush(Color.Orange);
        g.renderData.brushGooseOutline = new SolidBrush(Color.LightGray);
        g.rig.feets = new ProceduralFeets();
        g.position = new Vector2(W * 0.5f - 10f, H * 0.62f);
        g.direction = 0f;
        g.velocity = Vector2.zero;
        g.currentTask = 0;
        s.goose = g;
        s.SetFeet();
        Input.mouseX = -1000; Input.mouseY = -1000;
        GooseAnimator.HeadlessMouseHeld = false;
        return s;
    }

    private static KeyValuePair<string, Bitmap> Scenario(string name, HatStyle hat, Func<Sim, GoosePose> run)
    {
        Sim s = NewSim(hat);
        GoosePose pose = run(s);
        Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Background);
            s.renderer.Draw(g, pose, s.goose, s.particles, s.now);
        }
        Console.WriteLine(name + ": particles=" + s.particles.Count + " beak=" + pose.beakOpen.ToString("0.00") +
            " wing=" + pose.wingOpen.ToString("0.00") + " blink=" + pose.blink.ToString("0.00") + " squash=" + pose.squash.ToString("0.00"));
        return new KeyValuePair<string, Bitmap>(name, bmp);
    }

    private static GoosePose Settle(Sim s, int frames)
    {
        GoosePose p = null;
        for (int i = 0; i < frames; i++) p = s.Step();
        return p;
    }

    // ---- scenarios ----

    private static GoosePose Idle(Sim s)
    {
        // let the startup honk finish, keep blink/idle timers from firing
        return Settle(s, 60);
    }

    private static GoosePose Walk(Sim s)
    {
        s.goose.velocity = new Vector2(80f, 0f);
        s.goose.stepInterval = 0.2f;
        s.goose.rig.neckLerpPercent = 0f;
        for (int i = 0; i < 70; i++) { s.Step(); s.goose.position += s.goose.velocity * (Sim.Dt * 0.05f); s.SetFeet(); }
        // stop at a phase where the waddle is visible
        return s.Step();
    }

    private static GoosePose WalkLeft(Sim s)
    {
        s.goose.direction = 180f;
        s.goose.velocity = new Vector2(-80f, 0f);
        s.goose.stepInterval = 0.2f;
        s.SetFeet();
        for (int i = 0; i < 70; i++) s.Step();
        return s.Step();
    }

    private static GoosePose Charge(Sim s)
    {
        s.goose.velocity = new Vector2(400f, 0f);
        s.goose.stepInterval = 0.1f;
        s.goose.rig.neckLerpPercent = 1f;
        return Settle(s, 45);
    }

    private static GoosePose Honk(Sim s)
    {
        // startup honk peaks around the middle of its 0.45 s
        return Settle(s, 13);
    }

    private static GoosePose LookAtCursor(Sim s)
    {
        Settle(s, 60);
        Input.mouseX = (int)(s.goose.position.x + 40f);
        Input.mouseY = (int)(s.goose.position.y - 160f);
        return Settle(s, 60);
    }

    private static GoosePose Blink(Sim s)
    {
        Settle(s, 60);
        GoosePose p = null;
        for (int i = 0; i < 2000; i++) { p = s.Step(); if (p.blink > 0.85f) return p; }
        return p;
    }

    private static GoosePose Yawn(Sim s)
    {
        Settle(s, 60);
        GoosePose p = null;
        for (int i = 0; i < 20000; i++)
        {
            p = s.Step();
            if (p.beakOpen > 0.6f && p.neckHeadPoint.y < s.goose.position.y - 40f * 2.4f * 0.9f - 15f) return p;
        }
        Console.WriteLine("  (yawn not reached)");
        return p;
    }

    private static GoosePose Preen(Sim s)
    {
        Settle(s, 60);
        GoosePose p = null;
        for (int i = 0; i < 20000; i++)
        {
            p = s.Step();
            if (Vector2.Dot(p.fwdHead, p.fwd) < -0.6f) return p;
        }
        Console.WriteLine("  (preen not reached)");
        return p;
    }

    private static GoosePose MouseHeld(Sim s)
    {
        Settle(s, 60);
        GooseAnimator.HeadlessMouseHeld = true;
        return s.Step();
    }

    private static GoosePose StopSquash(Sim s)
    {
        s.goose.velocity = new Vector2(400f, 0f);
        Settle(s, 40);
        s.goose.velocity = Vector2.zero;
        return Settle(s, 3);
    }
}
