using System;
using System.Collections.Generic;

namespace SmartPaste
{
    public enum ScheduleState
    {
        Disabled,       // Scheduler is off
        DayOff,         // Today is not a work day
        BeforeWork,     // Too early
        Working,        // Active work period
        Lunch,          // Lunch break
        AfterWork       // Day is done
    }

    /// <summary>
    /// Pure state machine — no timers, no threads.
    /// The dashboard timer polls GetCurrentState() every tick.
    ///
    /// Energy curve simulates realistic human productivity:
    ///   09:00-10:30  Morning burst    → 0.7x delay (fast, energetic)
    ///   10:30-12:00  Normal           → 1.0x delay
    ///   12:00-13:30  Lunch            → no activity
    ///   13:30-15:00  Post-lunch dip   → 1.4x delay (slow, drowsy)
    ///   15:00-16:30  Afternoon        → 1.0x delay
    ///   16:30-17:00  Wind-down        → 1.3x delay (wrapping up)
    /// </summary>
    public static class WorkScheduler
    {
        public static ScheduleState GetCurrentState(List<ScheduleDay> week, bool enabled)
        {
            if (!enabled || week == null || week.Count < 7)
                return ScheduleState.Disabled;

            var now = DateTime.Now;
            int dayIndex = (int)now.DayOfWeek;
            var day = week[dayIndex];

            if (!day.Enabled)
                return ScheduleState.DayOff;

            var time = now.TimeOfDay;

            if (!TryParse(day.Start, out var start) ||
                !TryParse(day.End, out var end) ||
                !TryParse(day.LunchStart, out var lunchStart) ||
                !TryParse(day.LunchEnd, out var lunchEnd))
                return ScheduleState.Disabled;

            if (time < start)   return ScheduleState.BeforeWork;
            if (time >= end)    return ScheduleState.AfterWork;
            if (time >= lunchStart && time < lunchEnd) return ScheduleState.Lunch;

            return ScheduleState.Working;
        }

        /// <summary>
        /// Returns a delay multiplier based on time of day.
        /// Lower = faster typing (more energy).
        /// </summary>
        public static double GetEnergyMultiplier(List<ScheduleDay> week)
        {
            if (week == null || week.Count < 7) return 1.0;

            var now = DateTime.Now;
            var day = week[(int)now.DayOfWeek];
            if (!day.Enabled) return 1.0;

            if (!TryParse(day.Start, out var start) ||
                !TryParse(day.End, out var end) ||
                !TryParse(day.LunchEnd, out var lunchEnd))
                return 1.0;

            var time = now.TimeOfDay;
            double hoursFromStart = (time - start).TotalHours;
            double hoursToEnd = (end - time).TotalHours;
            double hoursAfterLunch = (time - lunchEnd).TotalHours;

            // Morning burst: first 1.5 hours
            if (hoursFromStart < 1.5)
                return 0.7;

            // Post-lunch dip: first 1.5 hours after lunch
            if (hoursAfterLunch >= 0 && hoursAfterLunch < 1.5)
                return 1.4;

            // Wind-down: last hour
            if (hoursToEnd < 1.0)
                return 1.3;

            // Normal
            return 1.0;
        }

        /// <summary>
        /// Returns a human-readable status string for the current state.
        /// </summary>
        public static string GetStatusText(ScheduleState state, List<ScheduleDay> week)
        {
            switch (state)
            {
                case ScheduleState.Working:
                    double energy = GetEnergyMultiplier(week);
                    string phase = energy switch
                    {
                        < 0.8  => "Morning burst",
                        > 1.3  => "Post-lunch dip",
                        > 1.2  => "Winding down",
                        _      => "Normal pace"
                    };
                    return $"Working — {phase}";

                case ScheduleState.Lunch:
                    return "Lunch break";

                case ScheduleState.BeforeWork:
                    var day = week[(int)DateTime.Now.DayOfWeek];
                    return $"Starts at {day.Start}";

                case ScheduleState.AfterWork:
                    return "Day complete";

                case ScheduleState.DayOff:
                    return "Day off";

                default:
                    return "Scheduler off";
            }
        }

        private static bool TryParse(string timeStr, out TimeSpan result)
        {
            return TimeSpan.TryParse(timeStr, out result);
        }
    }
}
