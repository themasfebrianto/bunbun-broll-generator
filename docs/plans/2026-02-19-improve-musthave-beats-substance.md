# Enhance Must-Have Beats Generation for Substantial Content

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the `mustHaveBeats` generation in `ConfigBatchGenerator` to produce substantive beats that align with each phase's `requiredElements` from the pattern JSON.

**Architecture:** Enhance the LLM prompt in `ConfigBatchGenerator.BuildSingleConfigPrompt()` to include phase-specific beat templates based on `requiredElements`, and add validation for beat quality.

**Tech Stack:** C# .NET 8, System.Text.Json, LLM prompt engineering

---

## Problem Analysis

### Current Issue
The generated beats are too generic and don't follow the phase structure defined in `jazirah-ilmu.json`:

### Phase Structure from jazirah-ilmu.json

| Phase | requiredElements | Beat Should Include |
|-------|-----------------|-------------------|
| **1. The Cold Open** | Cold Open tanpa salam, Misteri/Paradoks, Statement tesis, Framing misteri intelektual | Visual description + Paradok statement + Tesis framing |
| **2. The Hidden Reality** | 2 DATA KERAS, Studi kasus sejarah, High Stakes, Definisi ulang, Transisi | Angka/Tahun/Nama spesifik, Referensi (QS./HR./Kitab) |
| **3. The Systematic Breakdown** | Domino Effect, Psychological Trap, Ultimate Consequence, Istilah teknis, Studi kasus pembanding | Konsep psikologi/sains, Sebab-akibat material, Eskatologi |
| **4. The Critical Junction** | METAFORA VISUAL, Konfrontasi intelektual, Call to Mind, Silent Pauses | Metafora menyentuh perasaan, Pertanyaan tajam |
| **5. The Humble Conclusion** | Zoom out, Open loop, Closing statement rendah hati, Salam penutup | Lingering thought, Refleksi, Humility |

---

## Implementation Plan

### Task 1: Extract Phase Requirements from Pattern

**Files:**
- Create: `Services/PhaseBeatTemplate.cs`
- Modify: `Services/ConfigBatchGenerator.cs:95-183`

**Step 1: Create PhaseBeatTemplate class**

Create `Services/PhaseBeatTemplate.cs`:

