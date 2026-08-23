using System;
using System.Collections.Generic;

namespace VoiceDuck
{
    internal sealed class AudioEndpointChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public bool IsVirtual { get; set; }

        public override string ToString()
        {
            return (IsDefault ? "默认 · " : String.Empty) + Name;
        }
    }

    internal static class MusicShareCore
    {
        private static readonly string[] LoopbackNames =
        {
            "stereo mix", "waveout mix", "mixed output", "what u hear",
            "what you hear", "立体声混音", "混合输出"
        };

        public static bool IsLoopbackCaptureName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            string normalized = value.Trim().ToLowerInvariant();
            foreach (string candidate in LoopbackNames)
                if (normalized.Contains(candidate)) return true;
            return false;
        }

        public static bool IsVirtualCableName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            string normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("vb-audio") ||
                   normalized.Contains("vb cable") ||
                   normalized.Contains("vb-cable") ||
                   normalized.Contains("virtual cable") ||
                   normalized.Contains("virtual audio") ||
                   normalized.Contains("voicemeeter") ||
                   normalized.Contains("voice meeter") ||
                   normalized.Contains("todesk virtual");
        }

        public static bool IsVbCableRenderName(string value)
        {
            string normalized = NormalizeDeviceName(value);
            return normalized.Contains("cableinput") &&
                   (normalized.Contains("vbaudio") || normalized == "cableinput");
        }

        public static bool IsVbCableCaptureName(string value)
        {
            string normalized = NormalizeDeviceName(value);
            return normalized.Contains("cableoutput") &&
                   (normalized.Contains("vbaudio") || normalized == "cableoutput");
        }

        public static float ClampGain(float value)
        {
            return Math.Max(0.0f, Math.Min(1.5f, value));
        }

        public static int FindPreferredEndpointIndex(
            IList<AudioEndpointChoice> endpoints,
            string preferredIdOrName)
        {
            if (endpoints == null || endpoints.Count == 0) return -1;
            string preferred = NormalizeDeviceName(preferredIdOrName);
            int bestIndex = -1;
            int bestScore = -1;
            for (int index = 0; index < endpoints.Count; index++)
            {
                AudioEndpointChoice endpoint = endpoints[index];
                int score = endpoint.IsDefault ? 20 : 0;
                if (!String.IsNullOrWhiteSpace(preferredIdOrName) &&
                    String.Equals(endpoint.Id, preferredIdOrName, StringComparison.OrdinalIgnoreCase))
                    score += 200;
                string candidate = NormalizeDeviceName(endpoint.Name);
                if (preferred.Length > 0 && candidate == preferred) score += 100;
                else if (preferred.Length > 0 &&
                         (candidate.Contains(preferred) || preferred.Contains(candidate))) score += 60;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }
            return bestIndex;
        }

        public static bool LooksLikeCallProcess(string processName, IEnumerable<string> configuredNames)
        {
            string normalized = AppSettings.NormalizeProcessName(processName);
            if (normalized.Length == 0) return false;
            if (configuredNames != null)
            {
                foreach (string configured in configuredNames)
                    if (normalized == AppSettings.NormalizeProcessName(configured)) return true;
            }
            return normalized == "wechat" || normalized == "weixin" || normalized == "qq" ||
                   normalized == "wxwork" || normalized == "discord" || normalized == "teams" ||
                   normalized == "ms-teams" || normalized == "zoom";
        }

        private static string NormalizeDeviceName(string value)
        {
            return (value ?? String.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace('（', '(')
                .Replace('）', ')')
                .Replace(" ", String.Empty)
                .Replace("-", String.Empty)
                .Replace("_", String.Empty);
        }
    }
}
