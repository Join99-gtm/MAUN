using System;

namespace GooseDeluxe
{
    internal enum Season { None, Winter, Spring, Summer, Autumn }

    /// <summary>What season it is for the goose: by the calendar (meteorological seasons) or forced in the ini.</summary>
    internal static class SeasonClock
    {
        /// <summary>Replaceable for tests. DateTime.Now follows changes of the system clock immediately.</summary>
        public static Func<DateTime> Now = () => DateTime.Now;

        public static Season Of(DateTime d)
        {
            switch (d.Month)
            {
                case 12: case 1: case 2: return Season.Winter;
                case 3: case 4: case 5: return Season.Spring;
                case 6: case 7: case 8: return Season.Summer;
                default: return Season.Autumn;
            }
        }

        /// <summary><paramref name="setting"/> is the normalized ini value: Auto, Off, Winter, Spring, Summer or Autumn.</summary>
        public static Season Current(string setting)
        {
            if (string.Equals(setting, "Off", StringComparison.OrdinalIgnoreCase)) return Season.None;
            Season forced;
            if (!string.IsNullOrEmpty(setting) && !string.Equals(setting, "Auto", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(setting, true, out forced) && forced != Season.None)
                return forced;
            return Of(Now());
        }

        /// <summary>New Year week(s): 20 December – 10 January.</summary>
        public static bool IsNewYear(DateTime d)
        {
            return (d.Month == 12 && d.Day >= 20) || (d.Month == 1 && d.Day <= 10);
        }

        /// <summary>Accepts English and Russian names; null when the value means nothing.</summary>
        public static string NormalizeSetting(string value)
        {
            if (value == null) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "auto": case "авто": case "по дате": return "Auto";
                case "off": case "выкл": case "нет": case "false": return "Off";
                case "winter": case "зима": return "Winter";
                case "spring": case "весна": return "Spring";
                case "summer": case "лето": return "Summer";
                case "autumn": case "fall": case "осень": return "Autumn";
                default: return null;
            }
        }
    }
}
