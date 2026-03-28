using System;
using System.Globalization;

namespace BlueSapphire.Models
{
    public sealed record AudioTrimRequest(TimeSpan StartTime, TimeSpan EndTime)
    {
        public TimeSpan Duration => EndTime - StartTime;

        public string RangeText => $"{FormatTime(StartTime)} - {FormatTime(EndTime)}";

        public static bool TryCreate(
            double startSeconds,
            double endSeconds,
            out AudioTrimRequest? request,
            out string validationMessage)
        {
            request = null;

            if (double.IsNaN(startSeconds) || double.IsInfinity(startSeconds) ||
                double.IsNaN(endSeconds) || double.IsInfinity(endSeconds))
            {
                validationMessage = "请输入有效的裁剪时间。";
                return false;
            }

            if (startSeconds < 0 || endSeconds < 0)
            {
                validationMessage = "裁剪时间不能小于 0。";
                return false;
            }

            if (endSeconds <= startSeconds)
            {
                validationMessage = "结束时间必须大于开始时间。";
                return false;
            }

            request = new AudioTrimRequest(
                TimeSpan.FromSeconds(startSeconds),
                TimeSpan.FromSeconds(endSeconds));
            validationMessage = string.Empty;
            return true;
        }

        public static bool TryCreate(
            string? startText,
            string? endText,
            out AudioTrimRequest? request,
            out string validationMessage)
        {
            request = null;

            if (!TryParseTimecode(startText, out TimeSpan startTime))
            {
                validationMessage = "开始时间格式无效，请输入秒数或 mm:ss / hh:mm:ss。";
                return false;
            }

            if (!TryParseTimecode(endText, out TimeSpan endTime))
            {
                validationMessage = "结束时间格式无效，请输入秒数或 mm:ss / hh:mm:ss。";
                return false;
            }

            return TryCreate(
                startTime.TotalSeconds,
                endTime.TotalSeconds,
                out request,
                out validationMessage);
        }

        public static bool TryParseTimecode(string? text, out TimeSpan value)
        {
            value = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Contains(':'))
            {
                string[] parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length is < 2 or > 3)
                {
                    return false;
                }

                if (!int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes) || minutes < 0)
                {
                    return false;
                }

                if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0 || seconds >= 60)
                {
                    return false;
                }

                int hours = 0;
                if (parts.Length == 3)
                {
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) || hours < 0)
                    {
                        return false;
                    }
                }

                value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                return true;
            }

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double totalSeconds) || totalSeconds < 0)
            {
                return false;
            }

            value = TimeSpan.FromSeconds(totalSeconds);
            return true;
        }

        private static string FormatTime(TimeSpan value)
        {
            return value.TotalHours >= 1
                ? value.ToString(@"hh\:mm\:ss")
                : value.ToString(@"mm\:ss");
        }
    }
}
