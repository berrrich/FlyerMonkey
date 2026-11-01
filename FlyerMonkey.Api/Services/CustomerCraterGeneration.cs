using FlyerMonkey.Shared.Model;
using OpenAI;
using System.ClientModel.Primitives;

namespace FlyerMonkey.Api.Services;

public interface ICustomerCraterProvider
{
    Task<byte[]> GenerateAsync(string prompt, CancellationToken token);
}
public sealed class CustomerCraterProvider : ICustomerCraterProvider
{
    private readonly ImageGenerationService service;
    public CustomerCraterProvider(IConfiguration configuration, OpenAIClientOptions? options = null)
    {
        options ??= new OpenAIClientOptions();
        options.RetryPolicy = new ClientRetryPolicy(0);
        service = new(configuration, options);
    }
    public async Task<byte[]> GenerateAsync(string prompt, CancellationToken token) =>
        (await service.GenerateAsync(prompt, token)).ToArray();
}

public sealed class CustomerCraterGeneration(ICustomerCraterStore store)
{
    public CustomerCraterAttempt Status(Guid customer, bool eligible)
    {
        using var lease = store.Lock(customer, 139);
        if (lease is null) return new("Generating", false, false);
        var record = store.Read(customer, 139);
        if (record is null) return new("Ready", eligible, false);
        // A released lease with an unfinished journal indicates interruption/restart. Never dispatch again.
        if (record.State is "Pending" or "Generating")
        {
            record = record with { State = "Unknown", UpdatedUtc = DateTimeOffset.UtcNow };
            store.Write(record);
        }
        var image = record.State == "Completed" && store.ReadImage(customer, 139) is not null;
        return new(record.State, false, image);
    }
    public async Task<CustomerCraterAttempt> GenerateAsync(Guid customer, Product product,
        Func<ICustomerCraterProvider> providerFactory)
    {
        if (product.ID != 139) throw new InvalidDataException("Product not allowed.");
        using var lease = store.Lock(customer, 139);
        if (lease is null) return new("Generating", false, false);
        var existing = store.Read(customer, 139);
        if (existing is not null)
        {
            var state = existing.State is "Pending" or "Generating" ? "Unknown" : existing.State;
            return new(state, false, state == "Completed" && store.ReadImage(customer, 139) is not null);
        }
        var record = new CustomerAttemptRecord(customer, 139, "Pending", DateTimeOffset.UtcNow);
        store.Reserve(record);
        var dispatched = false;
        try
        {
            var provider = providerFactory(); // Credential/configuration failure consumes reservation, before payment.
            record = record with { State = "Generating", UpdatedUtc = DateTimeOffset.UtcNow };
            store.Write(record);
            var subject = $"{product.Brand} {product.Name}, {product.Variant}, {product.PackSizeText}";
            subject = subject[..Math.Min(900, subject.Length)];
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            dispatched = true;
            var image = await provider.GenerateAsync("Create playful imaginative product-inspired artwork featuring " +
                subject + ". Send this product crashing through a supermarket aboard a comet.", timeout.Token);
            store.SaveImage(customer, 139, image);
            record = record with { State = "Completed", UpdatedUtc = DateTimeOffset.UtcNow };
            store.Write(record);
            return new("Completed", false, true);
        }
        catch (Exception)
        {
            record = record with { State = dispatched ? "Unknown" : "Failed", UpdatedUtc = DateTimeOffset.UtcNow };
            // If this write fails, the durable Pending/Generating reservation still prevents another call.
            store.Write(record);
            return new(record.State, false, false);
        }
    }
}
