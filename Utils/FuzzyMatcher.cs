using System;
using System.Linq;

namespace BunbunBroll.Utils;

public static class FuzzyMatcher
{
    /// <summary>
    /// Computes the Levenshtein distance between two strings.
    /// Used for sentence-level fuzzy matching of SRT sequences.
    /// </summary>
    public static int ComputeLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            if (string.IsNullOrEmpty(target)) return 0;
            return target.Length;
        }

        if (string.IsNullOrEmpty(target)) return source.Length;

        int sourceLength = source.Length;
        int targetLength = target.Length;
        
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Step 2
        for (int i = 0; i <= sourceLength; distance[i, 0] = i++) { }
        for (int j = 0; j <= targetLength; distance[0, j] = j++) { }

        // Step 3
        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                // Step 4
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                // Step 5
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }

    /// <summary>
    /// Calculates a similarity score between 0.0 and 1.0 (1.0 meaning perfectly identical).
    /// </summary>
    public static double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(target)) return 1.0;
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;

        // Clean strings (lowercase, remove simple punctuation)
        source = CleanString(source);
        target = CleanString(target);

        if (source == target) return 1.0;

        int stepsToSame = ComputeLevenshteinDistance(source, target);
        return 1.0 - ((double)stepsToSame / (double)Math.Max(source.Length, target.Length));
    }

    private static string CleanString(string input)
    {
        // Remove basic punctuation and convert to lower case to improve match rate
        return new string(input.Where(c => !char.IsPunctuation(c)).ToArray()).ToLowerInvariant().Trim();
    }
}
