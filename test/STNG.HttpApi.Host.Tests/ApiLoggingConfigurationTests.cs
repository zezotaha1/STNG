using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog;
using Shouldly;
using Xunit;

namespace STNG;

public class ApiLoggingConfigurationTests
{
    [Fact]
    public void ResolveFilePath_ShouldReturnConfiguredAbsolutePathInProduction()
    {
        var expectedPath = Path.GetFullPath(Path.Combine("Logs", "stng-api-.log"));
        var configuration = CreateConfiguration(expectedPath);

        var actualPath = ApiLoggingConfiguration.ResolveFilePath(configuration, "Production");

        actualPath.ShouldBe(expectedPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveFilePath_ShouldRejectMissingPath(string? configuredPath)
    {
        var configuration = CreateConfiguration(configuredPath);

        var exception = Should.Throw<InvalidOperationException>(() =>
            ApiLoggingConfiguration.ResolveFilePath(configuration, "Production"));

        exception.Message.ShouldContain(ApiLoggingConfiguration.FilePathKey);
    }

    [Fact]
    public void ResolveFilePath_ShouldRejectRelativePathInProduction()
    {
        var configuration = CreateConfiguration(Path.Combine("Logs", "stng-api-.log"));

        var exception = Should.Throw<InvalidOperationException>(() =>
            ApiLoggingConfiguration.ResolveFilePath(configuration, "Production"));

        exception.Message.ShouldContain("absolute path");
    }

    [Fact]
    public void ResolveFilePath_ShouldAllowRelativePathInDevelopment()
    {
        var expectedPath = Path.Combine("Logs", "stng-api-.log");
        var configuration = CreateConfiguration(expectedPath);

        var actualPath = ApiLoggingConfiguration.ResolveFilePath(configuration, "Development");

        actualPath.ShouldBe(expectedPath);
    }

    [Fact]
    public void EnvironmentVariable_ShouldBindToLoggingFilePath()
    {
        const string prefix = "STNG_LOGGING_TEST_";
        const string variableName = prefix + "STNG__Logging__FilePath";
        var expectedPath = Path.GetFullPath(Path.Combine("Logs", "stng-api-.log"));
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, expectedPath);
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix)
                .Build();

            var actualPath = ApiLoggingConfiguration.ResolveFilePath(configuration, "Production");

            actualPath.ShouldBe(expectedPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }

    [Fact]
    public void WriteToApiFile_ShouldInitializeDailyRollingFileSink()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"stng-api-logging-{Guid.NewGuid():N}");
        var logFilePath = Path.Combine(directory, "stng-api-.log");
        var message = $"logging-test-{Guid.NewGuid():N}";

        try
        {
            using (var logger = new LoggerConfiguration()
                       .WriteToApiFile(logFilePath)
                       .CreateLogger())
            {
                logger.Information("{Message}", message);
            }

            var logFiles = Directory.GetFiles(directory, "stng-api-*.log");
            logFiles.Length.ShouldBe(1);
            File.ReadAllText(logFiles[0]).ShouldContain(message);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static IConfiguration CreateConfiguration(string? path)
    {
        var values = new Dictionary<string, string?>
        {
            [ApiLoggingConfiguration.FilePathKey] = path
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
