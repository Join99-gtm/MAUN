using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>
    /// Stops and restarts the goose's whole engine. The goose runs everything from an Application.Idle
    /// loop that re-asserts TopMost and invalidates a screen-sized canvas ~100 times a second; the
    /// canvas' Paint handler ticks the game and, every frame, calls ClipCursor(NULL), which lets the
    /// mouse escape games that confine it. Unhooking both handlers makes the goose fully dormant:
    /// no CPU, no z-order fights, no cursor meddling. Hooking them back resumes it where it was.
    /// </summary>
    internal static class Engine
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static EventHandler idleHandler;
        private static PaintEventHandler paintHandler;
        private static Control canvas;
        private static Form mainForm;
        private static bool mainHidden;

        public static bool Available { get; private set; }
        public static bool Frozen { get; private set; }

        public static Form MainForm
        {
            get
            {
                if (mainForm != null) return mainForm;
                return Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            }
        }

        public static void Init()
        {
            try
            {
                Assembly exe = Assembly.GetEntryAssembly();
                Type program = exe == null ? null : exe.GetType("GooseDesktop.Program");
                if (program == null) return;
                const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Static;
                MethodInfo idle = program.GetMethod("HandleApplicationIdle", priv);
                MethodInfo render = program.GetMethod("Render", priv);
                FieldInfo canvasField = program.GetField("canvas", priv);
                FieldInfo mainField = program.GetField("mainForm", BindingFlags.Public | BindingFlags.Static);
                if (idle == null || render == null || canvasField == null || mainField == null) return;
                idleHandler = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), idle);
                paintHandler = (PaintEventHandler)Delegate.CreateDelegate(typeof(PaintEventHandler), render);
                canvas = canvasField.GetValue(null) as Control;
                mainForm = mainField.GetValue(null) as Form;
                Available = canvas != null && mainForm != null;
            }
            catch (Exception ex)
            {
                Deluxe.Log("Engine hooks unavailable: " + ex.Message);
                Available = false;
            }
        }

        /// <summary>Must be called on the UI thread (Application.Idle is per thread).</summary>
        public static void Freeze(bool hideMainWindow)
        {
            if (!Available) return;
            if (!Frozen)
            {
                Application.Idle -= idleHandler;
                canvas.Paint -= paintHandler;
                Frozen = true;
            }
            // the canvas keeps its last frame on screen (e.g. autumn leaves); hide it when we must be invisible
            if (hideMainWindow != mainHidden && mainForm.IsHandleCreated)
            {
                ShowWindow(mainForm.Handle, hideMainWindow ? SW_HIDE : SW_SHOWNOACTIVATE);
                mainHidden = hideMainWindow;
            }
        }

        public static void Thaw()
        {
            if (!Available) return;
            if (mainHidden && mainForm.IsHandleCreated)
            {
                ShowWindow(mainForm.Handle, SW_SHOWNOACTIVATE);
                mainHidden = false;
            }
            if (Frozen)
            {
                canvas.Paint += paintHandler;
                Application.Idle += idleHandler;
                Frozen = false;
                canvas.Invalidate();
            }
        }
    }
}
