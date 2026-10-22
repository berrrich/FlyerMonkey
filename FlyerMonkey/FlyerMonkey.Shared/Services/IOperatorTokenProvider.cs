namespace FlyerMonkey.Shared.Services;

/// <summary>Implemented by the host; tokens must never be rendered into shared components.</summary>
public interface IOperatorTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public interface IOperatorSession
{
    bool IsEnabled { get; }
    Task SignInAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync();
}

/// <summary>A deliberately separate private client. Transport must disable redirects.</summary>
public sealed class OperatorArtworkClient(HttpClient http, IOperatorTokenProvider tokens, Uri trustedApi) : IDisposable
{
    public async Task<HttpResponseMessage> GetAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (trustedApi.Scheme != "https" || !string.IsNullOrEmpty(trustedApi.UserInfo) ||
            trustedApi.AbsolutePath != "/" || !string.IsNullOrEmpty(trustedApi.Query) || !string.IsNullOrEmpty(trustedApi.Fragment) ||
            !Uri.TryCreate(relativePath, UriKind.Relative, out _) || relativePath.StartsWith('/') ||
            relativePath.Contains('\\') || relativePath.Contains('%') || relativePath.Split('/').Any(p => p is "." or "..") ||
            !(relativePath.StartsWith("api/images/artwork?", StringComparison.Ordinal) ||
              relativePath.StartsWith("api/images/artwork/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Only private artwork reads at the configured HTTPS API are allowed.");
        var target = new Uri(trustedApi, relativePath);
        if (target.GetLeftPart(UriPartial.Authority) != trustedApi.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Private API origin mismatch.");
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
        var response = await http.SendAsync(request, cancellationToken);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    public void Dispose() => http.Dispose();
}