```csharp
namespace BunbunBroll.Services;

/// <summary>
/// Template for generating substantial beats per phase based on requiredElements
/// </summary>
public class PhaseBeatTemplate
{
    public string PhaseName { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public List<string> RequiredElements { get; set; } = new();
    public string GuidanceTemplate { get; set; } = string.Empty;
    public List<string> BeatExamples { get; set; } = new();

    public string GetBeatPrompt()
    {
        var examples = string.Join("\n", BeatExamples.Select(b => $"  - {b}"));
        return $@"
### {PhaseName}
Required Elements:
{string.Join("\n", RequiredElements.Select(e => $"  - {e}"))}

Beat Examples (SUBSTANTIAL):
{examples}

Generate 3-5 beats for this phase following the required elements above.
Each beat should be SPECIFIC, not generic.";
    }
}

/// <summary>
/// Builds phase beat templates from pattern configuration
/// </summary>
public class PhaseBeatTemplateBuilder
{
    public List<PhaseBeatTemplate> BuildTemplatesFromPattern(Models.PatternConfiguration pattern)
    {
        var templates = new List<PhaseBeatTemplate>();

        foreach (var phase in pattern.GetOrderedPhases())
        {
            templates.Add(new PhaseBeatTemplate
            {
                PhaseName = phase.Name,
                PhaseId = phase.Id,
                RequiredElements = phase.RequiredElements.ToList(),
                GuidanceTemplate = phase.GuidanceTemplate,
                BeatExamples = GetExamplesForPhase(phase.Id)
            });
        }

        return templates;
    }

    private static List<string> GetExamplesForPhase(string phaseId)
    {
        return phaseId switch
        {
            "opening-hook" => new List<string>
            {
                "Visual hening sebuah kamar gelap, hanya diterangi cahaya biru layar smartphone yang menyorot wajah kosong seseorang.",
                "Narasi paradoks: 'Dulu, berhala itu diam di tempat dan kita yang mendatanginya. Hari ini, berhala itu ada di saku, bergetar, dan memanggil kita setiap 3 menit.'",
                "Cut cepat ke montase orang menyeberang jalan sambil menunduk, orang makan sambil menunduk, orang di masjid sambil menunduk ke layar.",
                "Tesis pembuka: Kita merasa bebas memilih konten, padahal data membuktikan kita sedang 'digembalakan' oleh algoritma."
            },
            "contextualization" => new List<string>
            {
                "Menampilkan data statistik: Rata-rata screen time orang Indonesia (salah satu tertinggi di dunia) vs waktu ibadah.",
                "Penjelasan linguistik kata 'Ilah' (Tuhan) merujuk Ibnu Taimiyah dalam Al-Ubudiyah: Bukan sekadar pencipta, tapi 'sesuatu yang hati terpaut padanya, ditaati perintahnya, dan mendominasi perasaan'.",
                "Memasukkan QS. Al-Furqan: 43 ('Terangkanlah kepadaku tentang orang yang menjadikan hawa nafsunya sebagai tuhannya...').",
                "Korelasi sains: Mekanisme 'Variable Rewards' di media sosial yang meniru mesin judi slot, didesain untuk mengeksploitasi kelemahan psikologis manusia."
            },
            "multi-dimensi" => new List<string>
            {
                "Analisis psikologi: Pergeseran dari 'Need' (Butuh Informasi) menjadi 'Craving' (Butuh Validasi/Dopamin).",
                "Konsep 'Riya Digital': Bagaimana arsitektur 'Like' dan 'Comment' memfasilitasi penyakit hati (Ujub/Sum'ah) menjadi komoditas ekonomi.",
                "Kutipan Imam Al-Ghazali tentang bahaya hati yang lalai, disandingkan dengan fenomena 'doomscrolling'.",
                "Eskalasi dampak: Hilangnya kemampuan 'Tafakkur' (berpikir mendalam) karena otak terbiasa dengan konten durasi 15 detik.",
                "Studi kasus HR. Tirmidzi tentang 'Celakalah hamba Dinar', diadaptasi ke konteks modern 'Celakalah hamba Notifikasi'."
            },
            "climax" => new List<string>
            {
                "Staccato visual: Detak jantung naik saat notifikasi bunyi. Kecemasan saat sinyal hilang. Rasa iri melihat 'story' orang lain.",
                "Reality Check: 'Kita mengira kita adalah User (pengguna). Tapi dalam bisnis model gratisan, kitalah Produknya.'",
                "Konsekuensi spiritual: Hati yang keras (Qaswah al-Qalb) karena terus menerus dipapar maksiat mata dan telinga tanpa jeda.",
                "Pertanyaan tajam: 'Jika besok internet mati selamanya, siapa ''tuhan'' yang hilang dari hidupmu? Allah atau Akses?'"
            },
            "eschatology" => new List<string>
            {
                "Bukan ajakan untuk membuang HP ke sungai (anti-teknologi), tapi ajakan 'Reclaiming the Throne' (Mengambil alih tahta hati).",
                "Solusi praktis dari konsep Tazkiyatun Nafs: Puasa digital sebagai bentuk latihan pengendalian diri (Mujahadah).",
                "Visual akhir: Seseorang meletakkan HP-nya telungkup di meja, lalu melihat ke luar jendela atau mengambil wudhu.",
                "Epilog narator: 'Berhala modern tidak butuh sesajen bunga, mereka hanya butuh waktumu. Dan waktu, adalah nyawamu.'"
            },
            _ => new List<string>()
        };
    }
}
```

**Step 2: Test PhaseBeatTemplateBuilder**

Create test file:

```csharp
// File: Tests/Services/PhaseBeatTemplateBuilderTests.cs
using Xunit;
using BunbunBroll.Services;
using BunbunBroll.Models;
using System.Text.Json;

public class PhaseBeatTemplateBuilderTests
{
    [Fact]
    public void BuildTemplatesFromPattern_ShouldCreateTemplateForEachPhase()
    {
        // Arrange
        var patternJson = File.ReadAllText("patterns/jazirah-ilmu.json");
        var pattern = JsonSerializer.Deserialize<PatternConfiguration>(patternJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var builder = new PhaseBeatTemplateBuilder();

        // Act
        var templates = builder.BuildTemplatesFromPattern(pattern!);

        // Assert
        Assert.Equal(5, templates.Count);

        var coldOpen = templates.First(t => t.PhaseId == "opening-hook");
        Assert.Contains("Misteri", string.Join(" ", coldOpen.RequiredElements));
        Assert.NotEmpty(coldOpen.BeatExamples);
    }

    [Fact]
    public void GetBeatPrompt_ShouldIncludeRequiredElementsAndExamples()
    {
        // Arrange
        var template = new PhaseBeatTemplate
        {
            PhaseName = "The Cold Open (Hook)",
            PhaseId = "opening-hook",
            RequiredElements = new List<string>
            {
                "Cold Open: Langsung masuk ke narasi",
                "Misteri: Bangun ketegangan"
            },
            BeatExamples = new List<string>
            {
                "Visual hening sebuah kamar gelap..."
            }
        };

        // Act
        var prompt = template.GetBeatPrompt();

        // Assert
        Assert.Contains("The Cold Open", prompt);
        Assert.Contains("Cold Open: Langsung masuk ke narasi", prompt);
        Assert.Contains("Visual hening sebuah kamar gelap", prompt);
    }
}
```

