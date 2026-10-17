using TaleWorlds.CampaignSystem;

namespace FlowingTime
{
    /// <summary>
    /// What the player last asked the clock to do. Only player actions (pause key, speed keys, time bar
    /// buttons, starting to move) change it, so the game's automatic pauses can be told apart from the
    /// player's own pause.
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

        /// <summary>
        /// Player used Campaign.SetTimeSpeed: 0 = pause, 1 = play, 2 = fast forward. In a vanilla wait
        /// menu the speed is forced by the wait, so only pause/resume is remembered there.
        /// </summary>
        public static void FromSpeed(int speed, bool inWaitMenu)
        {
            if (speed == 0)
            {
                WantsRunning = false;
            }
            else if (speed == 1 || speed == 2)
            {
                WantsRunning = true;
                if (!inWaitMenu) FastForward = speed == 2;
            }
        }

        /// <summary>Player action left the clock in this mode (a time bar button, starting to move).</summary>
        public static void FromMode(CampaignTimeControlMode mode)
        {
            if (!IsRunning(mode))
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
        /// Keeps the remembered speed in step with the clock while it runs outside wait menus, e.g. a
        /// double click switching to fast forward, or a single click dropping back to normal play.
        /// </summary>
        public static void SyncSpeed(CampaignTimeControlMode mode)
        {
            if (WantsRunning && IsRunning(mode)) FastForward = IsFastForward(mode);
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
