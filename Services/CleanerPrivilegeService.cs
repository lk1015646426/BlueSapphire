using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class CleanerPrivilegeService
    {
        public bool IsElevated
        {
            get
            {
                try
                {
                    using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                    WindowsPrincipal principal = new(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<bool> RestartElevatedAsync(string? toolId = null, IEnumerable<string>? extraArguments = null)
        {
            if (IsElevated)
            {
                return true;
            }

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            List<string> forwardedArgs = Environment.GetCommandLineArgs()
                .Skip(1)
                .Where(arg =>
                !arg.StartsWith("--tool=", StringComparison.OrdinalIgnoreCase) &&
                !arg.StartsWith("--cleaner-retry-batch=", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!string.IsNullOrWhiteSpace(toolId))
            {
                forwardedArgs.Add($"--tool={toolId}");
            }
            if (extraArguments != null)
            {
                forwardedArgs.AddRange(extraArguments.Where(arg => !string.IsNullOrWhiteSpace(arg)));
            }

            return await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo startInfo = new()
                    {
                        FileName = executablePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    foreach (string argument in forwardedArgs)
                    {
                        startInfo.ArgumentList.Add(argument);
                    }

                    using Process? process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    if (App.CurrentWindow?.DispatcherQueue != null)
                    {
                        App.CurrentWindow.DispatcherQueue.TryEnqueue(() => Application.Current.Exit());
                    }
                    else
                    {
                        Application.Current.Exit();
                    }

                    return true;
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
