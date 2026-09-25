using System;
using System.Reflection;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// SamEngine.Deck.Reshuffle picks the swap index from [0, i) instead of [0, i], which is Sattolo's
    /// algorithm: it only ever produces cyclic permutations. With the three default random tasks the goose
    /// can only do them in 2 of the 6 possible orders (after the notepad always comes the mud).
    /// We can't patch the method, so whenever the deck wraps around we shuffle its cards again, properly.
    /// </summary>
    internal sealed class DeckFixer
    {
        private static readonly FieldInfo cursorField = typeof(Deck).GetField("i", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Func<Deck> getDeck;
        private readonly Random rng;
        private Deck lastDeck;
        private int lastCursor = -1;
        private int[] lastWritten;

        public int Fixes { get; private set; }

        public DeckFixer(Func<Deck> getDeck, Random rng = null)
        {
            this.getDeck = getDeck;
            this.rng = rng ?? new Random();
        }

        /// <summary>Finds the task deck inside the running goose; null if this isn't the goose process.</summary>
        public static Func<Deck> FromGooseDatabase()
        {
            try
            {
                Assembly exe = Assembly.GetEntryAssembly();
                Type db = exe == null ? null : exe.GetType("GooseDesktop.Refactor.GooseTasks.GooseTaskDatabase");
                FieldInfo deckField = db == null ? null : db.GetField("taskDeck", BindingFlags.NonPublic | BindingFlags.Static);
                if (deckField == null || cursorField == null) return null;
                return () => deckField.GetValue(null) as Deck;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Call once per frame. The deck's private cursor drops back to 0 right after it reshuffles, so a
        /// transition to 0 (or the very first look at a fresh deck) means "biased cards, shuffle them".
        /// Returns true when it reshuffled.
        /// </summary>
        public bool Update()
        {
            if (cursorField == null) return false;
            Deck deck = getDeck();
            if (deck == null || deck.indices == null || deck.indices.Length < 2) return false;
            if (!ReferenceEquals(deck, lastDeck))
            {
                lastDeck = deck;
                lastCursor = -1;
                lastWritten = null;
            }

            int cursor = (int)cursorField.GetValue(deck);
            bool wrapped = cursor == 0 && lastCursor != 0;
            lastCursor = cursor;
            if (!wrapped) return false;

            int[] cards = deck.indices;
            int previousLast = lastWritten != null && lastWritten.Length == cards.Length ? lastWritten[lastWritten.Length - 1] : -1;
            for (int i = cards.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = cards[i]; cards[i] = cards[j]; cards[j] = t;
            }
            // no back-to-back repeat of the same trick across the deck boundary
            if (cards[0] == previousLast)
            {
                int j = 1 + rng.Next(cards.Length - 1);
                int t = cards[0]; cards[0] = cards[j]; cards[j] = t;
            }
            lastWritten = (int[])cards.Clone();
            Fixes++;
            return true;
        }
    }
}
