using System;
using BunbunBroll.Services;
using BunbunBroll.Models;
using System.Text.RegularExpressions;

namespace BunbunBroll.Tests
{
    public class TestCompression
    {
        public static void Run()
        {
            var prompt1 = "1500 BC Ancient Egypt era, prophetic confrontation, ultra-wide cinematic panoramic view of the Red Sea freshly parted with towering walls of dark turquoise water on both sides, the dry seabed stretching into the distance under a dramatic amber and bronze sky, thousands of small figures of freed slaves walking dazed and scattered across the exposed sandy ocean floor, their dusty robes billowing in fierce wind, footprints trailing behind them being slowly erased by blowing sand, in the far background the collapsed water churning where an army has just been swallowed, debris and broken chariot wheels half-buried in wet sand in the foreground, the lighting is intense high-contrast with golden directional sunlight breaking through dark storm clouds casting long dramatic shadows across the seabed, the atmosphere heavy with settling dust and sea mist, warm earthy tones of amber terracotta ochre and burnt sienna dominating the palette with deep teal water contrasting against the desert-gold ground, the scale is epic and vast emphasizing the enormity of the miracle against the smallness of the human figures, a single rocky hilltop visible on the far shore where a lone robed male figure stands silhouetted against the light face replaced by intense white-golden divine light facial features not visible, the mood is both triumphant and eerily unsettled as if victory itself carries an ominous weight, semi-realistic academic painting style with visible brushstrokes, traditional Islamic iconography mixed with Western historical art influences, expressive painterly textures, atmospheric depth, ultra-detailed, sharp focus, 8k quality, consistent visual tone";
            var compressed1 = PromptCompressor.Compress(prompt1);
            Console.WriteLine($"Original1: {prompt1.Length} chars");
            Console.WriteLine($"Compressed1: {compressed1.Length} chars");
            Console.WriteLine($"Savings1: {100 - (double)compressed1.Length/prompt1.Length * 100:F1}%\n");
            Console.WriteLine($"Result1: {compressed1}\n");
        }
    }
}
