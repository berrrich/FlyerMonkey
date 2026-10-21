using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using OpenAI;
using OpenAI.Images;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed class ClassicImageGenerationService
{
    private const string Operation = "Classic image generation: POST https://api.openai.com/v1/images/generations";
    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(3);
    // Reuse connections. Never follow redirects with the operator's credential.
    private static readonly HttpClient ProviderHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ImageClient _client;
    private readonly TimeSpan _timeout;

    public ClassicImageGenerationService() : this(
        () => Environment.GetEnvironmentVariable("FLYERMONKEY_OPENAI_KEY"), ProviderHttp, GenerationTimeout) { }

    // The offline runner links this file and injects a transport that never opens sockets.
    internal ClassicImageGenerationService(Func<string?> readKey, HttpClient http, TimeSpan timeout)
    {
        var key = readKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Classic image generation is not configured: set FLYERMONKEY_OPENAI_KEY in the workstation environment before starting the Reviewer.");
        _timeout = timeout;
        _client = new ImageClient("gpt-image-1", new ApiKeyCredential(key), new OpenAIClientOptions
        {
            RetryPolicy = new ClientRetryPolicy(0),
            Transport = new HttpClientPipelineTransport(http),
            NetworkTimeout = timeout,
            ClientLoggingOptions = new ClientLoggingOptions { EnableLogging = false, EnableMessageContentLogging = false },
            EnableDistributedTracing = false
        });
    }

    public async Task<byte[]> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("Classic image generation canceled before sending; no request was made.", cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
#pragma warning disable OPENAI001
            var result = await _client.GenerateImageAsync(prompt, new ImageGenerationOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
                OutputFileFormat = GeneratedImageFileFormat.Jpeg,
                OutputCompressionFactor = 80
            }, timeout.Token);
#pragma warning restore OPENAI001
            var bytes = result.Value.ImageBytes?.ToArray()
                ?? throw new InvalidDataException("Classic image generation response validation failed: no image bytes returned.");
            // The provider protocol returns JSON containing base64, not an image/jpeg HTTP body.
            // JPEG is explicitly requested above; validate the actual decoded image before saving.
            ClassicImageValidation.ValidateJpeg("image/jpeg", bytes);
            return bytes;
        }
        catch (ClientResultException ex)
        {
            throw new HttpRequestException(ex.Status > 0
                ? $"{Operation} returned HTTP {ex.Status}. No automatic retry was made."
                : $"{Operation} failed during HTTP transport; no HTTP status is available. No automatic retry was made.",
                null, ex.Status > 0 ? (System.Net.HttpStatusCode)ex.Status : null);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException($"{Operation} failed during HTTP transport; no HTTP status is available. No automatic retry was made.");
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("Classic image generation canceled. No automatic retry was made; the provider may already have generated an image.", cancellationToken);
            throw new TimeoutException($"{Operation} timed out; HTTP status unavailable. No automatic retry was made; the provider may already have generated an image.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Classic image generation response validation failed: unexpected provider response. No image was saved.");
        }
    }
}
