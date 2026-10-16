using FlyerMonkey.Shared.Model;

namespace FlyerMonkey.Shared.Services;

public enum CraterStyle { Classic, CometCrush, AlienExposure, Comety, PullTheLever }

public sealed record CraterPreset(CraterStyle Style, string Name, string Description, string Instruction);

public static class CraterItPromptBuilder
{
    public static IReadOnlyList<CraterPreset> Presets { get; } = Array.AsReadOnly(new[]
    {
        new CraterPreset(CraterStyle.Classic, "Crater Classic", "Realistic, product-faithful artwork with appealing lighting.",
            "Create a high-quality, realistic, commercially attractive representation. Preserve recognisable appearance, packaging, colours, branding and proportions as faithfully as the supplied information allows. Avoid destruction, exaggerated transformations, invented packaging details or misleading product characteristics."),
        new CraterPreset(CraterStyle.CometCrush, "Comet Crush", "Dramatic comet impacts and imaginative destruction.",
            "Stage dramatic comet impacts and imaginative destruction around the recognisable product."),
        new CraterPreset(CraterStyle.AlienExposure, "Alien Exposure", "Playful science-fiction encounters and extraterrestrial transformations.",
            "Imagine playful science-fiction encounters and extraterrestrial transformations of the recognisable product."),
        new CraterPreset(CraterStyle.Comety, "Comety", "Absurd inventions, elaborate chain reactions and improbable machinery.",
            "Create whimsical, absurd, cartoon-like visual comedy inspired by Heath Robinson-style contraptions: elaborate chain reactions and impossible machinery launching, squeezing, transporting or transforming the product. Prioritise humour, imagination and surprising details over destruction."),
        new CraterPreset(CraterStyle.PullTheLever, "Pull the Lever", "Let the AI choose a surprising direction for this product.",
            "Choose a surprising creative direction appropriate to the product: realistic product photography, dramatic comet impacts, playful alien encounters, absurd cartoon contraptions, or an imaginative combination. Prioritise a coherent, unexpected result.")
    });

    public static CraterPreset GetPreset(CraterStyle style) =>
        Presets.FirstOrDefault(p => p.Style == style) ?? throw new ArgumentOutOfRangeException(nameof(style));

    public static string Build(Product product, CraterStyle style)
    {
        ArgumentNullException.ThrowIfNull(product);
        var preset = GetPreset(style);
        // Preserve the established Classic opening, metadata order and plain background.
        var subject = $"{Field(product.Brand)} {Field(product.Name)}, {Field(product.Variant)}, {Field(product.PackSizeText)}";
        var opening = style == CraterStyle.Classic
            ? $"Create a clean product image for {subject} on a plain background."
            : $"Create imaginative product artwork for {subject}.";
        return $"{opening} {preset.Instruction} Use original imagery; do not include movie-specific scenes or copyrighted characters.";
    }

    private static string Field(string? value)
    {
        var text = (value ?? "").Trim();
        return text[..Math.Min(text.Length, 300)];
    }
}
