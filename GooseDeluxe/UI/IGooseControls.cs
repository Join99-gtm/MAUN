using System;
using System.Collections.Generic;

namespace GooseDeluxe
{
    internal enum DiagLevel { Ok, Info, Warn, Fail }

    /// <summary>One line of the self-check: what was checked, how it went, and details.</summary>
    internal sealed class DiagItem
    {
        public DiagLevel Level;
        public string Title;
        public string Detail;

        public DiagItem(DiagLevel level, string title, string detail)
        {
            Level = level;
            Title = title;
            Detail = detail ?? "";
        }

        public string Mark
        {
            get
            {
                switch (Level)
                {
                    case DiagLevel.Ok: return "✔";
                    case DiagLevel.Warn: return "⚠";
                    case DiagLevel.Fail: return "✖";
                    default: return "•";
                }
            }
        }

        public override string ToString() { return Mark + " " + Title + (Detail.Length > 0 ? ": " + Detail : ""); }
    }

    /// <summary>The text "Скопировать отчёт" puts on the clipboard: every check plus the end of the log.</summary>
    internal static class DiagReport
    {
        public static string Build(List<DiagItem> items, string log)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Отчёт GooseDeluxe " + ModInfo.Version + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (DiagItem d in items) sb.AppendLine(d.ToString());
            sb.AppendLine("--- GooseDeluxe.log ---");
            sb.AppendLine(log);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Everything the control panel can do to the goose. The mod's Controller implements it; the UI
    /// preview implements it with a fake, so the panel can be rendered and checked without Windows.
    /// </summary>
    internal interface IGooseControls
    {
        DeluxeConfig Config { get; }

        // actions
        void Come();
        void HonkNow();
        void RunGooseTask(string id);
        bool Paused { get; }
        void TogglePause();
        void Exit();

        // the goose's own settings (config.ini)
        bool GooseSettingsAvailable { get; }
        bool Muted { get; set; }
        bool GooseMayStealMouse { get; set; }

        /// <summary>A GooseDeluxe setting changed in the panel: apply it right away and save the ini.</summary>
        void ApplyConfig(string key);

        // friends
        bool FriendsEnabled { get; }
        bool HasFriend { get; }
        string FriendName { get; }
        string MyCodeDisplay { get; }
        void SendNote();
        void SendPicture();
        void SendPrank(GooseCommand command);
        void CopyMyCode();
        void CopyInvite();
        void EditFriend();

        // self-check
        List<DiagItem> RunDiagnostics();
        string LogTail(int lines);
        void TestNote();
        void TestConnection(Action<string> report);
        void TestSnow();
        void OpenModFolder();
    }
}
