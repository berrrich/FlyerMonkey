using FlyerMonkey.Reviewer.Windows.Models;

namespace FlyerMonkey.Reviewer.Windows.Services;

/// <summary>A proposed image never changes the approved reference until explicitly accepted.</summary>
public sealed class ClassicImageProposal
{
    private readonly ExtractedProduct _product;
    private readonly string? _originalPath;
    private readonly Func<string, CancellationToken, Task<byte[]>> _generate;
    private readonly Func<string, byte[], Task<string>> _save;
    private byte[]? _proposed;
    private bool _busy;
    private bool _closed;

    public ClassicImageProposal(ExtractedProduct product, Func<string, CancellationToken, Task<byte[]>> generate, Func<string, byte[], Task<string>>? save = null)
    {
        _product = product;
        _originalPath = product.ImageFilePath;
        _generate = generate;
        _save = save ?? ClassicImageFileService.SaveAsync;
    }

    public bool HasProposal => !_closed && _proposed != null;
    public byte[]? PreviewBytes => _proposed?.ToArray();

    public async Task GenerateAsync(string? instructions, CancellationToken token = default)
    {
        if (_busy || _closed) throw new InvalidOperationException("An image operation is already running or this proposal is closed.");
        _busy = true;
        _proposed = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var bytes = await _generate(new ProductImageService().BuildImagePrompt(_product, instructions), token);
            token.ThrowIfCancellationRequested();
            ClassicImageValidation.ValidateJpeg("image/jpeg", bytes);
            if (!_closed) _proposed = bytes.ToArray();
        }
        finally { _busy = false; }
    }

    public async Task AcceptAsync(string folder, CancellationToken token = default)
    {
        if (_busy || !HasProposal) throw new InvalidOperationException("Generate a proposed image before accepting it.");
        EnsureOriginalUnchanged();
        _busy = true;
        try
        {
            token.ThrowIfCancellationRequested();
            var path = await _save(folder, _proposed!);
            token.ThrowIfCancellationRequested();
            EnsureOriginalUnchanged();
            if (_closed) throw new OperationCanceledException("Image proposal was rejected.");
            _product.ImageFilePath = path;
            _closed = true; // No cancellation check after successful acceptance.
            _proposed = null;
        }
        finally { _busy = false; }
    }

    private void EnsureOriginalUnchanged()
    {
        if (_product.ImageFilePath != _originalPath)
            throw new InvalidOperationException("The product image changed while this proposal was open. Close and reopen it before replacing the image.");
    }

    public void Reject() { _closed = true; _proposed = null; }
    public static void RemoveImage(ExtractedProduct product) => product.ImageFilePath = null;
}
