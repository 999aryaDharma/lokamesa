using System;
using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Domain;

public static class MetadataNormalizer
{
    public static string? NormalizePaperSummary(IEnumerable<string>? paperSizes)
    {
        if (paperSizes == null) return null;

        var cleaned = paperSizes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => StandardizePaperName(s.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0) return null;
        if (cleaned.Count == 1) return cleaned[0];

        return $"Mixed ({string.Join(", ", cleaned)})";
    }

    public static string? NormalizeMarginSummary(IEnumerable<string>? marginValues)
    {
        if (marginValues == null) return null;

        var cleaned = marginValues
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => StandardizeMarginName(s.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0) return null;
        if (cleaned.Count == 1) return cleaned[0];

        return $"Mixed ({string.Join(", ", cleaned)})";
    }

    public static string? NormalizeFontSummary(IEnumerable<string>? fonts)
    {
        if (fonts == null) return null;

        var cleaned = fonts
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0) return null;
        return string.Join(", ", cleaned);
    }

    private static string StandardizePaperName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "a4" => "A4",
            "letter" => "Letter",
            "legal" => "Legal",
            "a3" => "A3",
            "a5" => "A5",
            "b5" => "B5",
            "executive" => "Executive",
            _ => name
        };
    }

    private static string StandardizeMarginName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "normal" => "Normal",
            "narrow" => "Narrow",
            "wide" => "Wide",
            "moderate" => "Moderate",
            "mirrored" => "Mirrored",
            _ => name
        };
    }
}
