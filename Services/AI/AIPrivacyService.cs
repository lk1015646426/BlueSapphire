using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BlueSapphire.Services.AI
{
    public sealed partial class AIPrivacyService
    {
        public string RedactForRemoteModel(string? value)
        {
            string text = value ?? string.Empty;
            text = UserProfilePathRegex().Replace(text, @"C:\Users\<用户>");
            text = EmailRegex().Replace(text, "<邮箱>");
            text = BearerRegex().Replace(text, "$1<令牌>");
            text = SecretAssignmentRegex().Replace(text, "$1=<敏感信息>");
            text = QuerySecretRegex().Replace(text, "$1=<敏感信息>");
            return text;
        }

        public string DescribePathWithoutIdentity(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "未指定路径";
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                string name = Path.GetFileName(fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                return string.IsNullOrWhiteSpace(name) ? root : $"{root}…\\{name}";
            }
            catch
            {
                return "<路径>";
            }
        }

        [GeneratedRegex(@"(?i)\b[A-Z]:\\Users\\[^\\\r\n]+")]
        private static partial Regex UserProfilePathRegex();

        [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"(?i)\b(Authorization\s*:\s*Bearer\s+)[A-Za-z0-9\-._~+/]+=*")]
        private static partial Regex BearerRegex();

        [GeneratedRegex(@"(?i)\b(api[_-]?key|token|secret|password)\s*[:=]\s*[^\s,;]+")]
        private static partial Regex SecretAssignmentRegex();

        [GeneratedRegex(@"(?i)([?&](?:key|token|secret|api_key))=[^&#\s]+")]
        private static partial Regex QuerySecretRegex();
    }
}
