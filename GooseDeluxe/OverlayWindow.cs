using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GooseDeluxe
{
    /// <summary>
    /// Click-through, non-activating, always-on-top window with real per-pixel alpha.
    /// The goose's own window uses a colour key, which makes anti-aliased edges impossible;
    /// this one is backed by a 32-bit DIB and pushed to the compositor with UpdateLayeredWindow.
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        private IntPtr memDc;
        private IntPtr dib;
        private IntPtr oldBitmap;
        private Bitmap surface;
        private Rectangle bounds;

        public Bitmap Surface { get { return surface; } }

        public OverlayWindow(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(1, 1);
            TopMost = true;
            Text = "GooseDeluxe overlay";
            ResizeTo(screenBounds);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // WinForms would otherwise try to paint the (ignored) window surface every frame.
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void ResizeTo(Rectangle screenBounds)
        {
            if (screenBounds.Width < 1 || screenBounds.Height < 1) return;
            if (surface != null && screenBounds == bounds) return;
            FreeSurface();
            bounds = screenBounds;
            Bounds = screenBounds;

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                memDc = Native.CreateCompatibleDC(screenDc);
                Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
                bi.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                bi.biWidth = bounds.Width;
                bi.biHeight = -bounds.Height; // top-down, so the stride matches GDI+ expectations
                bi.biPlanes = 1;
                bi.biBitCount = 32;
                IntPtr bits;
                dib = Native.CreateDIBSection(screenDc, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
                oldBitmap = Native.SelectObject(memDc, dib);
                // GDI+ draws straight into the DIB memory: no per-frame copy of the whole screen.
                surface = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>Push the current surface to the screen.</summary>
        public void Present()
        {
            if (surface == null || !IsHandleCreated) return;
            Native.POINT dst = new Native.POINT(bounds.Left, bounds.Top);
            Native.POINT src = new Native.POINT(0, 0);
            Native.SIZE size = new Native.SIZE(bounds.Width, bounds.Height);
            Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
            blend.BlendOp = Native.AC_SRC_OVER;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = Native.AC_SRC_ALPHA;
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        /// <summary>The goose's main window re-asserts TopMost every frame; keep up with it.</summary>
        public void KeepOnTop()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void FreeSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
                Native.DeleteDC(memDc);
                memDc = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
        }

        protected override void Dispose(bool disposing)
        {
            FreeSurface();
            base.Dispose(disposing);
        }
    }
}
