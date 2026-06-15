using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlueSapphire.Services.Logging
{
    public static class FileLoggerExtensions
    {
        public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder)
        {
            builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
            return builder;
        }
    }
}
