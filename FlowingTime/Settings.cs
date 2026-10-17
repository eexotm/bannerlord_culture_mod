namespace FlowingTime
{
    /// <summary>Feature switches. Fixed values for now; can move to an MCM menu later.</summary>
    internal static class Settings
    {
        /// <summary>Time keeps running in town, castle and village menus.</summary>
        public static readonly bool FlowInMenus = true;

        /// <summary>
        /// Time keeps running at the encounter menu of the player's own field battle (Attack / Send troops /
        /// Try to get away / Surrender). The game never auto-resolves that battle while it waits for the
        /// player, so the enemy waits, and parties that arrive in the meantime join either side.
        /// </summary>
        public static readonly bool FlowInOwnBattles = true;

        /// <summary>In-game hours between checks for nearby parties joining the player's battle.</summary>
        public static readonly float JoinCheckIntervalHours = 0.25f;

        /// <summary>Time keeps running when the party reaches its destination instead of auto-pausing.</summary>
        public static readonly bool NoArrivalPause = true;

        /// <summary>Time keeps running while a popup decision (peace offer, ransom, ...) is on screen.</summary>
        public static readonly bool FlowDuringPopups = false;
    }
}
