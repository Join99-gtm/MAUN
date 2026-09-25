using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Mod entry point. On the first tick we replace the goose's render delegate with a no-op
    /// (so its colour-keyed window draws nothing) and draw the goose ourselves onto a per-pixel
    /// alpha overlay. If anything throws, the original renderer is restored and the error is logged
    /// to GooseDeluxe.log next to the DLL, so a bug here never takes the goose down with it.
    /// </summary>
    public class ModEntryPoint : IMod
    {
        private string modDir;
        private DeluxeConfig cfg;
        private OverlayWindow overlay;
        private ParticleSystem particles;
        private GooseAnimator animator;
        private GooseRenderer renderer;
        private GooseEntity.RenderFunction originalRender;
        private bool hooked;
        private bool failed;
        private float lastTime = -1f;

        void IMod.Init()
        {
            try
            {
                modDir = API.Helper != null && API.Helper.getModDirectory != null
                    ? API.Helper.getModDirectory(this)
                    : Path.GetDirectoryName(typeof(ModEntryPoint).Assembly.Location);
                cfg = DeluxeConfig.Load(Path.Combine(modDir, "GooseDeluxe.ini"));
            }
            catch (Exception ex)
            {
                cfg = new DeluxeConfig();
                Log("Config load failed, using defaults: " + ex);
            }
            particles = new ParticleSystem();
            animator = new GooseAnimator(cfg, particles);
            renderer = new GooseRenderer(cfg);
            InjectionPoints.PreTickEvent += OnPreTick;
            InjectionPoints.PostRenderEvent += OnPostRender;
        }

        private void OnPreTick(GooseEntity goose)
        {
            if (hooked || failed) return;
            try
            {
                overlay = new OverlayWindow(MainWindowBounds());
                overlay.Show();
                overlay.KeepOnTop();
                originalRender = goose.render;
                goose.render = NoRender;
                hooked = true;
            }
            catch (Exception ex)
            {
                Fail(goose, ex);
            }
        }

        private void OnPostRender(GooseEntity goose, Graphics unused)
        {
            if (!hooked || failed) return;
            try
            {
                float now = Time.time;
                float dt = lastTime < 0f ? 1f / 60f : M.Clamp(now - lastTime, 0f, 0.1f);
                lastTime = now;

                overlay.ResizeTo(MainWindowBounds());
                GoosePose pose = animator.Update(goose, dt, now);
                particles.Update(dt, now);

                using (Graphics g = Graphics.FromImage(overlay.Surface))
                {
                    g.Clear(Color.Transparent);
                    renderer.Draw(g, pose, goose, particles, now);
                }
                overlay.Present();
                overlay.KeepOnTop();
            }
            catch (Exception ex)
            {
                Fail(goose, ex);
            }
        }

        private static void NoRender(GooseEntity g, Graphics gfx) { }

        private static Rectangle MainWindowBounds()
        {
            if (Application.OpenForms.Count > 0)
            {
                Form main = Application.OpenForms[0];
                if (main != null && main.Width > 0 && main.Height > 0) return main.Bounds;
            }
            return Screen.PrimaryScreen.WorkingArea;
        }

        private void Fail(GooseEntity goose, Exception ex)
        {
            failed = true;
            Log(ex.ToString());
            try
            {
                if (originalRender != null) goose.render = originalRender;
                if (overlay != null) { overlay.Hide(); overlay.Dispose(); overlay = null; }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                string dir = modDir ?? Path.GetDirectoryName(typeof(ModEntryPoint).Assembly.Location);
                File.AppendAllText(Path.Combine(dir, "GooseDeluxe.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
