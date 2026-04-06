using BlueSapphire.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerAutomationScheduleService
    {
        public const string DefaultTaskName = "BlueSapphire Cleaner Automation";

        private readonly Func<string?> _executablePathProvider;
        private readonly Func<DateTimeOffset> _nowProvider;
        private readonly Func<string, Task<CleanerTaskCommandResult>> _commandRunner;

        public CleanerAutomationScheduleService(
            Func<string?>? executablePathProvider = null,
            Func<DateTimeOffset>? nowProvider = null,
            Func<string, Task<CleanerTaskCommandResult>>? commandRunner = null)
        {
            _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
            _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
            _commandRunner = commandRunner ?? RunCommandAsync;
        }

        public async Task<CleanerAutomationScheduleState> SyncAsync(CleanerPreferenceState preferences)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new CleanerAutomationScheduleState
                {
                    IsSupported = false,
                    IsConfigured = false,
                    IsRegistered = false,
                    TaskName = DefaultTaskName,
                    LastSynchronizedAt = preferences.LastAutomationScheduleSyncAt,
                    ErrorMessage = "当前平台不支持 Windows 计划任务。"
                };
            }

            bool shouldRegister = preferences.ReminderEnabled || preferences.AutoLowRiskCleanupEnabled;
            DateTimeOffset syncTime = _nowProvider();

            if (!shouldRegister)
            {
                CleanerTaskCommandResult deleteResult = await _commandRunner($"/Delete /F /TN \"{DefaultTaskName}\"");
                bool removed = deleteResult.ExitCode == 0 || ContainsTaskNotFound(deleteResult);
                return new CleanerAutomationScheduleState
                {
                    IsSupported = true,
                    IsConfigured = false,
                    IsRegistered = false,
                    TaskName = DefaultTaskName,
                    LastSynchronizedAt = syncTime,
                    ErrorMessage = removed ? string.Empty : GetError(deleteResult)
                };
            }

            string? executablePath = _executablePathProvider();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new CleanerAutomationScheduleState
                {
                    IsSupported = true,
                    IsConfigured = true,
                    IsRegistered = false,
                    TaskName = DefaultTaskName,
                    LastSynchronizedAt = syncTime,
                    ErrorMessage = "无法确定当前应用可执行文件路径，未能注册计划任务。"
                };
            }

            string action = $"\\\"{executablePath}\\\" --tool=CleanerAssistant";
            string startTime = syncTime.LocalDateTime.AddMinutes(2).ToString("HH:mm", CultureInfo.InvariantCulture);
            int intervalDays = CleanerAutomationService.NormalizeInterval(preferences.ReminderIntervalDays);

            CleanerTaskCommandResult createResult = await _commandRunner(
                $"/Create /F /SC DAILY /MO {intervalDays} /TN \"{DefaultTaskName}\" /TR \"{action}\" /ST {startTime} /RL LIMITED");

            return new CleanerAutomationScheduleState
            {
                IsSupported = true,
                IsConfigured = true,
                IsRegistered = createResult.ExitCode == 0,
                TaskName = DefaultTaskName,
                LastSynchronizedAt = syncTime,
                ErrorMessage = createResult.ExitCode == 0 ? string.Empty : GetError(createResult)
            };
        }

        public async Task<CleanerAutomationScheduleState> GetStateAsync(CleanerPreferenceState preferences)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new CleanerAutomationScheduleState
                {
                    IsSupported = false,
                    IsConfigured = false,
                    IsRegistered = false,
                    TaskName = DefaultTaskName,
                    LastSynchronizedAt = preferences.LastAutomationScheduleSyncAt,
                    ErrorMessage = "当前平台不支持 Windows 计划任务。"
                };
            }

            bool shouldRegister = preferences.ReminderEnabled || preferences.AutoLowRiskCleanupEnabled;
            CleanerTaskCommandResult queryResult = await _commandRunner($"/Query /TN \"{DefaultTaskName}\"");
            bool registered = queryResult.ExitCode == 0;

            return new CleanerAutomationScheduleState
            {
                IsSupported = true,
                IsConfigured = shouldRegister,
                IsRegistered = registered,
                TaskName = string.IsNullOrWhiteSpace(preferences.LastAutomationScheduleTaskName)
                    ? DefaultTaskName
                    : preferences.LastAutomationScheduleTaskName,
                LastSynchronizedAt = preferences.LastAutomationScheduleSyncAt,
                ErrorMessage = shouldRegister && !registered
                    ? string.IsNullOrWhiteSpace(preferences.LastAutomationScheduleError)
                        ? "计划任务尚未注册成功。"
                        : preferences.LastAutomationScheduleError
                    : string.Empty
            };
        }

        private static bool ContainsTaskNotFound(CleanerTaskCommandResult result)
        {
            string message = $"{result.StandardOutput}\n{result.StandardError}";
            return message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("找不到", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetError(CleanerTaskCommandResult result)
        {
            string error = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;

            return string.IsNullOrWhiteSpace(error)
                ? $"schtasks.exe 返回代码 {result.ExitCode}"
                : error.Trim();
        }

        private static async Task<CleanerTaskCommandResult> RunCommandAsync(string arguments)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = startInfo };
            process.Start();
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new CleanerTaskCommandResult(process.ExitCode, stdout, stderr);
        }
    }

    public readonly record struct CleanerTaskCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
