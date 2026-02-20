using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public class MediaRenameService
    {
        /// <summary>
        /// 智能解析文件时间（支持正则提取 1900-2099 年份）
        /// </summary>
        public async Task<DateTimeOffset> SmartParseDateAsync(StorageFile file)
        {
            string fileName = file.Name;

            // 匹配 1900-2099 的完整时间
            var fullTimePattern = new Regex(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s]?(?<month>0[1-9]|1[0-2])[-_.\s]?(?<day>0[1-9]|[12]\d|3[01])[-_.\sT]+(?<hour>[01]\d|2[0-3])[-_:.]?(?<minute>[0-5]\d)[-_:.]?(?<second>[0-5]\d)?");
            var match = fullTimePattern.Match(fileName);
            if (match.Success) return ParseRegexMatch(match);

            // 匹配仅日期
            var dateOnlyPattern = new Regex(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s年]?(?<month>0?[1-9]|1[0-2])[-_.\s月]?(?<day>0?[1-9]|[12]\d|3[01])[日号]?");
            match = dateOnlyPattern.Match(fileName);
            if (match.Success) return ParseRegexMatch(match).Date;

            return DateTimeOffset.MinValue;
        }

        private DateTime ParseRegexMatch(Match match)
        {
            try
            {
                int y = int.Parse(match.Groups["year"].Value);
                int m = int.Parse(match.Groups["month"].Value);
                int d = int.Parse(match.Groups["day"].Value);
                int h = 0, min = 0, s = 0;
                if (match.Groups["hour"].Success) h = int.Parse(match.Groups["hour"].Value);
                if (match.Groups["minute"].Success) min = int.Parse(match.Groups["minute"].Value);
                if (match.Groups["second"].Success) s = int.Parse(match.Groups["second"].Value);

                return new DateTime(y, m, d, h, min, s);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }
    }
}