**Step 3: Run tests**

```bash
dotnet test Tests/Services/PhaseBeatTemplateBuilderTests.cs -f net8.0
```

Expected: PASS

**Step 4: Commit**

```bash
git add Services/PhaseBeatTemplate.cs Tests/Services/PhaseBeatTemplateBuilderTests.cs
git commit -m "feat: add PhaseBeatTemplate for substantial beat generation"
```

---

### Task 2: Update ConfigBatchGenerator Prompt

**Files:**
- Modify: `Services/ConfigBatchGenerator.cs:95-183`

**Step 1: Update BuildSingleConfigPrompt method**

```csharp
private string BuildSingleConfigPrompt(string theme, string channelName, string? seed,
    HashSet<string> existingTopics, ScriptPattern pattern)
{
    var context = existingTopics.Any()
        ? $"\nCONTEXT - DO NOT REPEAT THESE TOPICS:\n- {string.Join("\n- ", existingTopics)}"
        : "";

    // Build phase beat templates
    var templateBuilder = new PhaseBeatTemplateBuilder();
    var phaseTemplates = templateBuilder.BuildTemplatesFromPattern(pattern.Configuration);
    var beatTemplateSection = string.Join("\n", phaseTemplates.Select(t => t.GetBeatPrompt()));

    return $@"
Generate 1 (ONE) unique video configuration JSON for channel '{channelName}'.
Theme: '{theme}'.
Language: INDONESIAN (Bahasa Indonesia) for Topic, Outline, and Beats.
{context}
Seed/Instruction: {seed ?? "None"}

=== THEME GUIDANCE ===
{GetThemeGuidance(theme)}

=== REQUIREMENTS ===
1. TITLE (Topic): CRITICAL - YOU MUST USE one of the following 10 formulas:

   Formula 1: Angka + Subjek + yang Bisa/Mungkin + Konsekuensi
   Formula 2: Durasi + Kata Kerja Memahami + Kenapa + Subjek + Kata Kunci Emosional
   Formula 3: Beginilah Nasib/Keadaan + [Tempat/Orang] Setelah + [X Tahun/Kejadian]
   Formula 4: Ketika + Ide/Agama/Metode + Untuk + Hasil/Peristiwa + [Tokoh]
   Formula 5: Mereka Dibilang X, Tapi Y. Apakah Kebetulan?
   Formula 6: Mitos Atau Fakta: [Klaim Provokatif]
   Formula 7: [Nama Orang] dan [Nama Orang] di Catatan [Pelaku/Tokoh Misterius]
   Formula 8: Seberapa [Adjektif Ekstrem] + [Periode/Peristiwa] + ?
   Formula 9: Bagaimana Jika + Hipotesis/Perubahan + [Konsekuensi Besar]
   Formula 10: Reportase singkat: [Tempat/Peristiwa] — [Frasa Menarik]

2. DURATION: Between 15 - 35 minutes.
3. SOURCES (SourceReferences): THIS IS CRITICAL. You must cite specific valid sources (Quran Surah:Ayat, Hadith Narrator/Number, Name of Classical Kitab/Book).

=== PHASE-SPECIFIC BEAT REQUIREMENTS ===

Each phase has specific REQUIRED ELEMENTS that must be reflected in the beats:

{beatTemplateSection}

=== BEAT QUALITY RULES ===

ATURAN PENULISAN BEAT YANG WAJIB DIPATUHI:

1. **SPESIFIK & KONKRET**: Gunakan deskripsi visual jelas (warna, suasana, adegan)
2. **REFERENSI JELAS**: Sebutkan QS. X:Y, HR. Nama#Nomor, Nama Kitab, Nama Tokoh, Tahun
3. **KONSEPSI ILMIAH/PSIKOLOGIS**: Nama teori, mekanisme, istilah teknis dengan konteks
4. **NARASI/KALIMAT CONTOH**: Tulis kalimat aktual yang bisa diucapkan, bukan ringkasan
5. **EMOSI**: Hubungkan dengan perasaan (takut, kagum, sedih, terkejut, gelisah)
6. **HINDARI FRASA UMUM**: Jangan gunakan 'analisis', 'penjelasan', 'membahas', 'mengulas'

CONTOH BEAT YANG BAIK (SUBSTANTIAL):
- [The Cold Open]: Visual hening sebuah kamar gelap, hanya diterangi cahaya biru layar smartphone yang menyorot wajah kosong seseorang.
- [The Cold Open]: Narasi paradoks: 'Dulu, berhala itu diam di tempat dan kita yang mendatanginya. Hari ini, berhala itu ada di saku...'
- [The Hidden Reality]: Penjelasan linguistik kata 'Ilah' merujuk Ibnu Taimiyah dalam Al-Ubudiyah: Bukan sekadar pencipta, tapi 'sesuatu yang hati terpaut padanya'.
- [The Systematic Breakdown]: Konsep 'Riya Digital': Bagaimana arsitektur 'Like' dan 'Comment' memfasilitasi penyakit hati (Ujub/Sum'ah).
- [The Critical Junction]: Pertanyaan tajam: 'Jika besok internet mati selamanya, siapa ''tuhan'' yang hilang dari hidupmu?'

CONTOH BEAT YANG BURUK (TERLALU UMUM) - HINDARI:
- [The Cold Open]: Hook visual yang kuat... ❌
- [The Hidden Reality]: Penjelasan konteks sejarah... ❌
- [The Systematic Breakdown]: Analisis mendalam tentang... ❌
- [The Critical Junction]: Pertanyaan reflektif... ❌

=== OUTPUT FORMAT (STRICT JSON) ===
Return ONLY this JSON structure (no markdown text):
{{
  ""topic"": ""Judul video bahasa Indonesia"",
  ""targetDurationMinutes"": 20,
  ""outline"": ""Ringkasan alur cerita dalam 2-3 kalimat..."",
  ""sourceReferences"": ""QS. Al-Mulk: 1-5, HR. Muslim No. 203, Kitab Al-Bidaya wan Nihaya Vol 3"",
  ""mustHaveBeats"": [
    ""[The Cold Open]: Visual spesifik dengan deskripsi mendalam..."",
    ""[The Cold Open]: Narasi paradoks dengan kutipan langsung..."",
    ""[The Hidden Reality]: Data konkret: Angka/Tahun/Nama spesifik..."",
    ""[The Hidden Reality]: Referensi jelas: QS. atau HR. atau Kitab..."",
    ""... (lanjutkan untuk SEMUA 5 phase, total 15-25 beats yang substansial)""
  ]
}}";
}
```

