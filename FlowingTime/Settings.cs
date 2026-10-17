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

        /// <summary>
        /// Parties marching toward your field battle arrive during it and enter from the battlefield edge in
        /// the direction they came from on the campaign map.
        /// </summary>
        public static readonly bool MidBattleArrivals = true;

        /// <summary>How far away (as a multiple of vanilla's joining radius) a party can be and still arrive.</summary>
        public static readonly float ArrivalRadiusMultiplier = 4f;

        /// <summary>Battle seconds per campaign hour of marching, for arrival times.</summary>
        public static readonly float BattleSecondsPerCampaignHour = 60f;

        /// <summary>No arrival earlier than this many seconds after the fighting starts.</summary>
        public static readonly float MinArrivalSeconds = 30f;

        /// <summary>Most parties that can arrive per side in one battle (nearest first).</summary>
        public static readonly int MaxArrivalsPerSide = 4;

        /// <summary>Most troops spawned at once per arrival; never more than the battle size limit allows.</summary>
        public static readonly int MaxSpawnPerWave = 60;

        /// <summary>Time keeps running when the party reaches its destination instead of auto-pausing.</summary>
        public static readonly bool NoArrivalPause = true;

        /// <summary>Time keeps running while a popup decision (peace offer, ransom, ...) is on screen.</summary>
        public static readonly bool FlowDuringPopups = false;
    }
}
