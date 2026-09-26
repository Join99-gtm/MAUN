using System;
using System.Collections.Generic;

namespace GooseDeluxe
{
    /// <summary>
    /// Hands out items in shuffled rounds: every item once before any comes back, and never the same one
    /// twice in a row, not even across rounds. The goose picks memes and notes with plain random.Next,
    /// so with its 8 memes the same one comes twice in a row every eighth time.
    /// The item list is passed on every call (files can be added while the goose runs); when it
    /// changes, a new round starts.
    /// </summary>
    internal sealed class NoRepeatDeck
    {
        private readonly Random rng;
        private readonly List<string> order = new List<string>();
        private readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int next;
        private string last;

        public NoRepeatDeck(Random rng = null) { this.rng = rng ?? new Random(); }

        public string Last { get { return last; } }

        public string Next(IList<string> items)
        {
            if (items == null || items.Count == 0) return null;
            if (!SameSet(items)) Reset(items);
            if (next >= order.Count) Shuffle();
            last = order[next++];
            return last;
        }

        /// <summary>The item <see cref="Next"/> will return, without taking it (to get it ready in advance).</summary>
        public string Peek(IList<string> items)
        {
            if (items == null || items.Count == 0) return null;
            if (!SameSet(items)) Reset(items);
            if (next >= order.Count) Shuffle();
            return order[next];
        }

        private bool SameSet(IList<string> items)
        {
            if (items.Count != known.Count) return false;
            foreach (string s in items) if (!known.Contains(s)) return false;
            return true;
        }

        private void Reset(IList<string> items)
        {
            known.Clear();
            foreach (string s in items) known.Add(s);
            order.Clear();
            order.AddRange(known);
            next = order.Count; // shuffle on the next draw
        }

        private void Shuffle()
        {
            // Fisher–Yates: every order equally likely (unlike the goose's own Sattolo "Deck")
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                string t = order[i]; order[i] = order[j]; order[j] = t;
            }
            if (order.Count > 1 && last != null && string.Equals(order[0], last, StringComparison.OrdinalIgnoreCase))
            {
                int j = 1 + rng.Next(order.Count - 1);
                string t = order[0]; order[0] = order[j]; order[j] = t;
            }
            next = 0;
        }
    }
}
