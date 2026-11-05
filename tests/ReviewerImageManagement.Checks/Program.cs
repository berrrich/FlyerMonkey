using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlyerMonkey.Reviewer.Windows;
using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;

internal static class Program
{
    private static int checks;
    private static void Check(bool result, string name) { if (!result) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    private static async Task Fails<T>(Func<Task> action, string name) where T : Exception
    { try { await action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL " + name); }
    internal static byte[] Jpeg(byte colour)
    {
        var pixels = Enumerable.Repeat(colour, 32 * 32 * 3).ToArray();
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgr24, null, pixels, 96)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    [STAThread] private static int Main(string[] args)
    {
        try { if (args.Contains("--ui")) return RunUi(); Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task Run()
    {
        var folder = Path.GetFullPath(Path.Combine(".artifacts", "reviewer-image-management", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        var originalBytes = Jpeg(40); var replacement = Jpeg(200);
        var original = Path.Combine(folder, "original.jpg"); await File.WriteAllBytesAsync(original, originalBytes);
        var p = new ExtractedProduct { ProductName = "Milk", Brand = "Brand", Variant = "Full cream", PackSizeText = "2L", Price = "$3", ImageFilePath = original };
        var prompt = new ProductImageService();
        Check(prompt.BuildImagePrompt(p).StartsWith("Create a clean product image for Brand Milk, Full cream, 2L."), "non-produce Classic product facts preserved");
        Check(prompt.BuildImagePrompt(p, "  brighter lighting  ").Contains("Additional operator instructions:\nbrighter lighting"), "optional instructions appended and trimmed");
        Check(prompt.BuildImagePrompt(p, "   ") == prompt.BuildImagePrompt(p), "blank instructions preserve existing prompt");
        foreach (var name in new[] { "Australian Navel Oranges", "Bananas", "Broccoli", "Garlic", "Mangoes", "Strawberries", "Baby Spinach" })
        {
            var produce = new ExtractedProduct { ProductName = name, Brand = "InventedBrandMustNotAppear", PackSizeText = "2.75 kg", Category = "Fresh Fruit & Vegetables" };
            var text = prompt.BuildImagePrompt(produce, "Use a plastic bag and a branded sticker");
            Check(text.Contains("actual fresh produce: " + name) && text.Contains("natural form"), name + " identifies actual produce and natural form");
            Check(!text.Contains("InventedBrandMustNotAppear") && !text.Contains("2.75") && text.EndsWith("Keep the produce loose even if other instructions suggest packaging."), name + " loose produce excludes brand/weight and retains final no-packaging constraint");
        }
        Check(prompt.BuildImagePrompt(new ExtractedProduct { ProductName = "Blueberries", PackSizeText = "125g punnet" }).Contains("packaging explicitly described in the product data: 125g punnet"), "explicit produce packaging is grounded in actual data");
        foreach (var name in new[] { "Apple Juice", "Apple Pies", "Tomato Soup", "Apple Cider Vinegar", "Potato Gems" })
            Check(!prompt.BuildImagePrompt(new ExtractedProduct { ProductName = name }).Contains("actual fresh produce"), name + " not mistaken for loose produce");
        Check(!prompt.BuildImagePrompt(new ExtractedProduct { ProductName = "Chicken Garlic", Category = "Meat" }).Contains("actual fresh produce"), "meat with produce ingredients not misclassified");

        var unusual = prompt.BuildImagePrompt(new ExtractedProduct { ProductName = "Dragonfruit" });
        Check(unusual.Contains("Dragonfruit") && unusual.Contains("exact named produce in its natural form") && unusual.Contains("For loose produce, omit all packaging"), "unclassified produce still receives natural-form and loose-produce safeguards");
        var notices = new List<string?>(); p.PropertyChanged += (_, e) => notices.Add(e.PropertyName);
        var products = new List<ExtractedProduct> { p, new() { ProductName = "Other offer", Price = "$4" } };
        var db = Path.Combine(folder, "drafts.db"); var saver = new ExtractionSaveService(db);
        await saver.SaveAsync(new FlyerFile { Retailer = "Offline", FileName = "fixture.pdf" }, new FlyerPage { FileName = "page.pdf", PageNumber = 1 }, products);
        async Task<List<ExtractedProduct>> Reload() => await new SqlCommitService(db).LoadProductsAsync((await new ExtractionReadService(db).GetAllAsync()).Single());
        ClassicImageProposal.RemoveImage(p);
        Check(p.ImageFilePath == null && products.Count == 2 && p.ProductName == "Milk" && p.Price == "$3", "Remove Image preserves product and offer fields");
        Check(File.ReadAllBytes(original).SequenceEqual(originalBytes), "Remove Image keeps original file");
        Check(notices.Contains(nameof(p.HasImage)) && notices.Contains(nameof(p.ImageActionText)) && !p.HasImage, "removal notifies preview and image-action bindings");
        await saver.UpdateSavedExtractionAsync(1, products);
        Check((await Reload())[0].ImageFilePath == null && (await Reload()).Count == 2, "removed reference persists through SQLite Save/reload");
        p.ImageFilePath = original;
        var ready = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposal = new ClassicImageProposal(p, (_, _) => ready.Task);
        var generation = proposal.GenerateAsync("less shadow");
        Check(p.ImageFilePath == original && !proposal.HasProposal, "generation in progress preserves approved image");
        await Fails<InvalidOperationException>(() => proposal.GenerateAsync(null), "repeated generation blocked");
        ready.SetResult(replacement); await generation;
        Check(p.ImageFilePath == original && proposal.HasProposal && proposal.PreviewBytes!.SequenceEqual(replacement), "successful generation is a proposal only");
        Check(Directory.GetFiles(folder, "*.jpg").Length == 1, "unaccepted proposal writes no image file");
        proposal.Reject(); Check(p.ImageFilePath == original && !proposal.HasProposal, "reject proposal keeps approved image");
        await Fails<InvalidOperationException>(() => proposal.AcceptAsync(folder), "rejected proposal cannot be accepted");
        using (var cancel = new CancellationTokenSource())
        {
            var delayed = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new ClassicImageProposal(p, (_, _) => delayed.Task);
            var pending = cancelled.GenerateAsync(null, cancel.Token); cancel.Cancel(); delayed.SetResult(replacement);
            await Fails<OperationCanceledException>(() => pending, "provider result after cancellation discarded");
            Check(p.ImageFilePath == original && !cancelled.HasProposal, "cancelled generation preserves original reference");
        }
        var calls = 0;
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); var cancelled = new ClassicImageProposal(p, (_, _) => { calls++; return Task.FromResult(replacement); });
            await Fails<OperationCanceledException>(() => cancelled.GenerateAsync(null, cancel.Token), "pre-cancelled proposal rejected");
            Check(calls == 0, "pre-cancelled proposal never calls provider");
        }
        var failed = new ClassicImageProposal(p, (_, _) => { calls++; throw new IOException("offline failure"); });
        await Fails<IOException>(() => failed.GenerateAsync(null), "provider failure surfaced");
        Check(p.ImageFilePath == original && !failed.HasProposal && calls == 1, "provider failure preserves image without retry");
        var bad = new ClassicImageProposal(p, (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }));
        await Fails<InvalidDataException>(() => bad.GenerateAsync(null), "invalid generated bytes rejected");
        Check(p.ImageFilePath == original && !bad.HasProposal, "invalid response never becomes an accepted image");
        var accepted = new ClassicImageProposal(p, (_, _) => Task.FromResult(replacement)); await accepted.GenerateAsync("brighter");
        await accepted.AcceptAsync(folder); var acceptedPath = p.ImageFilePath!;
        Check(acceptedPath != original && File.ReadAllBytes(acceptedPath).SequenceEqual(replacement) && File.ReadAllBytes(original).SequenceEqual(originalBytes), "explicit acceptance changes reference and preserves original file");
        Check(p.HasImage && p.ImageActionText == "Crater It!", "accepted image keeps one-click Crater action; editing is separate");
        await Fails<InvalidOperationException>(() => accepted.AcceptAsync(folder), "double acceptance refused");
        await saver.UpdateSavedExtractionAsync(1, products);
        Check((await Reload())[0].ImageFilePath == acceptedPath, "accepted image survives SQLite Save/reload");
        var stale = new ClassicImageProposal(p, (_, _) => Task.FromResult(originalBytes)); await stale.GenerateAsync(null); p.ImageFilePath = original;
        await Fails<InvalidOperationException>(() => stale.AcceptAsync(folder), "stale proposal cannot overwrite a changed image reference");
        var rejectedWhilePending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new ClassicImageProposal(p, (_, _) => rejectedWhilePending.Task); var request = closed.GenerateAsync(null); closed.Reject(); rejectedWhilePending.SetResult(replacement); await request;
        Check(p.ImageFilePath == original && !closed.HasProposal, "closing during generation cannot install late result");
        var io = new ClassicImageProposal(p, (_, _) => Task.FromResult(replacement)); await io.GenerateAsync(null);
        await Fails<IOException>(() => io.AcceptAsync(original), "accept save failure preserves approved reference");
        Check(p.ImageFilePath == original && File.ReadAllBytes(original).SequenceEqual(originalBytes), "failed save never deletes or replaces original file");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); await Fails<OperationCanceledException>(() => io.AcceptAsync(folder, cancel.Token), "cancelled acceptance does not change original reference");
            Check(p.ImageFilePath == original, "cancelled acceptance keeps approved image");
        }
        foreach (var closeInsteadOfCancel in new[] { true, false })
        {
            var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var latePath = Path.Combine(folder, "unattached-" + Guid.NewGuid().ToString("N") + ".jpg");
            await File.WriteAllBytesAsync(latePath, replacement);
            var late = new ClassicImageProposal(p, (_, _) => Task.FromResult(replacement), (_, _) => saved.Task);
            await late.GenerateAsync(null);
            using var cancel = new CancellationTokenSource();
            var accepting = late.AcceptAsync(folder, cancel.Token);
            Check(p.ImageFilePath == original, "acceptance pending preserves original reference");
            if (closeInsteadOfCancel) late.Reject(); else cancel.Cancel();
            saved.SetResult(latePath);
            await Fails<OperationCanceledException>(() => accepting, closeInsteadOfCancel ? "closing during acceptance preserves original" : "cancellation during acceptance preserves original");
            Check(p.ImageFilePath == original && File.Exists(latePath) && File.Exists(original), "cancelled acceptance retains original and unattached new file without deleting either");
        }
        p.ImageFilePath = acceptedPath; ClassicImageProposal.RemoveImage(p); await saver.UpdateSavedExtractionAsync(1, products);
        Check((await Reload())[0].ImageFilePath == null && File.Exists(acceptedPath) && File.Exists(original), "removing accepted replacement preserves both files and persists cleared reference");
        Console.WriteLine($"PASS: {checks} offline Reviewer image-management checks; no provider/SQL/Azure calls. Fixtures retained at {folder}");
    }
    private static int RunUi()
    {
        var folder = Path.GetFullPath(Path.Combine(".artifacts", "reviewer-image-management", "ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "original.jpg"); File.WriteAllBytes(original, Jpeg(40));
        var p = new ExtractedProduct { ProductName = "Australian Navel Oranges", PackSizeText = "kg", ImageFilePath = original };
        var app = new Application();
        var window = new ClassicImageEditorWindow(p, folder, CancellationToken.None, async (_, token) => { await Task.Delay(300, token); return Jpeg(200); });
        window.Title = "Classic Image Editor - OFFLINE FIXTURE";
        ((System.Windows.Controls.TextBlock)window.FindName("StatusText")).Text = "Offline fixture: Generate uses synthetic samples and never contacts a provider.";
        Console.WriteLine("Fake-only UI fixture: " + folder); window.ShowDialog(); Console.WriteLine("Final image: " + p.ImageFilePath); return 0;
    }
}
