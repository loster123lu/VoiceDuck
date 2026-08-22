using System;

namespace VoiceDuck
{
    internal sealed class VoiceGate
    {
        private int _aboveThresholdMs;
        private int _holdRemainingMs;
        private bool _open;

        public bool IsOpen { get { return _open; } }

        public bool Update(float peak, float thresholdDb, int triggerDelayMs, int holdMs, int elapsedMs)
        {
            float peakDb = LinearToDb(peak);
            bool above = peakDb >= thresholdDb;

            if (above)
            {
                _aboveThresholdMs += elapsedMs;
                if (_aboveThresholdMs >= triggerDelayMs)
                {
                    _open = true;
                    _holdRemainingMs = holdMs;
                }
            }
            else
            {
                _aboveThresholdMs = 0;
                if (_open)
                {
                    _holdRemainingMs -= elapsedMs;
                    if (_holdRemainingMs <= 0)
                    {
                        _holdRemainingMs = 0;
                        _open = false;
                    }
                }
            }

            return _open;
        }

        public void Reset()
        {
            _aboveThresholdMs = 0;
            _holdRemainingMs = 0;
            _open = false;
        }

        public static float LinearToDb(float peak)
        {
            return peak <= 0.000001f ? -96.0f : (float)(20.0 * Math.Log10(peak));
        }
    }
}
