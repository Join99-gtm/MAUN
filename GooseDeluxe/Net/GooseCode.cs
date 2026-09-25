using System;
using System.Security.Cryptography;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// A goose's address: 12 random characters shown as GUS-XXXX-XXXX-XXXX. Its ntfy topic is derived
    /// from it, so the code is also the secret that lets someone reach this goose: 32^12 ≈ 10^18
    /// possibilities, nobody guesses it.
    /// </summary>
    internal static class GooseCode
    {
        // no I, O, 0, 1: easy to dictate over the phone
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int Length = 12;

        public static string Generate()
        {
            byte[] bytes = new byte[Length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            StringBuilder sb = new StringBuilder(Length);
            foreach (byte b in bytes) sb.Append(Alphabet[b % Alphabet.Length]); // 256 % 32 == 0: no bias
            return sb.ToString();
        }

        /// <summary>Accepts "GUS-ABCD-EFGH-JKLM", "abcd efgh jklm", a pasted invite line…; null if it isn't a code.</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            StringBuilder sb = new StringBuilder();
            foreach (char ch in input.ToUpperInvariant())
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            string s = sb.ToString();
            int gus = s.IndexOf("GUS", StringComparison.Ordinal);
            if (s.Length != Length && gus >= 0 && s.Length - gus - 3 >= Length) s = s.Substring(gus + 3, Length);
            if (s.Length != Length) return null;
            foreach (char ch in s) if (Alphabet.IndexOf(ch) < 0) return null;
            return s;
        }

        public static string Display(string code)
        {
            if (code == null || code.Length != Length) return code ?? "";
            return "GUS-" + code.Substring(0, 4) + "-" + code.Substring(4, 4) + "-" + code.Substring(8, 4);
        }

        public static string Topic(string code)
        {
            return "goosedeluxe-" + code.ToLowerInvariant();
        }
    }
}
