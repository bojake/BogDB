using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BogDb.Extensions.FTS;

/// <summary>
/// Configurable tokenizer for full-text search.
/// C++ parity: extension/fts/src/function/tokenize.cpp
/// </summary>
public sealed class FtsTokenizer
{
    private static readonly HashSet<string> DefaultStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English stop words (Porter stemmer standard list)
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and",
        "any", "are", "aren't", "as", "at", "be", "because", "been", "before", "being",
        "below", "between", "both", "but", "by", "can't", "cannot", "could", "couldn't",
        "did", "didn't", "do", "does", "doesn't", "doing", "don't", "down", "during",
        "each", "few", "for", "from", "further", "get", "got", "had", "hadn't", "has",
        "hasn't", "have", "haven't", "having", "he", "he'd", "he'll", "he's", "her",
        "here", "here's", "hers", "herself", "him", "himself", "his", "how", "how's",
        "i", "i'd", "i'll", "i'm", "i've", "if", "in", "into", "is", "isn't", "it",
        "it's", "its", "itself", "let's", "me", "more", "most", "mustn't", "my",
        "myself", "no", "nor", "not", "of", "off", "on", "once", "only", "or", "other",
        "ought", "our", "ours", "ourselves", "out", "over", "own", "same", "shan't",
        "she", "she'd", "she'll", "she's", "should", "shouldn't", "so", "some", "such",
        "than", "that", "that's", "the", "their", "theirs", "them", "themselves", "then",
        "there", "there's", "these", "they", "they'd", "they'll", "they're", "they've",
        "this", "those", "through", "to", "too", "under", "until", "up", "very", "was",
        "wasn't", "we", "we'd", "we'll", "we're", "we've", "were", "weren't", "what",
        "what's", "when", "when's", "where", "where's", "which", "while", "who", "who's",
        "whom", "why", "why's", "will", "with", "won't", "would", "wouldn't", "you",
        "you'd", "you'll", "you're", "you've", "your", "yours", "yourself", "yourselves"
    };

    private static readonly Regex DefaultTokenPattern = new(@"[a-zA-Z0-9]+", RegexOptions.Compiled);

    private readonly HashSet<string> _stopWords;
    private readonly Regex _tokenPattern;
    private readonly bool _enableStemming;

    public FtsTokenizer(
        HashSet<string>? stopWords = null,
        string? ignorePattern = null,
        bool enableStemming = true)
    {
        _stopWords = stopWords ?? DefaultStopWords;
        _tokenPattern = ignorePattern != null
            ? new Regex($"[^{ignorePattern}]+", RegexOptions.Compiled)
            : DefaultTokenPattern;
        _enableStemming = enableStemming;
    }

    /// <summary>
    /// Tokenizes text into normalized terms suitable for indexing/querying.
    /// </summary>
    public List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (Match m in _tokenPattern.Matches(text))
        {
            var word = m.Value.ToLowerInvariant();
            if (word.Length == 0) continue;
            if (_stopWords.Contains(word)) continue;
            if (_enableStemming)
                word = PorterStemmer.Stem(word);
            if (word.Length > 0)
                tokens.Add(word);
        }
        return tokens;
    }
}

/// <summary>
/// Simplified Porter stemmer for English.
/// C++ uses Snowball; this is a pure-C# approximation.
/// </summary>
internal static class PorterStemmer
{
    public static string Stem(string word)
    {
        if (word.Length <= 2) return word;

        var w = word;

        // Step 1a: plurals
        if (w.EndsWith("sses")) w = w[..^2];
        else if (w.EndsWith("ies")) w = w[..^2];
        else if (!w.EndsWith("ss") && w.EndsWith("s")) w = w[..^1];

        // Step 1b: -ed, -ing
        if (w.EndsWith("eed"))
        {
            if (MeasureOf(w[..^3]) > 0) w = w[..^1];
        }
        else if (w.EndsWith("ed") && HasVowel(w[..^2]))
        {
            w = w[..^2];
            w = FixStep1b(w);
        }
        else if (w.EndsWith("ing") && HasVowel(w[..^3]))
        {
            w = w[..^3];
            w = FixStep1b(w);
        }

        // Step 1c: y → i
        if (w.EndsWith("y") && HasVowel(w[..^1]))
            w = w[..^1] + "i";

        // Step 2: common suffixes
        w = ApplyStep2(w);

        // Step 3: further suffixes
        w = ApplyStep3(w);

        // Step 4: remove -tion, etc.
        w = ApplyStep4(w);

        // Step 5: final cleanup
        if (w.EndsWith("e"))
        {
            if (MeasureOf(w[..^1]) > 1) w = w[..^1];
            else if (MeasureOf(w[..^1]) == 1 && !EndsWithCvc(w[..^1])) w = w[..^1];
        }
        if (w.EndsWith("ll") && MeasureOf(w[..^1]) > 1) w = w[..^1];

        return w;
    }

