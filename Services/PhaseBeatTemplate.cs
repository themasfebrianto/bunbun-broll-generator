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
