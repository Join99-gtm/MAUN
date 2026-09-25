using System;
using System.Collections;
using System.Reflection;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's official Autumn mod (Assets/Mods/Autumn) spawns leaf piles all year round: it never looks
    /// at the date. Outside autumn we empty its public static pile list (Autumn.ModEntryPoint.piles), so it
    /// has nothing to draw and its "chase a leaf pile" task gives up at once.
    /// </summary>
    internal sealed class AutumnControl
    {
        private IList piles;

        public static AutumnControl Find()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType("Autumn.ModEntryPoint", false);
                    if (t == null) continue;
                    FieldInfo f = t.GetField("piles", BindingFlags.Public | BindingFlags.Static);
                    IList list = f == null ? null : f.GetValue(null) as IList;
                    if (list != null) return new AutumnControl { piles = list };
                }
                catch { }
            }
            return null;
        }

        public int Count { get { return piles.Count; } }

        /// <summary>Removes every leaf pile; returns how many there were.</summary>
        public int Suppress()
        {
            int n = piles.Count;
            if (n > 0) piles.Clear();
            return n;
        }
    }
}