**Step 2: Test updated prompt**

```bash
dotnet test -f net8.0
```

Expected: All tests pass

**Step 3: Commit**

```bash
git add Services/ConfigBatchGenerator.cs
git commit -m "feat: update ConfigBatchGenerator prompt with phase-specific beat templates"
```

---

### Task 3: Add Beat Quality Validation

**Files:**
- Create: `Services/BeatQualityValidator.cs`
- Modify: `Services/ConfigBatchGenerator.cs:186-220`

**Step 1: Create BeatQualityValidator**

```csharp
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
                || cleanBeat.Length > 100; // Longer beats tend to be more substantial

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
```

**Step 2: Integrate validator into ParseSingleConfig**

```csharp
private GeneratedConfig? ParseSingleConfig(string response)
{
    try
    {
        response = StripMarkdownCodeBlocks(response);

        int idxStart = response.IndexOf('{');
        int idxEnd = response.LastIndexOf('}');

        if (idxStart == -1 || idxEnd == -1) return null;

        string jsonClean = response.Substring(idxStart, idxEnd - idxStart + 1);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        GeneratedConfig? config = null;
        if (jsonClean.TrimStart().StartsWith("["))
        {
            var list = JsonSerializer.Deserialize<List<GeneratedConfig>>(jsonClean, options);
            config = list?.FirstOrDefault();
        }
        else
        {
            config = JsonSerializer.Deserialize<GeneratedConfig>(jsonClean, options);
        }

        // Validate beat quality if beats exist
        if (config?.MustHaveBeats != null && config.MustHaveBeats.Count > 0)
        {
            var validator = new BeatQualityValidator();
            var validationResult = validator.Validate(config.MustHaveBeats);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Generated config has low-quality beats: {Ratio:P0} substantial. Issues: {Issues}",
                    validationResult.SubstantialRatio,
                    string.Join("; ", validationResult.Issues));

                // Return null to trigger retry
                return null;
            }

            _logger.LogInformation(
                "Beat quality validation passed: {Ratio:P0} substantial beats",
                validationResult.SubstantialRatio);
        }

        return config;
    }
    catch (Exception)
    {
        return null;
    }
}
```

