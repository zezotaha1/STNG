using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace STNG;

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STNG_BROWSER_NODE")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STNG_PLAYWRIGHT_MODULES")))
            Skip = "Set STNG_BROWSER_NODE and STNG_PLAYWRIGHT_MODULES to run the real Microsoft Edge HTTPS test.";
    }
}

[Collection("Authentication host")]
public class FamilyJoinBrowserTests
{
    private readonly ITestOutputHelper _output;
    public FamilyJoinBrowserTests(ITestOutputHelper output) => _output = output;

    [BrowserFact]
    public async Task Edge_swagger_join_approval_and_ownership_transfer_use_the_real_https_pipeline()
    {
        var host = new AuthenticationHostFixture(network: true);
        Process? process = null;
        try
        {
            await host.InitializeAsync();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.GetDirectories("src").Length == 0) root = root.Parent ?? throw new DirectoryNotFoundException("Repository root");
            var script = Path.Combine(root.FullName, "test", "STNG.HttpApi.Host.Tests", "Browser", "family-join-local.cjs");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("STNG_BROWSER_NODE")!)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root.FullName
            };
            start.ArgumentList.Add(script);
            process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync();
            var accounts = new[] { "A", "B", "C" }.Select(label => new
            {
                label, email = $"browser-{Guid.NewGuid():N}@example.com",
                password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12))
            }).ToArray();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { root = host.Authority, accounts }));
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var passed = 0;
            var completed = false;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var message = JsonDocument.Parse(line);
                var item = message.RootElement;
                if (item.GetProperty("type").GetString() == "otp")
                {
                    var label = item.GetProperty("label").GetString();
                    var email = accounts.Single(x => x.label == label).email;
                    var code = Regex.Match(host.Email.Messages.Last(x => x.Email == email).Body, @"\b\d{6}\b").Value;
                    code.Length.ShouldBe(6);
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { otp = code }));
                    await process.StandardInput.FlushAsync();
                }
                else if (item.GetProperty("type").GetString() == "advance")
                {
                    var minutes = item.TryGetProperty("minutes", out var duration) ? duration.GetInt32() : 2;
                    minutes.ShouldBeInRange(1, 4321);
                    host.Clock.Advance(TimeSpan.FromMinutes(minutes));
                    await process.StandardInput.WriteLineAsync("{}");
                    await process.StandardInput.FlushAsync();
                }
                else
                {
                    _output.WriteLine(line); // Script emits assertion names and status codes only.
                    item.GetProperty("passed").GetBoolean().ShouldBeTrue();
                    completed |= item.GetProperty("name").GetString() == "All isolated browser assertions completed";
                    passed++;
                }
            }
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.ShouldBe(0, await stderr);
            completed.ShouldBeTrue("The browser must finish the entire workflow, including the final isolation and expiry checks");
            passed.ShouldBeGreaterThan(0);
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process?.Dispose();
            await ((IAsyncLifetime)host).DisposeAsync();
        }
    }
}
