using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace STNG;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var logFilePath = ApiLoggingConfiguration.ResolveFilePath(
            builder.Configuration,
            builder.Environment.EnvironmentName);

        Log.Logger = new LoggerConfiguration()
            .WriteToApiFile(logFilePath)
            .WriteTo.Async(c => c.Console())
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting STNG.HttpApi.Host.");
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration)
                        .MinimumLevel.Override("OpenIddict", LogEventLevel.Warning)
                        .ReadFrom.Services(services)
                        .WriteToApiFile(logFilePath)
                        .WriteTo.Async(c => c.AbpStudio(services));
                });
            await builder.AddApplicationAsync<STNGHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
