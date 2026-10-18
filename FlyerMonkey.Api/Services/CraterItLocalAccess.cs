namespace FlyerMonkey.Api.Services;

public static class CraterItLocalAccess
{
    public static bool Allows(HttpContext context, IWebHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("CraterIt:Enabled") &&
        context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote) &&
        Uri.TryCreate($"http://{context.Request.Host}", UriKind.Absolute, out var host) && host.IsLoopback &&
        context.Request.Headers["X-CraterIt-Local"] == "1";
}