    private static string FixStep1b(string w)
    {
        if (w.EndsWith("at") || w.EndsWith("bl") || w.EndsWith("iz"))
            return w + "e";
        if (w.Length >= 2 && w[^1] == w[^2] && !"lsz".Contains(w[^1]))
            return w[..^1];
        if (MeasureOf(w) == 1 && EndsWithCvc(w))
            return w + "e";
        return w;
    }

    private static readonly (string suffix, string replacement)[] Step2Rules =
    {
        ("ational", "ate"), ("tional", "tion"), ("enci", "ence"), ("anci", "ance"),
        ("izer", "ize"), ("abli", "able"), ("alli", "al"), ("entli", "ent"),
        ("eli", "e"), ("ousli", "ous"), ("ization", "ize"), ("ation", "ate"),
        ("ator", "ate"), ("alism", "al"), ("iveness", "ive"), ("fulness", "ful"),
        ("ousness", "ous"), ("aliti", "al"), ("iviti", "ive"), ("biliti", "ble"),
    };

    private static string ApplyStep2(string w)
    {
        foreach (var (suffix, rep) in Step2Rules)
        {
            if (w.EndsWith(suffix))
            {
                var stem = w[..^suffix.Length];
                if (MeasureOf(stem) > 0) return stem + rep;
                return w;
            }
        }
        return w;
    }

    private static readonly (string suffix, string replacement)[] Step3Rules =
    {
        ("icate", "ic"), ("ative", ""), ("alize", "al"), ("iciti", "ic"),
        ("ical", "ic"), ("ful", ""), ("ness", ""),
    };

    private static string ApplyStep3(string w)
    {
        foreach (var (suffix, rep) in Step3Rules)
        {
            if (w.EndsWith(suffix))
            {
                var stem = w[..^suffix.Length];
                if (MeasureOf(stem) > 0) return stem + rep;
                return w;
            }
        }
        return w;
    }

    private static readonly string[] Step4Suffixes =
    {
        "al", "ance", "ence", "er", "ic", "able", "ible", "ant", "ement",
        "ment", "ent", "ion", "ou", "ism", "ate", "iti", "ous", "ive", "ize"
    };

    private static string ApplyStep4(string w)
    {
        foreach (var suffix in Step4Suffixes)
        {
            if (w.EndsWith(suffix))
            {
                var stem = w[..^suffix.Length];
                if (suffix == "ion" && stem.Length > 0 && (stem[^1] == 's' || stem[^1] == 't')
                    && MeasureOf(stem) > 1)
                    return stem;
                if (suffix != "ion" && MeasureOf(stem) > 1)
                    return stem;
            }
        }
        return w;
    }

    private static bool IsVowel(char c) => "aeiou".Contains(c);

    private static bool HasVowel(string s) => s.Any(IsVowel);

    private static int MeasureOf(string s)
    {
        int n = 0;
        int i = 0;
        while (i < s.Length && IsVowel(s[i])) i++;
        while (i < s.Length)
        {
            while (i < s.Length && !IsVowel(s[i])) i++;
            if (i >= s.Length) break;
            n++;
            while (i < s.Length && IsVowel(s[i])) i++;
        }
        return n;
    }

    private static bool EndsWithCvc(string s)
    {
        if (s.Length < 3) return false;
        return !IsVowel(s[^1]) && IsVowel(s[^2]) && !IsVowel(s[^3])
               && !"wxy".Contains(s[^1]);
    }
}