**Step 3: Add required using statement**

```csharp
using System.Text.RegularExpressions;
```

**Step 4: Run tests**

```bash
dotnet test -f net8.0
```

Expected: All tests pass

**Step 5: Commit**

```bash
git add Services/BeatQualityValidator.cs Services/ConfigBatchGenerator.cs
git commit -m "feat: add beat quality validation with 70% substantial threshold"
```

---

### Task 4: Manual Testing & Verification

**Files:**
- Test: Manual testing with batch generation

**Step 1: Run batch generation**

```bash
cd E:\VibeCode\ScriptFlow_workspace\bunbun-broll-generator
dotnet run -- --batch-generate --theme "modern idols" --count 1 --pattern jazirah-ilmu
```

**Step 2: Inspect generated beats**

```bash
# Find the latest session
cd output
ls -t | head -1 | xargs cat
```

Check `mustHaveBeats` in the output:

**Expected substantial beats:**
```json
"mustHaveBeats": [
  "[The Cold Open]: Visual hening sebuah kamar gelap, hanya diterangi cahaya biru layar smartphone...",
  "[The Cold Open]: Narasi paradoks: 'Dulu, berhala itu diam di tempat...'",
  "[The Hidden Reality]: Menampilkan data statistik: Rata-rata screen time orang Indonesia...",
  "[The Hidden Reality]: Penjelasan linguistik kata 'Ilah' merujuk Ibnu Taimiyah...",
  "[The Systematic Breakdown]: Analisis psikologi: Pergeseran dari 'Need' menjadi 'Craving'...",
  "[The Critical Junction]: Pertanyaan tajam: 'Jika besok internet mati selamanya...'"
]
```

**Not acceptable (generic):**
```json
"mustHaveBeats": [
  "[The Cold Open]: Hook visual yang kuat...",
  "[The Hidden Reality]: Penjelasan konteks sejarah...",
  "[The Systematic Breakdown]: Analisis mendalam..."
]
```

**Step 3: Update documentation**

Create `docs/beat-quality-guidelines.md`:

```markdown
# Beat Quality Guidelines

## Phase-Specific Requirements

### The Cold Open (Hook)
- **Required**: Visual description, Paradoks statement, Tesis framing
- **Format**: Visual konkret + Narasi paradoks + Statement tesis

### The Hidden Reality (Data & Context)
- **Required**: 2 DATA KERAS, Studi kasus sejarah, Referensi
- **Format**: Angka/Tahun/Nama + QS./HR./Kitab

### The Systematic Breakdown (Progressive Escalation)
- **Required**: Domino Effect, Psychological Trap, Istilah teknis
- **Format**: Konsep psikologi/sains + Sebab-akibat + Eskatologi

### The Critical Junction (Reality Check)
- **Required**: METAFORA VISUAL, Konfrontasi intelektual, Call to Mind
- **Format**: Metafora emosional + Pertanyaan tajam

### The Humble Conclusion (Epilogue)
- **Required**: Zoom out, Open loop, Humble closing
- **Format**: Lingering thought + Solusi praktis + Epilog

## Validation Rules
- Minimum 70% substantial beats
- Beats must include specific references (QS., HR., names, numbers)
- Avoid generic phrases ("analisis", "penjelasan", "membahas")
- Preferred length: 80-200 characters per beat
```

**Step 4: Commit**

```bash
git add docs/beat-quality-guidelines.md
git commit -m "docs: add beat quality guidelines with phase-specific requirements"
```

---

## Summary

This plan improves `mustHaveBeats` generation by:

1. **PhaseBeatTemplate**: Extracts `requiredElements` from pattern JSON and provides phase-specific examples
2. **Enhanced Prompt**: Includes phase-by-phase beat requirements based on actual `requiredElements`
3. **Quality Validation**: Validates 70% substantial beats with specific indicators
4. **Documentation**: Clear guidelines per phase based on jazirah-ilmu.json structure

**Expected Outcome:**
- Beats align with `requiredElements` from each phase
- Substantial content: specific references, data, concepts, not generic phrases
- Phase 1: Visual + Paradoks + Tesis
- Phase 2: Data keras + Referensi (QS./HR.)
- Phase 3: Konsep psikologi + Domino effect
- Phase 4: Metafora + Pertanyaan tajam
- Phase 5: Solusi praktis + Humble closing
