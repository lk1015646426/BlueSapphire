using BlueSapphire.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services;

public sealed class CleanerSystemCleanupService
{
    public ProcessStartInfo BuildDeliveryOptimizationStartInfo()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Import-Module DeliveryOptimization -ErrorAction Stop; Delete-DeliveryOptimizationCache -Force -ErrorAction Stop");
        return startInfo;
    }

    public async Task<CleanerSystemCleanupResult> ExecuteAsync(
        CleanerSystemActionKind action,
        string measurementPath,
        CancellationToken cancellationToken)
    {
        if (action != CleanerSystemActionKind.DeliveryOptimization)
        {
            return new CleanerSystemCleanupResult
            {
                Message = "未配置对应的 Windows 专用清理动作。"
            };
        }

        long beforeBytes = MeasurePath(measurementPath, cancellationToken);
        ProcessStartInfo startInfo = BuildDeliveryOptimizationStartInfo();

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Windows PowerShell。" );
            await process.WaitForExitAsync(cancellationToken);
            string error = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return new CleanerSystemCleanupResult
                {
                    Message = string.IsNullOrWhiteSpace(error)
                        ? $"Windows 专用清理返回错误代码 {process.ExitCode}。"
                        : error.Trim()
                };
            }

            long afterBytes = MeasurePath(measurementPath, cancellationToken);
            long releasedBytes = Math.Max(0, beforeBytes - afterBytes);
            return new CleanerSystemCleanupResult
            {
                Succeeded = true,
                ReleasedBytes = releasedBytes,
                Message = releasedBytes > 0
                    ? $"Windows 已清理传递优化缓存，实际释放 {CleanerSizeFormatter.Format(releasedBytes)}。"
                    : "Windows 已完成传递优化缓存清理，未检测到可核对的空间变化。"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CleanerSystemCleanupResult
            {
                Message = ex.Message
            };
        }
    }

    private static long MeasurePath(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        if (!Directory.Exists(path) || CleanerPathSafety.IsReparsePoint(path))
        {
            return 0;
        }

        long total = 0;
        Stack<string> pending = new();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            foreach (string file in CleanerPathSafety.SafeEnumerateFiles(current))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            foreach (string directory in CleanerPathSafety.SafeEnumerateDirectories(current))
            {
                if (!CleanerPathSafety.IsReparsePoint(directory))
                {
                    pending.Push(directory);
                }
            }
        }

        return total;
    }
}
