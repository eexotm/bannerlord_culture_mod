using System;
using System.Diagnostics;
using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FlowingTime
{
    /// <summary>
    /// Optional readout of how long this mod's own code takes per frame. Off unless a file named
    /// "perf.txt" exists in Modules/FlowingTime. Every 10 seconds it shows the average and worst cost per
    /// map frame, and the current game speed, so lag from the mod can be told apart from lag from the
    /// campaign simulation running.
    /// </summary>
    internal static class PerfStats
    {
        public static readonly bool Enabled = DetectEnabled();

        private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;
        private static long _frameTicks;
        private static long _totalTicks;
        private static long _worstFrameTicks;
        private static int _frames;
        private static long _lastReport = Stopwatch.GetTimestamp();

        /// <summary>Returns a start timestamp, or 0 when disabled (then End does nothing).</summary>
        public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

        public static void End(long start)
        {
            if (start != 0) _frameTicks += Stopwatch.GetTimestamp() - start;
        }

        /// <summary>Called once per map frame, at its start: closes the previous frame and reports every 10 s.</summary>
        public static void FrameBoundary()
        {
            if (!Enabled) return;
            _totalTicks += _frameTicks;
            if (_frameTicks > _worstFrameTicks) _worstFrameTicks = _frameTicks;
            _frameTicks = 0;
            _frames++;

            long now = Stopwatch.GetTimestamp();
            if (now - _lastReport < Stopwatch.Frequency * 10) return;

            double avg = _frames > 0 ? _totalTicks / TicksPerMicrosecond / _frames : 0;
            double worst = _worstFrameTicks / TicksPerMicrosecond;
            double fps = _frames / ((now - _lastReport) / (double)Stopwatch.Frequency);
            string speed = Campaign.Current != null ? Campaign.Current.TimeControlMode.ToString() : "-";
            InformationManager.DisplayMessage(new InformationMessage(
                $"[Flowing Time] mod code {avg:0.0} us/frame (worst {worst:0} us), map {fps:0} fps, speed {speed}", Colors.Yellow));

            _lastReport = now;
            _totalTicks = 0;
            _worstFrameTicks = 0;
            _frames = 0;
        }

        private static bool DetectEnabled()
        {
            try
            {
                // .../Modules/FlowingTime/bin/Win64_Shipping_Client/FlowingTime.dll -> .../Modules/FlowingTime
                string dll = typeof(PerfStats).Assembly.Location;
                string moduleDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dll)));
                return moduleDir != null && File.Exists(Path.Combine(moduleDir, "perf.txt"));
            }
            catch
            {
                return false;
            }
        }
    }
}
