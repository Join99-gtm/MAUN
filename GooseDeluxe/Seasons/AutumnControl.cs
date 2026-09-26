using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// The goose's official Autumn mod (Assets/Mods/Autumn) spawns leaf piles all year round: it never looks
    /// at the date. Outside autumn we empty its public static pile list (Autumn.ModEntryPoint.piles), so it
    /// has nothing to draw and its "chase a leaf pile" task gives up at once.
    /// In autumn a click on a pile kicks it the way the goose does (LeafPile.Kick): the leaves fly apart,
    /// and the pile fades out a few seconds later instead of the Autumn mod's ten.
    /// </summary>
    internal sealed class AutumnControl
    {
        /// <summary>After a click the leaves settle for this long, then fade (the Autumn mod fades over 8–10 s after a kick).</summary>
        public const float SecondsLeftAfterClick = 3f;
        /// <summary>Kick strength as the goose's "speed percentage": 1 is a charging goose, 2 flings the leaves further.</summary>
        public const float ClickKickStrength = 2f;

        private IList piles;
        private FieldInfo posField, radField, kickedField;
        private MethodInfo kickMethod;

        public struct Pile
        {
            public Vector2 Pos;
            public float Rad;
            public bool Kicked;
        }

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
                    if (list == null) continue;
                    AutumnControl c = new AutumnControl { piles = list };
                    Type pile = asm.GetType("LeafPile", false);
                    if (pile != null)
                    {
                        const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
                        c.posField = pile.GetField("pos", inst);
                        c.radField = pile.GetField("rad", inst);
                        c.kickedField = pile.GetField("timeSinceKicked", inst);
                        c.kickMethod = pile.GetMethod("Kick", inst, null, new[] { typeof(Vector2), typeof(Vector2), typeof(float) }, null);
                    }
                    return c;
                }
                catch { }
            }
            return null;
        }

        public int Count { get { return piles.Count; } }

        /// <summary>Piles can be kicked by a click (the Autumn mod is the version we know).</summary>
        public bool CanKick
        {
            get { return posField != null && radField != null && kickedField != null && kickMethod != null; }
        }

        /// <summary>Removes every leaf pile; returns how many there were.</summary>
        public int Suppress()
        {
            int n = piles.Count;
            if (n > 0) piles.Clear();
            return n;
        }

        /// <summary>Where the piles are right now (in the goose window's coordinates).</summary>
        public List<Pile> Piles()
        {
            List<Pile> list = new List<Pile>();
            if (!CanKick) return list;
            foreach (object p in piles)
            {
                if (p == null) continue;
                list.Add(new Pile
                {
                    Pos = (Vector2)posField.GetValue(p),
                    Rad = (float)radField.GetValue(p),
                    Kicked = (float)kickedField.GetValue(p) > 0f,
                });
            }
            return list;
        }

        /// <summary>Index of the untouched pile under <paramref name="point"/> (the one nearest the viewer), or -1.</summary>
        public int PileAt(Vector2 point)
        {
            if (!CanKick) return -1;
            int best = -1;
            float bestY = float.MinValue;
            for (int i = 0; i < piles.Count; i++)
            {
                object p = piles[i];
                if (p == null || (float)kickedField.GetValue(p) > 0f) continue;
                Vector2 pos = (Vector2)posField.GetValue(p);
                if (LeafHit.IsOnPile(pos, (float)radField.GetValue(p), point) && pos.y > bestY)
                {
                    best = i;
                    bestY = pos.y;
                }
            }
            return best;
        }

        /// <summary>Kicks every untouched pile at once. Returns how many.</summary>
        public int KickAll(float now)
        {
            if (!CanKick) return 0;
            int n = 0;
            foreach (object p in piles)
            {
                if (p == null || (float)kickedField.GetValue(p) > 0f) continue;
                Kick(p, now);
                n++;
            }
            return n;
        }

        private void Kick(object p, float now)
        {
            Vector2 pos = (Vector2)posField.GetValue(p);
            kickMethod.Invoke(p, new object[] { new Vector2(0f, 0f), pos, ClickKickStrength });
            kickedField.SetValue(p, Math.Max(now - (8f - SecondsLeftAfterClick), 0.001f));
        }

        /// <summary>Kicks the pile under the point: the leaves fly up and apart. Returns false if there is none.</summary>
        public bool KickAt(Vector2 point, float now)
        {
            int i = PileAt(point);
            if (i < 0) return false;
            // no direction: the leaves burst out in all directions, harder than a charging goose kicks them.
            // The Autumn mod removes a kicked pile 10 s after the kick, fading over the last 2 s; Kick
            // pretends the kick was earlier so the leaves are gone soon after they land.
            Kick(piles[i], now);
            return true;
        }
    }
}
