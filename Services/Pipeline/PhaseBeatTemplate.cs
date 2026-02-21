namespace BunbunBroll.Services;

/// <summary>
/// Template for generating substantial beats per phase based on requiredElements
/// Following Jazirah Ilmu golden standard: narrative-focused, long flowing content
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
        var elements = string.Join("\n", RequiredElements.Select(e => $"  - {e}"));
        var examples = string.Join("\n", BeatExamples.Select(b => $"  - {b}"));
        return $@"
### {PhaseName}
Required Elements:
{elements}

Beat Examples (Jazirah Ilmu Style - NARRATIVE FOCUSED):
{examples}

Generate 3-5 beats for this phase following the required elements above.
Each beat should be SUBSTANTIAL, NARRATIVE-FOCUSED, and follow Jazirah Ilmu style.
NOT visual instructions like 'Close up iris mata' but STORY CONTENT like 'Ceritakan tentang...'";
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
                "Beat 1: Narasi pembuka panjang yang mengalir - mulai dengan observasi luas tentang dunia yang tampak tenang/normal, lalu perlahan mengarah ke anomali atau paradoks yang tersembunyi. Contoh JI: 'Dunia hari ini terlihat tenang. Layar menyala, pasar bergerak... Tapi di balik itu semua, ada satu titik rapuh...'",
                "Beat 2: Koneksi ke konteks historis/keagamaan - ceritakan bagaimana peradaban masa lalu menghadapi fenomena serupa (penyembahan berhala), lalu hubungkan ke kondisi masa kini. GUNAKAN kalimat panjang yang mengalir dengan klausa 'yang', 'namun', 'tapi'.",
                "Beat 3: Pengenalan topik sebagai misteri intelektual - bukan sebagai peringatan moral, tapi sebagai investigasi tentang apa yang sebenarnya terjadi di balik layar/ponsel. Buat audiens penasaran, bukan merasa digurui.",
                "Beat 4 (Opsional): Reinforcement paradoks - tunjukkan kontras antara apa yang kita kira (kita pengguna bebas) vs realita (kita sedang dimanipulasi)."
            },
            "contextualization" => new List<string>
            {
                "Beat 1: DATA KONKRET - Sajikan angka/statistik yang mengejutkan dengan cara naratif yang mengalir. Jangan hanya sebut '8 jam 36 menit', tapi ceritakan implikasinya: 'Jika dikalkulasi, hampir sepertiga usia produktif kita habis dalam posisi menunduk pada layar, melakukan sujud digital yang durasinya jauh melampaui waktu yang kita berikan untuk Tuhan pemilik semesta.'",
                "Beat 2: REFERENSI KITAB sebagai FAKTA SEJARAH - Sebut tokoh/kitab (Ibnu Taimiyah, Al-Ghazali, dll) sebagai konteks analisis, bukan untuk dakwah. Ceritakan definisi atau konsep dengan bahasa yang mengalir.",
                "Beat 3: DALIL/QURAN sebagai KONTEKS NARASI - Masukkan ayat/hadits sebagai bagian dari alur cerita sejarah atau analisis, bukan sebagai 'bukti' yang dilempar. Ceritakan bagaimana teks tersebut relevan dengan fenomena yang dibahas.",
                "Beat 4 (Opsional): ANALISIS SAINTIS - Jelaskan mekanisme psikologis atau neurosains (dopamin, Variable Rewards, dll) dengan bahasa yang edukatif dan mengalir, bukan seperti kuliah akademis."
            },
            "multi-dimensi" => new List<string>
            {
                "Beat 1: ANALISIS PSIKOLOGI MENGENAL - Jelaskan mekanisme 'Intermittent Variable Rewards' atau sistem dopamin yang dipakai media sosial. Ceritakan bagaimana ini diadopsi dari mesin judi slot. GUNAKAN kalimat panjang dengan klausa 'yang', 'yaitu', 'di mana'.",
                "Beat 2: STUDI KASUS HADITS HISTORIS - Bawa hadits Nabi tentang 'hamba dinar/dirham' sebagai FAKTA SEJARAH, lalu hubungkan ke konteks modern (social currency, validation, viral). JANGAN gunakan gaya 'seandainya Nabi ada hari ini', tapi analisis pola yang sama.",
                "Beat 3: DAMPAK FISIK/SARAF - Ceritakan fenomena 'Phantom Vibration Syndrome' atau dampak fisik lainnya sebagai bukti bagaimana sistem saraf kita telah dikondisikan. GUNAKAN narasi yang mengalir, bukan poin-poin terpisah.",
                "Beat 4: EROSI IBADAH - Analisis dampak pada ibadah (Khusyu, Tafakkur, fokus shalat) dengan cara yang mendalam tapi tidak menghakumi. Ceritakan bagaimana otak yang terbiasa konten 15 detik kehilangan kemampuan untuk durasi panjang.",
                "Beat 5 (Opsional): STUDI BANDING - Bandingkan kondisi masa kini dengan peradaban atau zaman lain untuk menunjukkan pola sejarah."
            },
            "climax" => new List<string>
            {
                "Beat 1: METAFORA VISUAL yang menyentuh - Ceritakan suatu adegan/metafora yang menggambarkan kondisi saat ini (misal: keluarga di meja makan dengan 'kabel transparan' menyedot perhatian ke layar). GUNAKAN narasi panjang yang mengalir, JANGAN gunakan '(Hening 3 detik)' atau dramatic pauses.",
                "Beat 2: REALITY CHECK - Konfrontasi intelektual, bukan emosional. Tunjukkan konsekuensi logis dari kondisi saat ini melalui analisis. JANGAN gunakan 'Siapa Tuanmu?' secara langsung.",
                "Beat 3: CLIMAX ANALISIS - Puncak dari analisis multi-dimensi yang menghubungkan semua aspek (psikologi, teknologi, spiritual). GUNAKAN kalimat panjang yang mengalir ke kesimpulan yang menohok.",
                "Beat 4 (Opsional): REFLEKSI - Bawa audiens untuk merenung tentang posisi mereka sendiri tanpa menyalahkan atau menghakumi."
            },
            "eschatology" => new List<string>
            {
                "Beat 1: SOLUSI/PERSPEKTIF BARU - Tawarkan perspektif baru atau solusi yang tidak klise. JANGAN 'pokoknya tobat' tapi analisis yang lebih mendalam (misal: konsep uzlah di era modern, puasa digital, dll).",
                "Beat 2: ZOOM OUT - Kembali ke big picture, rangkum perjalanan dari awal hingga akhir. GUNAKAN kalimat panjang yang mengalir.",
                "Beat 3: OPEN LOOP (JI Style) - Pernyataan reflektif yang menggantungkan kesimpulan pada audiens, BUKAN pertanyaan langsung. Contoh: 'Pada akhirnya, pertanyaan bukan lagi tentang kebenaran klaim keagamaan di masa lalu, melainkan tentang apakah kita memiliki kebijaksanaan untuk membedakan antara keyakinan yang memerdekakan dan fanatisme yang memperbudak.'",
                "Beat 4: VISUAL AKHIR - Deskripsikan adegan penutup dengan narasi yang mengalir (misal: seseorang meletakkan HP dan beranjak mengambil wudhu). JANGAN gunakan '(Fade out)' sebagai instruksi, tapi deskripsikan sebagai cerita.",
                "Beat 5 (Opsional): CLOSING REFLECTION - Refleksi terakhir tentang makna perjalanan yang telah dibahas."
            },
            _ => new List<string>()
        };
    }
}
