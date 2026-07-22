using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace BlueSapphire.Services;

internal static class CleanerKnownPathResolver
{
    private static readonly IReadOnlyDictionary<string, Func<string?>> Resolvers =
        new Dictionary<string, Func<string?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["%STEAM_INSTALL%"] = ResolveSteamInstall,
            ["%UBISOFT_INSTALL%"] = ResolveUbisoftInstall
        };

    public static string Expand(string rawPath)
    {
        string expanded = Environment.ExpandEnvironmentVariables(rawPath ?? string.Empty);
        foreach ((string token, Func<string?> resolver) in Resolvers)
        {
            if (!expanded.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? resolved = resolver();
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return string.Empty;
            }

            expanded = expanded.Replace(token, resolved.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static string? ResolveSteamInstall()
    {
        return ReadRegistryPath(Registry.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath") ??
               ReadRegistryPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") ??
               ExistingDefault(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }

    private static string? ResolveUbisoftInstall()
    {
        return ReadRegistryPath(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher", "InstallDir") ??
               ReadRegistryPath(Registry.LocalMachine, @"SOFTWARE\Ubisoft\Launcher", "InstallDir") ??
               ExistingDefault(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ubisoft", "Ubisoft Game Launcher");
    }

    private static string? ReadRegistryPath(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(subKey);
            string? value = key?.GetValue(valueName) as string;
            return !string.IsNullOrWhiteSpace(value) && Directory.Exists(value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExistingDefault(params string[] segments)
    {
        string path = Path.Combine(segments);
        return Directory.Exists(path) ? path : null;
    }
}
