using BunbunBroll.Services;
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class VisualHookSplitTests
{
    private readonly SrtService _srtService = new();
    private readonly PhaseDetectionService _phaseDetection = new();
    private readonly TimestampSplitterService _splitter = new();

    /// <summary>
    /// Full pipeline test: SRT content → ParseSrt → MergeToSegments → SplitIntoMicroBeats
    /// Verifies that visual hook phases produce correct micro-beat splits.
    /// </summary>
    [Fact]
    public void FullPipeline_OpeningHook_SplitsInto8Beats()
    {
        // SRT entries within 0-45s (opening-hook phase)
        var srt = @"1
00:00:00,000 --> 00:00:05,000
Selama hampir dua milenium Gurun Yudea di Tepi Barat memeluk erat sebuah rahasia

2
00:00:05,000 --> 00:00:10,000
di dalam gua gua kapur yang sunyi membiarkan angin panas dan debu menutupi

3
00:00:10,000 --> 00:00:15,000
jejak sebuah komunitas yang memilih lari dari dunia demi menjaga kesucian iman mereka";

        var entries = _srtService.ParseSrt(srt);
        Assert.Equal(3, entries.Count);

        var merged = _srtService.MergeToSegments(entries, 20.0);
        // All fit within 15s < 20s, should merge into 1 segment
        Assert.Single(merged);

        var microBeats = _splitter.SplitIntoMicroBeats(merged, _phaseDetection);

        // Opening hook (0-45s) has SplitFactor=8, so should split into multiple micro-beats
        Assert.True(microBeats.Count > 1, $"Expected multiple micro-beats in opening-hook but got {microBeats.Count}");
        Assert.All(microBeats, beat => Assert.Equal("opening-hook", beat.PhaseId));

        // Each beat should have ~2.5s duration
        foreach (var beat in microBeats)
        {
            Assert.InRange(beat.DurationSeconds, 2.0, 3.0);
        }
    }

    [Fact]
    public void FullPipeline_Contextualization_SplitsInto2Beats()
    {
        // SRT entries in 50-70s range (contextualization phase: 45-180s)
        var srt = @"1
00:00:50,000 --> 00:00:55,000
Namun pada tahun 1947 dinding waktu yang tebal itu runtuh seketika

2
00:00:55,000 --> 00:01:00,000
hanya karena satu peristiwa sepele yaitu ketika seorang penggembala

3
00:01:00,000 --> 00:01:05,000
Badui melempar batu ke dalam celah gelap untuk mencari kambingnya";

        var entries = _srtService.ParseSrt(srt);
        Assert.Equal(3, entries.Count);

        var merged = _srtService.MergeToSegments(entries, 20.0);
        Assert.Single(merged);

        var microBeats = _splitter.SplitIntoMicroBeats(merged, _phaseDetection);

        // Contextualization (45-180s) has SplitFactor=2
        Assert.True(microBeats.Count >= 2, $"Expected >=2 micro-beats in contextualization but got {microBeats.Count}");
        Assert.All(microBeats, beat => Assert.Equal("contextualization", beat.PhaseId));
    }

    [Fact]
    public void FullPipeline_Normal_NoSplit()
    {
        // SRT entries after 3 minutes (normal phase: >180s)
        var srt = @"1
00:03:10,000 --> 00:03:15,000
Di sinilah letak ironi terbesarnya bahwa petunjuk paling krusial

2
00:03:15,000 --> 00:03:20,000
tentang sejarah teologi manusia justru ditemukan bukan oleh ilmuwan";

        var entries = _srtService.ParseSrt(srt);
        Assert.Equal(2, entries.Count);

        var merged = _srtService.MergeToSegments(entries, 20.0);
        Assert.Single(merged);

        var microBeats = _splitter.SplitIntoMicroBeats(merged, _phaseDetection);

        // Normal phase (>180s) has SplitFactor=1 → no splitting
        Assert.Single(microBeats);
        Assert.Equal("normal", microBeats[0].PhaseId);
    }

    [Fact]
    public void ParseWithPhaseSplitting_CrossPhase_MultipleSegments()
    {
        // SRT that spans from opening-hook (0-45s) into contextualization (45-180s)
        // With 20s max merge, these will produce separate merged segments
        var srt = @"1
00:00:00,000 --> 00:00:05,000
First five seconds of the opening hook

2
00:00:05,000 --> 00:00:10,000
More opening hook content here adding words

3
00:00:10,000 --> 00:00:15,000
Even more opening hook text for testing

4
00:00:50,000 --> 00:00:55,000
Now we are in contextualization phase

5
00:00:55,000 --> 00:01:00,000
More contextualization content continues here

6
00:03:05,000 --> 00:03:10,000
Normal phase segment after three minutes";

        var result = _srtService.ParseWithPhaseSplitting(srt, _phaseDetection, _splitter);

        // Should have beats from all 3 phases
        Assert.True(result.Count >= 3, $"Expected at least 3 micro-beats across phases but got {result.Count}");

        var phases = result.Select(r => r.PhaseId).Distinct().ToList();
        Assert.Contains("opening-hook", phases);
        Assert.Contains("contextualization", phases);
        Assert.Contains("normal", phases);

        // Opening-hook beats should have more splits than normal
        var hookBeats = result.Count(r => r.PhaseId == "opening-hook");
        var normalBeats = result.Count(r => r.PhaseId == "normal");
        Assert.True(hookBeats > normalBeats,
            $"Opening hook should have more beats ({hookBeats}) than normal ({normalBeats})");
    }

    [Fact]
    public void MergeToSegments_ProducesMultipleSegments_ForLongSrt()
    {
        // Simulate a 2-minute continuous SRT with no punctuation (auto-generated)
        var entries = new List<SrtEntry>();
        for (int i = 0; i < 24; i++)  // 24 entries × 5s = 120s
        {
            entries.Add(new SrtEntry
            {
                StartTime = TimeSpan.FromSeconds(i * 5),
                EndTime = TimeSpan.FromSeconds((i + 1) * 5),
                Text = $"segment {i} dengan beberapa kata tambahan untuk mengisi"  // ~8 words each
            });
        }

        var merged = _srtService.MergeToSegments(entries, 20.0);

        // 120s of content with 20s max should produce ~6+ segments
        Assert.True(merged.Count >= 4, $"Expected at least 4 segments for 120s SRT but got {merged.Count}");

        // Each segment should be reasonable length
        foreach (var seg in merged)
        {
            var words = seg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= 85, $"Segment has {words} words, too large");
        }
    }

    [Fact]
    public void OriginalText_PreservedInMicroBeats()
    {
        // For opening-hook, each micro-beat should have OriginalText = full segment text
        var srt = @"1
00:00:00,000 --> 00:00:05,000
Selama hampir dua milenium Gurun Yudea di Tepi Barat memeluk erat sebuah rahasia di dalam gua gua kapur yang sunyi";

        var entries = _srtService.ParseSrt(srt);
        var merged = _srtService.MergeToSegments(entries, 20.0);
        var microBeats = _splitter.SplitIntoMicroBeats(merged, _phaseDetection);

        // All micro-beats should share the same OriginalText
        var originalText = microBeats[0].OriginalText;
        Assert.All(microBeats, beat =>
        {
            Assert.Equal(originalText, beat.OriginalText);
            Assert.NotEmpty(beat.Text);  // Each beat has its own chunk
        });
    }
}
