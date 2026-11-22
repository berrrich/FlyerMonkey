using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlyerMonkey.Api.Services;

public sealed record CustomerAttemptRecord(Guid CustomerId, int ProductId, string State, DateTimeOffset UpdatedUtc,
    bool IsRepeat = false, int RetestCount = 0, DateTimeOffset? StartedUtc = null, string? Prompt = null)
{
    [JsonIgnore]
    public int RetestsUsed => Math.Max(IsRepeat ? 1 : 0, RetestCount);
}

public interface ICustomerCraterStore
{
    bool HasDailyCapacity(DateOnly day);
    bool TryReserveDailySlot(DateOnly day);
    IDisposable? Lock(Guid customer);
    int AttemptsUsed(Guid customer);
    CustomerAttemptRecord? Read(Guid customer, int product);
    void Reserve(CustomerAttemptRecord record);
    void Write(CustomerAttemptRecord record);
    void PreservePreview(CustomerAttemptRecord record, byte[] image);
    void SaveImage(CustomerAttemptRecord record, byte[] image);
    byte[]? ReadImage(Guid customer, int product);
}

// Deliberately separate from ICraterArtworkStore and DATA/CraterArt. No operator reader uses this root.
public sealed class CustomerCraterStore(string root) : ICustomerCraterStore
{
    private const int DailyLimit = 10;

    private string DailySlot(DateOnly day, int slot) =>
        Path.Combine(root, "daily-quota", day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            slot.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public bool HasDailyCapacity(DateOnly day) =>
        Enumerable.Range(1, DailyLimit).Any(slot => !File.Exists(DailySlot(day, slot)));

    public bool TryReserveDailySlot(DateOnly day)
    {
        for (var slot = 1; slot <= DailyLimit; slot++)
        {
            var path = DailySlot(day, slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            FileStream reservation;
            try { reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }

            using (reservation)
                reservation.Flush(flushToDisk: true);
            return true;
        }
        return false;
    }

    public static string Root(IWebHostEnvironment environment) =>
        Path.Combine(Path.GetDirectoryName(LocalCraterArtworkStore.ResolveRoot(environment))!, "CustomerCraterArt");
    private string CustomerFolder(Guid customer)
    {
        if (customer == Guid.Empty) throw new InvalidDataException("Invalid customer artwork key.");
        return Path.Combine(root, customer.ToString("N"));
    }

    private string Folder(Guid customer, int product)
    {
        if (product <= 0) throw new InvalidDataException("Invalid product artwork key.");
        return Path.Combine(CustomerFolder(customer), product.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public IDisposable? Lock(Guid customer)
    {
        var folder = CustomerFolder(customer);
        Directory.CreateDirectory(folder);
        try { return new FileStream(Path.Combine(folder, "lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    public int AttemptsUsed(Guid customer)
    {
        var used = 0;
        foreach (var folder in Directory.EnumerateDirectories(CustomerFolder(customer)))
        {
            if (!int.TryParse(Path.GetFileName(folder), out var product) || product <= 0) continue;
            if (Read(customer, product) is { } record)
                used = checked(used + 1 + record.RetestsUsed);
        }
        return used;
    }
    public CustomerAttemptRecord? Read(Guid customer, int product)
    {
        var path = Path.Combine(Folder(customer, product), "attempt.json");
        if (!File.Exists(path)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<CustomerAttemptRecord>(File.ReadAllText(path));
            if (record is null || record.CustomerId != customer || record.ProductId != product || record.RetestCount < 0 ||
                record.State is not ("Pending" or "Generating" or "Completed" or "Failed" or "Unknown"))
                throw new InvalidDataException("Invalid customer attempt journal.");
            return record;
        }
        catch (JsonException) { throw new InvalidDataException("Unreadable customer attempt journal."); }
    }
    public void Reserve(CustomerAttemptRecord record)
    {
        var folder = Folder(record.CustomerId, record.ProductId);
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(Path.Combine(folder, "attempt.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, record);
        stream.Flush(true);
        WriteMetadata(record);
    }
    public void Write(CustomerAttemptRecord record)
    {
        WriteMetadata(record);
        Atomic(Path.Combine(Folder(record.CustomerId, record.ProductId), "attempt.json"), JsonSerializer.SerializeToUtf8Bytes(record));
    }

    private string AttemptFolder(CustomerAttemptRecord record) =>
        Path.Combine(Folder(record.CustomerId, record.ProductId), "attempts",
            (record.RetestsUsed + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void WriteMetadata(CustomerAttemptRecord record)
    {
        var folder = AttemptFolder(record);
        Directory.CreateDirectory(folder);
        Atomic(Path.Combine(folder, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(record,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public void PreservePreview(CustomerAttemptRecord record, byte[] image)
    {
        var path = Path.Combine(AttemptFolder(record), "artwork.jpg");
        if (File.Exists(path)) return;
        ValidateImage(image);
        WriteMetadata(record);
        Atomic(path, image);
    }

    public void SaveImage(CustomerAttemptRecord record, byte[] image)
    {
        ValidateImage(image);
        Atomic(Path.Combine(AttemptFolder(record), "artwork.jpg"), image);
        SaveImage(record.CustomerId, record.ProductId, image);
    }

    public void SaveImage(Guid customer, int product, byte[] image)
    {
        ValidateImage(image);
        Atomic(Path.Combine(Folder(customer, product), "artwork.jpg"), image);
    }

    private static void ValidateImage(byte[] image)
    {
        if (image.Length < 3 || image[0] != 255 || image[1] != 216 || image[2] != 255)
            throw new InvalidDataException("Provider did not return a JPEG.");
    }
    public byte[]? ReadImage(Guid customer, int product)
    {
        var path = Path.Combine(Folder(customer, product), "artwork.jpg");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
    private static void Atomic(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}
