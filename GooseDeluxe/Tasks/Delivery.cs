namespace GooseDeluxe
{
    /// <summary>A note or a picture the goose is about to drag onto the screen.</summary>
    internal sealed class DeliveryPayload
    {
        public string FromName;
        /// <summary>Set when the sender is a goose: the note window then offers "reply by goose".</summary>
        public string FromCode;
        public string Text;
        public byte[] ImageBytes;
        public string FileName;

        public bool IsImage { get { return ImageBytes != null; } }
    }

    /// <summary>The window the goose drags in with its beak (a WinForms form in the goose, a fake in tests).</summary>
    internal interface INoteWindow
    {
        int Width { get; }
        int Height { get; }
        /// <summary>Closed by the user or otherwise disposed: stop dragging it.</summary>
        bool IsGone { get; }
        void MoveTo(int x, int y);
        /// <summary>Show it (deferred to outside the goose's paint); safe to call repeatedly.</summary>
        void ShowNote();
    }

    /// <summary>An outgoing message the goose runs off with: it waits off-screen until the upload is done.</summary>
    internal sealed class CarryJob
    {
        public const int Sending = 0, Sent = 1, Failed = 2;
        public CarryKind Kind = CarryKind.Note;
        public volatile int State = Sending;
        public string To = "";
    }
}
