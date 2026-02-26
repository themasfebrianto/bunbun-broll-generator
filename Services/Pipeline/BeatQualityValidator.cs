using System.Text.RegularExpressions;

namespace BunbunBroll.Services;

/// <summary>
/// Validates that generated beats have substantial content aligned with requiredElements
/// </summary>
public class BeatQualityValidator
{
    private static readonly string[] GenericPhrases = new[]
    {
        "hook visual", "visual yang kuat", "visual menarik", "visual opening",
        "pertanyaan retoris", "pertanyaan untuk memancing", "pertanyaan reflektif",
        "penjelasan konteks", "penjelasan sejarah", "penjelasan tentang",
        "kutipan dalil", "kutipan pertama", "kutipan ayat",
        "analisis mendalam", "analisis tentang", "analisis mendalam",
        "membahas", "mengulas", "menjelaskan", "mendeskripsikan",
        "studi kasus", "contoh nyata", "ilustrasi"
    };

    private static readonly string[] SubstantialIndicators = new[]
    {
        "QS.", "HR.", "Surah", "ayat", "hadits",
        "Ibnu", "Imam", "Kitab", "Al-", "Rasulullah",
        "visual", "narasi", "paradoks", "mekanisme",
        "psikologis", "variable rewards", "dopamin", "kognitif",
        "konsekuensi", "pertanyaan tajam", "kata kunci",
        "data statistik", "screen time", "tahun", "abad",
        "definisi", "konsep", "studi kasus", "penelitian"
    };

    public ValidationResult Validate(List<string> beats)
    {
        var issues = new List<string>();
        int substantialCount = 0;

        foreach (var beat in beats)
        {
            // Extract beat content after phase prefix
            var cleanBeat = beat;
            int bracketIdx = beat.IndexOf(']');
            if (bracketIdx >= 0)
            {
                cleanBeat = beat.Substring(bracketIdx + 1).TrimStart(':', ' ');
            }

            // Check for generic phrases (bad indicators)
            bool isGeneric = GenericPhrases.Any(p => cleanBeat.ToLower().Contains(p.ToLower()));

            // Check for substantial indicators (good indicators)
            bool isSubstantial = SubstantialIndicators.Any(i => cleanBeat.ToLower().Contains(i.ToLower()))
                || cleanBeat.Length > 30; // Short concise beats with content are substantial

            // Check for specific patterns that indicate substance
            bool hasSpecificContent = cleanBeat.Contains("'") || cleanBeat.Contains("\"")
                || Regex.IsMatch(cleanBeat, @"\d+") // Has numbers
                || cleanBeat.Contains(':'); // Has references like "QS. X:Y"

            if (isGeneric && cleanBeat.Length < 60 && !hasSpecificContent)
            {
                issues.Add($"Beat terlalu umum untuk phase: \"{cleanBeat.Substring(0, Math.Min(50, cleanBeat.Length))}...\"");
            }
            else if (isSubstantial || hasSpecificContent)
            {
                substantialCount++;
            }
        }

        // At least 70% of beats should be substantial
        double substantialRatio = beats.Count > 0 ? (double)substantialCount / beats.Count : 0;
        bool isValid = issues.Count == 0 && substantialRatio >= 0.7;

        return new ValidationResult
        {
            IsValid = isValid,
            Issues = issues,
            SubstantialRatio = substantialRatio
        };
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = new();
    public double SubstantialRatio { get; set; }
}
