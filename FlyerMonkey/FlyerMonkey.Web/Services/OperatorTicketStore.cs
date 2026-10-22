using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace FlyerMonkey.Web.Services;

/// <summary>Single-process pilot sessions. Cookie sign-out removes the ticket, including for old-cookie replay.</summary>
internal sealed class OperatorTicketStore : ITicketStore, IDisposable
{
    private readonly MemoryCache tickets = new(new MemoryCacheOptions());

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "OperatorSession-" + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        tickets.Set(key, ticket, ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(1));
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        Task.FromResult(tickets.Get<AuthenticationTicket>(key));

    public Task RemoveAsync(string key)
    {
        tickets.Remove(key);
        return Task.CompletedTask;
    }

    public void Dispose() => tickets.Dispose();
}
