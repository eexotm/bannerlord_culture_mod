using TaleWorlds.CampaignSystem;

namespace FlowingTime
{
    /// <summary>
    /// What the player last asked the clock to do. Only player actions (pause key, speed keys, time bar
    /// buttons, clicking to move) change it, so the game's automatic pauses can be told apart from the
    /// player's own pause and undone.
    /// </summary>
    internal static class TimeIntent
    {
        /// <summary>False until the player starts time, matching vanilla's paused state after loading.</summary>
        public static bool WantsRunning { get; private set; }

        /// <summary>True for fast forward, false for normal play.</summary>
        public static bool FastForward { get; private set; }

        public static void Reset()
        {
            WantsRunning = false;
            FastForward = false;
        }

        /// <summary>Player used Campaign.SetTimeSpeed: 0 = pause, 1 = play, 2 = fast forward.</summary>
        public static void FromSpeed(int speed)
        {
            if (speed == 0)
            {
                WantsRunning = false;
            }
            else if (speed == 1 || speed == 2)
            {
                WantsRunning = true;
                FastForward = speed == 2;
            }
        }

        /// <summary>Player action left the clock in this mode (e.g. a time bar button or a map click).</summary>
        public static void FromMode(CampaignTimeControlMode mode)
        {
            if (mode == CampaignTimeControlMode.Stop || mode == CampaignTimeControlMode.FastForwardStop)
            {
                WantsRunning = false;
            }
            else
            {
                WantsRunning = true;
                FastForward = IsFastForward(mode);
            }
        }

        /// <summary>
        /// Keeps the remembered speed in step with the clock while it runs, e.g. when a single map click
        /// during fast forward drops back to normal play.
        /// </summary>
        public static void SyncSpeed(CampaignTimeControlMode mode)
        {
            if (WantsRunning && IsRunning(mode))
            {
                FastForward = IsFastForward(mode);
            }
        }

        public static bool IsRunning(CampaignTimeControlMode mode)
        {
            return mode != CampaignTimeControlMode.Stop && mode != CampaignTimeControlMode.FastForwardStop;
        }

        public static bool IsFastForward(CampaignTimeControlMode mode)
        {
            return mode == CampaignTimeControlMode.StoppableFastForward
                || mode == CampaignTimeControlMode.UnstoppableFastForward
                || mode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime;
        }
    }
}
