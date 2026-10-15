using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace STNG;

internal static class ApiLoggingConfiguration
{
    internal const string FilePathKey = "STNG:Logging:FilePath";
    internal const int RetainedFileCountLimit = 31;

    internal static string ResolveFilePath(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredPath = configuration[FilePathKey]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                $"The '{FilePathKey}' configuration value is required. " +
                "For IIS, configure the STNG__Logging__FilePath application-pool environment variable.");
        }

        if (Environments.Production.Equals(environmentName, StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                $"The '{FilePathKey}' configuration value must be an absolute path in Production.");
        }

        return configuredPath;
    }

    internal static LoggerConfiguration WriteToApiFile(
        this LoggerConfiguration loggerConfiguration,
        string logFilePath)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);

        return loggerConfiguration.WriteTo.Async(sink => sink.File(
            logFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: RetainedFileCountLimit));
    }
}
