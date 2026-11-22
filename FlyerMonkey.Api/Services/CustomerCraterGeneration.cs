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

public sealed class CustomerCraterGeneration(ICustomerCraterStore store, TimeProvider? clock = null,
    int retestLimit = 0, bool unlimited = false)
{
    private DateOnly UtcDay => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
    private bool HasAccountCapacity(Guid customer) => unlimited || store.AttemptsUsed(customer) <= retestLimit;
    private bool HasDailyCapacity() => unlimited || store.HasDailyCapacity(UtcDay);

    public CustomerCraterAttempt Status(Guid customer, bool eligible, int productId = 139)
    {
        using var lease = store.Lock(customer);
        if (lease is null) return new("Generating", false, false);
        var record = store.Read(customer, productId);
        if (record is null)
        {
            if (!eligible) return new("Ready", false, false);
            if (!HasAccountCapacity(customer)) return new("AccountLimitReached", false, false);
            return HasDailyCapacity() ? new("Ready", true, false, AttemptVersion: unlimited ? 0 : null) :
                new("DailyLimitReached", false, false);
        }
        var attempt = ReadExistingAttempt(record);
        return attempt with
        {
            CanGenerate = eligible && attempt.HasImage && attempt.State == "Completed" &&
                HasAccountCapacity(customer) && HasDailyCapacity(),
            AttemptVersion = unlimited && eligible ? checked(record.RetestsUsed + 1) : null
        };
    }
    public async Task<CustomerCraterAttempt> GenerateAsync(Guid customer, Product product,
        Func<ICustomerCraterProvider> providerFactory, bool repeat = false, int? expectedVersion = null)
    {
        if (product.ID <= 0) throw new InvalidDataException("Product not allowed.");
        using var lease = store.Lock(customer);
        if (lease is null) return new("Generating", false, false);
        var existing = store.Read(customer, product.ID);
        // An unlimited account can make another paid request only from a fresh status snapshot.
        if (unlimited && expectedVersion != (existing is null ? 0 : checked(existing.RetestsUsed + 1)))
            return new("RequestStale", false, false);
        byte[]? previousImage = null;
        if (existing is not null)
        {
            var previous = ReadExistingAttempt(existing);
            if (!repeat || existing.State != "Completed" || !previous.HasImage) return previous;
            previousImage = store.ReadImage(customer, product.ID);
            if (previousImage is null) return previous with { HasImage = false };
        }
        else if (repeat) return new("Ready", false, false);
        if (!HasAccountCapacity(customer)) return new("AccountLimitReached", false, previousImage is not null);
        if (!unlimited && !store.TryReserveDailySlot(UtcDay)) return new("DailyLimitReached", false, false);
        if (existing is not null && previousImage is not null) store.PreservePreview(existing, previousImage);
        var subject = $"{product.Brand} {product.Name}, {product.Variant}, {product.PackSizeText}";
        subject = subject[..Math.Min(900, subject.Length)];
        var prompt = "Create playful imaginative product-inspired artwork featuring " +
            subject + ". Put the product into planet of the apes motion picture.";
        var started = DateTimeOffset.UtcNow;
        var record = new CustomerAttemptRecord(customer, product.ID, "Pending", started,
            existing is not null, existing is null ? 0 : existing.RetestsUsed + 1, started, prompt);
        if (record.IsRepeat) store.Write(record);
        else store.Reserve(record);
        var dispatched = false;
        try
        {
            var provider = providerFactory(); // Credential/configuration failure consumes reservation, before payment.
            record = record with { State = "Generating", UpdatedUtc = DateTimeOffset.UtcNow };
            store.Write(record);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            dispatched = true;
            var image = await provider.GenerateAsync(prompt, timeout.Token);
            store.SaveImage(record, image);
            record = record with { State = "Completed", UpdatedUtc = DateTimeOffset.UtcNow };
            store.Write(record);
            return new("Completed", false, true);
        }
        catch (Exception)
        {
            record = record with { State = dispatched ? "Unknown" : "Failed", UpdatedUtc = DateTimeOffset.UtcNow };
            // If this write fails, the durable Pending/Generating reservation still prevents another call.
            store.Write(record);
            return new(record.State, false, record.IsRepeat);
        }
    }

    private CustomerCraterAttempt ReadExistingAttempt(CustomerAttemptRecord record)
    {
        // The JPEG is committed atomically before the journal. Recover it under the same lease.
        var image = record.IsRepeat || record.State is "Completed" or "Generating" or "Unknown"
            ? store.ReadImage(record.CustomerId, record.ProductId)
            : null;
        var state = record.State;
        if (!record.IsRepeat && state is "Generating" or "Unknown" && image is [255, 216, 255, ..])
            state = "Completed";
        else if (state is "Pending" or "Generating")
            state = "Unknown";
        if (state != record.State)
            store.Write(record with { State = state, UpdatedUtc = DateTimeOffset.UtcNow });
        return new(state, false, (state == "Completed" || record.IsRepeat) && image is not null);
    }
}
