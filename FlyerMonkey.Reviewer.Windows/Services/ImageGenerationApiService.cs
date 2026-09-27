using System.Net.Http;
using System.Net.Http.Json;

namespace FlyerMonkey.Reviewer.Windows.Services;

public class ImageGenerationApiService
{
    private readonly HttpClient _httpClient;

    public ImageGenerationApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(
                "https://flyermonkeyapi-g7htasdacxfzgbcd.australiaeast-01.azurewebsites.net/")
        };
    }

    public async Task<byte[]> GenerateImageAsync(string prompt)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/images/generate",
            new { Prompt = prompt });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }
}