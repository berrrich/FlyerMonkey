namespace FlyerMonkey.Api.Services;

internal static class CustomerCraterAccess
{
    public static bool IsLocalHttps(HttpContext context, IWebHostEnvironment environment) =>
        environment.IsDevelopment() &&
        context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
        context.Request.IsHttps && (context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1");

    public static bool PreviewEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("CustomerAuthentication:Enabled") &&
        configuration.GetValue<bool>("CustomerCrater:Enabled") &&
        configuration.GetValue<bool>("CraterIt:Enabled");

    public static bool GenerationEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration["CustomerCrater:GenerationEnabled"], out var enabled) && enabled;

    public static Guid? AllowedCustomer(IConfiguration configuration) =>
        Guid.TryParse(configuration["CustomerCrater:AllowedCustomerId"], out var customer) && customer != Guid.Empty
            ? customer : null;

    public static bool HasUnlimitedCraterIt(IConfiguration configuration, Guid customer) =>
        customer != Guid.Empty &&
        Guid.TryParse(configuration["CustomerCrater:UnlimitedCustomerId"], out var unlimited) && customer == unlimited;
}
