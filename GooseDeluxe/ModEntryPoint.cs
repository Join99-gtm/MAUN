using GooseShared;

namespace GooseDeluxe
{
    /// <summary>
    /// Mod entry point, created by the goose's mod loader. All the work happens in <see cref="Controller"/>;
    /// see it for how the mod hooks into the goose.
    /// </summary>
    public class ModEntryPoint : IMod
    {
        private readonly Controller controller = new Controller();

        void IMod.Init()
        {
            controller.Init(this);
        }
    }
}
