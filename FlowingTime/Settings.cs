namespace FlowingTime
{
    /// <summary>Feature switches. Constants for now; can move to an MCM menu later.</summary>
    internal static class Settings
    {
        /// <summary>Time keeps running in town, castle, village and other game menus.</summary>
        public static readonly bool FlowInMenus = true;

        /// <summary>
        /// Time keeps running in encounter menus (attack / surrender / leave). The player's own battle is
        /// never auto-resolved by the game while it waits for a decision, but other parties keep moving
        /// and can join it.
        /// </summary>
        public static readonly bool FlowDuringEncounters = true;

        /// <summary>Time keeps running when the party reaches its destination instead of auto-pausing.</summary>
        public static readonly bool NoArrivalPause = true;

        /// <summary>Time keeps running while a popup decision (peace offer, ransom, ...) is on screen.</summary>
        public static readonly bool FlowDuringPopups = false;
    }
}
