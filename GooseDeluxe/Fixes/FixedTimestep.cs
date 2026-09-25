using System;
using System.Diagnostics;
using GooseShared;
using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Wraps the goose's tick. The goose integrates movement with a hard-coded 1/120 s step but is
    /// driven by a Sleep(8) loop that usually runs at ~64 FPS, so it walks at roughly half the speed
    /// its author tuned. This runs the original tick as many times per frame as real time demands.
    /// It is also where pausing happens: while paused the original tick is not called at all.
    /// </summary>
    internal sealed class FixedTimestep
    {
        public const float Step = 1f / 120f;
        public const int MaxStepsPerFrame = 8;
        private const double StepSeconds = 1.0 / 120.0; // the accumulator runs in double: no drift over hours

        private readonly GooseEntity.TickFunction original;
        private readonly Func<double> clock;
        private double last = -1;
        private double acc;

        /// <summary>When false the wrapper calls the original tick exactly once per frame.</summary>
        public bool Enabled = true;
        /// <summary>Sub-steps executed in the last frame; extra geese are ticked the same number of times.</summary>
        public int LastSteps;

        public GooseEntity.TickFunction Original { get { return original; } }

        public FixedTimestep(GooseEntity.TickFunction original, Func<double> clock = null)
        {
            this.original = original;
            if (clock == null)
            {
                Stopwatch sw = Stopwatch.StartNew();
                clock = () => sw.Elapsed.TotalSeconds;
            }
            this.clock = clock;
        }

        /// <summary>Forget the time already accumulated (after a pause), so the goose doesn't jump.</summary>
        public void Reset()
        {
            last = -1;
            acc = 0;
        }

        public void Tick(GooseEntity g)
        {
            if (Deluxe.Sleeping || Deluxe.HiddenForFullscreen)
            {
                Reset();
                LastSteps = 0;
                return;
            }
            LastSteps = StepsForThisFrame();
            RunSteps(original, g, LastSteps);
        }

        /// <summary>How many 1/120 s steps the time since the previous frame is worth.</summary>
        public int StepsForThisFrame()
        {
            if (!Enabled) return 1;
            double now = clock();
            double dt = last < 0 ? StepSeconds : now - last;
            last = now;
            if (dt < 0) dt = 0;
            if (dt > 0.25) dt = 0.25; // after a stall (a dialog, the PC sleeping) don't make the goose teleport
            acc += dt;
            int steps = 0;
            while (acc >= StepSeconds - 1e-9 && steps < MaxStepsPerFrame)
            {
                acc -= StepSeconds;
                steps++;
            }
            if (acc < 0) acc = 0;
            if (acc > StepSeconds * MaxStepsPerFrame) acc = 0;
            return steps;
        }

        /// <summary>
        /// Runs a tick function <paramref name="steps"/> times. A mouse click is shown to the first sub-step
        /// only, otherwise one click would make the goose react several times.
        /// </summary>
        public static void RunSteps(GooseEntity.TickFunction tick, GooseEntity g, int steps)
        {
            if (steps <= 0) return;
            ButtonState saved = Input.leftMouseButton;
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    tick(g);
                    Input.leftMouseButton.Clicked = false;
                }
            }
            finally
            {
                Input.leftMouseButton = saved;
            }
        }
    }
}
