using TaleWorlds.CampaignSystem;

namespace FlowingTime
{
    /// <summary>
    /// Whether the player last asked the clock to run or to pause. Only player actions (pause key, speed
    /// keys, time bar buttons, starting to move) change it, so the game's automatic pauses can be told
    /// apart from the player's own pause.
    /// </summary>
    internal static class TimeIntent
    {
        /// <summary>False until the player starts time, matching vanilla's paused state after loading.</summary>
        public static bool WantsRunning { get; private set; }

        public static void Reset()
        {
            WantsRunning = false;
        }

        /// <summary>Player used Campaign.SetTimeSpeed: 0 = pause, 1 = play, 2 = fast forward.</summary>
        public static void FromSpeed(int speed)
        {
            if (speed == 0) WantsRunning = false;
            else if (speed == 1 || speed == 2) WantsRunning = true;
        }

        /// <summary>Player action left the clock in this mode (starting to move).</summary>
        public static void FromMode(CampaignTimeControlMode mode)
        {
            WantsRunning = IsRunning(mode);
        }

        public static bool IsRunning(CampaignTimeControlMode mode)
        {
            return mode != CampaignTimeControlMode.Stop && mode != CampaignTimeControlMode.FastForwardStop;
        }
    }
}
