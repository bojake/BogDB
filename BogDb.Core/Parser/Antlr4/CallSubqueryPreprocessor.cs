using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BogDb.Core.Parser.Antlr4;

/// <summary>
/// Pre-processes Cypher query text to extract CALL { subquery } blocks that the
/// current ANTLR grammar doesn't handle natively. Replaces each CALL { ... }
/// with a placeholder MATCH clause that the transformer can detect and replace.
///
/// The placeholder form is:
///   MATCH (__call_subquery_N:__CallSubquery)
/// which is syntactically valid for ANTLR but semantically recognized by the
/// transformer as a CALL subquery marker.
/// </summary>
public class CallSubqueryPreprocessor
{
    private readonly List<string> _extractedBodies = new();
    public IReadOnlyList<string> ExtractedBodies => _extractedBodies;

    /// <summary>
    /// Marker prefix for placeholder node patterns inserted for CALL subqueries.
    /// </summary>
    public const string PlaceholderPrefix = "__call_subquery_";
    public const string PlaceholderLabel = "__CallSubquery";

    /// <summary>
    /// Scans the query text for CALL { ... } blocks and replaces each with a
    /// placeholder MATCH clause. Returns the rewritten query.
    /// </summary>
    public string Preprocess(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query;

        var result = new StringBuilder(query.Length);
        int pos = 0;

        while (pos < query.Length)
        {
            // Look for CALL followed by optional whitespace and {
            var callMatch = FindCallSubquery(query, pos);
            if (callMatch.Start < 0)
            {
                result.Append(query, pos, query.Length - pos);
                break;
            }

            // Append everything before the CALL
            result.Append(query, pos, callMatch.Start - pos);

            // Extract the body between { and }
            var body = query.Substring(callMatch.BodyStart, callMatch.BodyEnd - callMatch.BodyStart);
            int index = _extractedBodies.Count;
            _extractedBodies.Add(body.Trim());

            // Insert placeholder: MATCH (__call_subquery_N:__CallSubquery)
            // This is syntactically valid for the ANTLR parser and will be
            // detected by the transformer.
            result.Append($"MATCH ({PlaceholderPrefix}{index}:{PlaceholderLabel})");

            pos = callMatch.End;
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns true if the given variable name is a CALL subquery placeholder.
    /// </summary>
    public static bool IsPlaceholder(string variableName)
        => variableName.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Extracts the placeholder index from a placeholder variable name.
    /// </summary>
    public static int GetPlaceholderIndex(string variableName)
    {
        var suffix = variableName.Substring(PlaceholderPrefix.Length);
        return int.Parse(suffix);
    }

    private struct CallSubqueryMatch
    {
        public int Start;      // Start of "CALL" keyword
        public int BodyStart;  // Start of body (after '{')
        public int BodyEnd;    // End of body (before '}')
        public int End;        // Position after '}'
    }

    private static CallSubqueryMatch FindCallSubquery(string query, int startPos)
    {
        var result = new CallSubqueryMatch { Start = -1 };

        for (int i = startPos; i <= query.Length - 5; i++)
        {
            // Check for CALL keyword (case-insensitive)
            if (!IsCallAt(query, i))
                continue;

            // Must be word boundary before CALL
            if (i > 0 && char.IsLetterOrDigit(query[i - 1]))
                continue;

            int afterCall = i + 4;

            // Skip whitespace after CALL
            while (afterCall < query.Length && char.IsWhiteSpace(query[afterCall]))
                afterCall++;

            // Must be followed by {
            if (afterCall >= query.Length || query[afterCall] != '{')
                continue;

            // Find the matching closing brace, accounting for nested braces and strings
            int braceDepth = 1;
            int bodyStart = afterCall + 1;
            int pos = bodyStart;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            while (pos < query.Length && braceDepth > 0)
            {
                char c = query[pos];

                if (inSingleQuote)
                {
                    if (c == '\\' && pos + 1 < query.Length) { pos += 2; continue; }
                    if (c == '\'') inSingleQuote = false;
                }
                else if (inDoubleQuote)
                {
                    if (c == '\\' && pos + 1 < query.Length) { pos += 2; continue; }
                    if (c == '"') inDoubleQuote = false;
                }
                else
                {
                    switch (c)
                    {
                        case '\'': inSingleQuote = true; break;
                        case '"': inDoubleQuote = true; break;
                        case '{': braceDepth++; break;
                        case '}': braceDepth--; break;
                    }
                }
                pos++;
            }

            if (braceDepth != 0)
                throw new InvalidOperationException(
                    "Unmatched '{' in CALL subquery — missing closing '}'.");

            result.Start = i;
            result.BodyStart = bodyStart;
            result.BodyEnd = pos - 1;  // pos is one past '}'
            result.End = pos;
            return result;
        }

        return result;
    }

    private static bool IsCallAt(string query, int pos)
    {
        if (pos + 4 > query.Length) return false;
        return (query[pos] == 'C' || query[pos] == 'c')
            && (query[pos + 1] == 'A' || query[pos + 1] == 'a')
            && (query[pos + 2] == 'L' || query[pos + 2] == 'l')
            && (query[pos + 3] == 'L' || query[pos + 3] == 'l')
            // Must be end or non-alphanumeric after CALL (word boundary)
            && (pos + 4 >= query.Length || !char.IsLetterOrDigit(query[pos + 4]));
    }
}
