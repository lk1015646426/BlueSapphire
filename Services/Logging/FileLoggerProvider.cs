using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services.Logging
{
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly Channel<string> _logChannel;
        private readonly string _logFilePath;
        private bool _isDisposed;

        public FileLoggerProvider()
        {
            _logChannel = Channel.CreateUnbounded<string>();
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire", "Logs");
            Directory.CreateDirectory(folderPath);
            _logFilePath = Path.Combine(folderPath, "app.log");
            
            _ = Task.Run(ProcessLogsAsync);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, this);
        }

        internal void Log(string message)
        {
            if (!_isDisposed)
            {
                _logChannel.Writer.TryWrite(message);
            }
        }

        private async Task ProcessLogsAsync()
        {
            try
            {
                await foreach (var logMsg in _logChannel.Reader.ReadAllAsync())
                {
                    await File.AppendAllTextAsync(_logFilePath, logMsg + Environment.NewLine);
                }
            }
            catch
            {
                // Ignore I/O errors during logging
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            _logChannel.Writer.TryComplete();
        }
    }
}
