using System;
using System.Collections.Generic;

namespace GooseDeluxe
{
    /// <summary>
    /// Ctrl+Alt+&lt;key&gt; detection by polling the keyboard, as a backup for RegisterHotKey (which fails
    /// silently for the user when another program already owns the combination). Fires once per press.
    /// </summary>
    internal sealed class HotkeyPoller
    {
        public const int VK_CONTROL = 0x11, VK_MENU = 0x12;

        private readonly Func<int, bool> isDown;
        private readonly Dictionary<char, bool> wasDown = new Dictionary<char, bool>();
        private readonly Dictionary<char, double> lastFired = new Dictionary<char, double>();
        private readonly Func<double> clock;

        /// <summary>A press already handled (e.g. by WM_HOTKEY) isn't fired again within this window.</summary>
        public double DedupSeconds = 0.6;

        public HotkeyPoller(Func<int, bool> isDown, Func<double> clock)
        {
            this.isDown = isDown;
            this.clock = clock;
        }

        /// <summary>Remember that <paramref name="key"/> was just handled through WM_HOTKEY.</summary>
        public void MarkFired(char key) { lastFired[key] = clock(); }

        /// <summary>Returns the keys whose Ctrl+Alt+key press started since the previous poll.</summary>
        public List<char> Poll(IEnumerable<char> keys)
        {
            List<char> fired = new List<char>();
            bool mods = isDown(VK_CONTROL) && isDown(VK_MENU);
            double now = clock();
            foreach (char k in keys)
            {
                bool down = mods && isDown(k);
                bool was;
                wasDown.TryGetValue(k, out was);
                wasDown[k] = down;
                if (!down || was) continue;
                double last;
                if (lastFired.TryGetValue(k, out last) && now - last < DedupSeconds) continue;
                lastFired[k] = now;
                fired.Add(k);
            }
            return fired;
        }
    }
}
