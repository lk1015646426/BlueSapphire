using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Services.Cleaner;

public sealed class CleanerApplicationDiscoveryService
{
    private readonly Lazy<IReadOnlyList<InstalledApplication>> _installedApplications;

    public CleanerApplicationDiscoveryService()
    {
        _installedApplications = new Lazy<IReadOnlyList<InstalledApplication>>(LoadInstalledApplications, true);
    }

    public string GetInstalledContext(string ownerApp)
    {
        string[] tokens = BuildSearchTokens(ownerApp);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        InstalledApplication? match = _installedApplications.Value
            .Where(app => tokens.Any(token => app.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(app => tokens.Count(token => app.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(app => app.DisplayName.Length)
            .FirstOrDefault();
        return match == null ? string.Empty : FormatContext(match.DisplayName, match.Version, match.InstallLocation);
    }

    public static string FormatContext(string displayName, string? version, string? installLocation)
    {
        List<string> parts = new() { displayName };
        if (!string.IsNullOrWhiteSpace(version))
        {
            parts.Add($"版本 {version}");
        }
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            parts.Add($"安装于 {installLocation}");
        }
        return string.Join(" · ", parts);
    }

    private static string[] BuildSearchTokens(string ownerApp)
    {
        if (string.IsNullOrWhiteSpace(ownerApp) ||
            ownerApp.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ||
            ownerApp.Contains("Chromium Browser", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        return ownerApp
            .Split(new[] { '/', '·' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Replace(" IDE", string.Empty, StringComparison.OrdinalIgnoreCase).Trim())
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<InstalledApplication> LoadInstalledApplications()
    {
        List<InstalledApplication> result = new();
        foreach (RegistryKey? root in OpenUninstallRoots())
        {
            using (root)
            {
                if (root == null) continue;
                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? appKey = root.OpenSubKey(subKeyName);
                        string displayName = appKey?.GetValue("DisplayName") as string ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        string version = appKey?.GetValue("DisplayVersion") as string ?? string.Empty;
                        string installLocation = appKey?.GetValue("InstallLocation") as string ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(installLocation) && !Directory.Exists(installLocation))
                        {
                            installLocation = string.Empty;
                        }
                        result.Add(new InstalledApplication(displayName.Trim(), version.Trim(), installLocation.Trim()));
                    }
                    catch (Exception)
                    {
                        // 个别卸载注册表键权限不足或损坏时跳过，属正常降级，不影响其余应用识别。
                    }
                }
            }
        }

        return result
            .GroupBy(app => $"{app.DisplayName}|{app.Version}|{app.InstallLocation}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<RegistryKey?> OpenUninstallRoots()
    {
        yield return TryOpen(() => Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
        yield return TryOpen(() => Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"));
        yield return TryOpen(() => Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
    }

    private static RegistryKey? TryOpen(Func<RegistryKey?> factory)
    {
        try { return factory(); } catch { return null; }
    }

    private sealed record InstalledApplication(string DisplayName, string Version, string InstallLocation);
}
