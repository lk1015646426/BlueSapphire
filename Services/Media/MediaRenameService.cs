using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using Windows.Storage;

namespace BlueSapphire.Services.Media
{
    public class MediaRenameService
    {
        private static readonly Regex FullTimeSeparatedPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s年](?<month>0?[1-9]|1[0-2])[-_.\s月](?<day>0?[1-9]|[12]\d|3[01])[日号]?[-_.\sT]+(?<hour>[01]\d|2[0-3])[-_:.]?(?<minute>[0-5]\d)(?:[-_:.]?(?<second>[0-5]\d))?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex FullTimeCompactPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12]\d|3[01])[-_.\sT]?(?<hour>[01]\d|2[0-3])(?<minute>[0-5]\d)(?<second>[0-5]\d)?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex DateOnlySeparatedPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})[-_.\s年](?<month>0?[1-9]|1[0-2])[-_.\s月](?<day>0?[1-9]|[12]\d|3[01])[日号]?(?!\d)", RegexOptions.Compiled);
        private static readonly Regex DateOnlyCompactPattern = new(@"(?<!\d)(?<year>(?:19|20)\d{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.Compiled);

        public bool HasUsableTimestamp(DateTimeOffset value)
        {
            return value != DateTimeOffset.MinValue && value.Year >= 1900;
        }

        public async Task<DateTimeOffset> ResolveBestTimestampAsync(StorageFile file)
        {
            var metadataTimestamp = await TryGetMetadataTimestampAsync(file);
            if (HasUsableTimestamp(metadataTimestamp))
            {
                return metadataTimestamp;
            }

            var parsedTimestamp = ParseTimestampFromFileName(file.Name);
            if (HasUsableTimestamp(parsedTimestamp))
            {
                return parsedTimestamp;
            }

            return file.DateCreated;
        }

        public Task<DateTimeOffset> SmartParseDateAsync(StorageFile file)
        {
            return Task.FromResult(ParseTimestampFromFileName(file.Name));
        }

        public DateTimeOffset ParseTimestampFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return DateTimeOffset.MinValue;
            }

            foreach (var pattern in new[] { FullTimeSeparatedPattern, FullTimeCompactPattern })
            {
                var match = pattern.Match(fileName);
                if (match.Success)
                {
                    return ParseRegexMatch(match);
                }
            }

            foreach (var pattern in new[] { DateOnlySeparatedPattern, DateOnlyCompactPattern })
            {
                var match = pattern.Match(fileName);
                if (match.Success)
                {
                    return ParseRegexMatch(match).Date;
                }
            }

            return DateTimeOffset.MinValue;
        }

        private static async Task<DateTimeOffset> TryGetMetadataTimestampAsync(StorageFile file)
        {
            if (!MediaFileCatalog.IsImage(file.Name))
            {
                return DateTimeOffset.MinValue;
            }

            try
            {
                var imageProperties = await file.Properties.GetImagePropertiesAsync();
                return imageProperties.DateTaken;
            }
            catch
            {
                return DateTimeOffset.MinValue;
            }
        }

        private static DateTime ParseRegexMatch(Match match)
        {
            try
            {
                int year = int.Parse(match.Groups["year"].Value);
                int month = int.Parse(match.Groups["month"].Value);
                int day = int.Parse(match.Groups["day"].Value);
                int hour = match.Groups["hour"].Success ? int.Parse(match.Groups["hour"].Value) : 0;
                int minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value) : 0;
                int second = match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value) : 0;

                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
        }
    }
}
