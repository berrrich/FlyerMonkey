using System.Text.RegularExpressions;

namespace FlyerMonkey.Reviewer.Windows.Services;

public static class JsonResponseCleaner
{
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "The model returned an empty response.");
        }

        var cleaned = raw.Trim();

        // Remove UTF-8 BOM / zero-width characters if present
        cleaned = cleaned
            .TrimStart('\uFEFF')
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "");

        // Strip Markdown code fences
        cleaned = Regex.Replace(
            cleaned,
            @"^```(?:json)?\s*",
            "",
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(
            cleaned,
            @"\s*```$",
            "");

        cleaned = cleaned.Trim();

        // Keep only the JSON payload if the model added commentary.
        int arrayStart = cleaned.IndexOf('[');
        int objectStart = cleaned.IndexOf('{');

        int start;

        if (arrayStart >= 0 && objectStart >= 0)
            start = Math.Min(arrayStart, objectStart);
        else
            start = Math.Max(arrayStart, objectStart);

        int arrayEnd = cleaned.LastIndexOf(']');
        int objectEnd = cleaned.LastIndexOf('}');

        int end = Math.Max(arrayEnd, objectEnd);

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException(
                "No JSON object or array was found in the model response.");
        }

        return cleaned.Substring(
            start,
            end - start + 1);
    }
}