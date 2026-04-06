using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueSapphire.Services
{
    public sealed class CleanerLaunchActionService
    {
        private readonly Func<string> _argumentsProvider;
        private readonly HashSet<string> _consumedTokens = new(StringComparer.OrdinalIgnoreCase);

        public CleanerLaunchActionService(Func<string>? argumentsProvider = null)
        {
            _argumentsProvider = argumentsProvider ?? (() => App.LaunchArguments);
        }

        public string? ConsumeRetryBatchId()
        {
            return ConsumeValue("--cleaner-retry-batch=");
        }

        private string? ConsumeValue(string prefix)
        {
            foreach (string token in TokenizeArguments(_argumentsProvider()))
            {
                if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_consumedTokens.Add(token))
                {
                    return null;
                }

                string value = token[prefix.Length..].Trim().Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        public static IReadOnlyList<string> TokenizeArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return Array.Empty<string>();
            }

            return arguments
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }
}
