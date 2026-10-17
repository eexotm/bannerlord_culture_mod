namespace FlowingTime
{
    /// <summary>Feature switches. Fixed values for now; can move to an MCM menu later.</summary>
    internal static class Settings
    {
        /// <summary>Time keeps running in town, castle and village menus.</summary>
        public static readonly bool FlowInMenus = true;

        /// <summary>Time keeps running when the party reaches its destination instead of auto-pausing.</summary>
        public static readonly bool NoArrivalPause = true;

        /// <summary>Time keeps running while a popup decision (peace offer, ransom, ...) is on screen.</summary>
        public static readonly bool FlowDuringPopups = false;
    }
}
