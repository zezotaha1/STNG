using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace STNG;

public class ApiErrorPageTests
{
    [Theory]
    [InlineData("/api/families/mine", 401)]
    [InlineData("/api/auth/me", 401)]
    [InlineData("/API/families/test", 403)]
    [InlineData("/api/families/unknown", 404)]
    [InlineData("/connect/token", 400)]
    public async Task Api_and_protocol_errors_preserve_status_and_authentication_headers(string path, int status)
    {
        using var host = await CreateHostAsync(status);
        using var client = host.GetTestClient();
        using var response = await client.GetAsync(path);

        ((int)response.StatusCode).ShouldBe(status);
        response.Headers.Location.ShouldBeNull();
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/Account/Protected")]
    [InlineData("/api-documentation")]
    public async Task Browser_pages_keep_the_existing_error_page_redirect(string path)
    {
        using var host = await CreateHostAsync(401);
        using var client = host.GetTestClient();
        using var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/Error?httpStatusCode=401");
    }

    private static Task<IHost> CreateHostAsync(int status) => new HostBuilder()
        .ConfigureWebHost(web => web.UseEnvironment("Production").UseTestServer().Configure(app =>
        {
            STNGHttpApiHostModule.ConfigureErrorPages(app);
            app.Run(context =>
            {
                context.Response.StatusCode = status;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return Task.CompletedTask;
            });
        }))
        .StartAsync();
}
