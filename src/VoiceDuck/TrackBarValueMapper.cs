using System;

namespace VoiceDuck
{
    internal static class TrackBarValueMapper
    {
        public static int FromPosition(
            int minimum,
            int maximum,
            int smallChange,
            int channelStart,
            int channelEnd,
            int mousePosition,
            bool reversed)
        {
            if (maximum < minimum) throw new ArgumentOutOfRangeException("maximum");
            if (maximum == minimum || channelEnd <= channelStart) return minimum;

            double ratio = (mousePosition - channelStart) / (double)(channelEnd - channelStart);
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            if (reversed) ratio = 1.0 - ratio;

            double rawValue = minimum + ratio * (maximum - minimum);
            int step = Math.Max(1, smallChange);
            int steps = (int)Math.Round(
                (rawValue - minimum) / step,
                MidpointRounding.AwayFromZero);
            int value = minimum + steps * step;
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
