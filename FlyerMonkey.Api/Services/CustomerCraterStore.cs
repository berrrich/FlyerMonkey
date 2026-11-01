using System.Text.Json;

namespace FlyerMonkey.Api.Services;

public sealed record CustomerAttemptRecord(Guid CustomerId, int ProductId, string State, DateTimeOffset UpdatedUtc);

public interface ICustomerCraterStore
{
    IDisposable? Lock(Guid customer, int product);
    CustomerAttemptRecord? Read(Guid customer, int product);
    void Reserve(CustomerAttemptRecord record);
    void Write(CustomerAttemptRecord record);
    void SaveImage(Guid customer, int product, byte[] image);
    byte[]? ReadImage(Guid customer, int product);
}

// Deliberately separate from ICraterArtworkStore and DATA/CraterArt. No operator reader uses this root.
public sealed class CustomerCraterStore(string root) : ICustomerCraterStore
{
    public static string Root(IWebHostEnvironment environment) =>
        Path.Combine(Path.GetDirectoryName(LocalCraterArtworkStore.ResolveRoot(environment))!, "CustomerCraterArt");
    private string Folder(Guid customer, int product)
    {
        if (customer == Guid.Empty || product != 139) throw new InvalidDataException("Invalid customer artwork key.");
        return Path.Combine(root, customer.ToString("N"), "139");
    }
    public IDisposable? Lock(Guid customer, int product)
    {
        var folder = Folder(customer, product);
        Directory.CreateDirectory(folder);
        try { return new FileStream(Path.Combine(folder, "lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
    public CustomerAttemptRecord? Read(Guid customer, int product)
    {
        var path = Path.Combine(Folder(customer, product), "attempt.json");
        if (!File.Exists(path)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<CustomerAttemptRecord>(File.ReadAllText(path));
            if (record is null || record.CustomerId != customer || record.ProductId != product ||
                record.State is not ("Pending" or "Generating" or "Completed" or "Failed" or "Unknown"))
                throw new InvalidDataException("Invalid customer attempt journal.");
            return record;
        }
        catch (JsonException) { throw new InvalidDataException("Unreadable customer attempt journal."); }
    }
    public void Reserve(CustomerAttemptRecord record)
    {
        using var stream = new FileStream(Path.Combine(Folder(record.CustomerId, record.ProductId), "attempt.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, record);
        stream.Flush(true); // Permanent one-attempt reservation; never deleted or reset by this application.
    }
    public void Write(CustomerAttemptRecord record) => Atomic(
        Path.Combine(Folder(record.CustomerId, record.ProductId), "attempt.json"), JsonSerializer.SerializeToUtf8Bytes(record));
    public void SaveImage(Guid customer, int product, byte[] image)
    {
        if (image.Length < 3 || image[0] != 255 || image[1] != 216 || image[2] != 255)
            throw new InvalidDataException("Provider did not return a JPEG.");
        Atomic(Path.Combine(Folder(customer, product), "artwork.jpg"), image);
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
