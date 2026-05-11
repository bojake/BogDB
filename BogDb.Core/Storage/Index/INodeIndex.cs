using System.Collections.Generic;

namespace BogDb.Core.Storage.Index;

/// <summary>
/// Common interface for all node property index implementations.
/// Allows callers to use either the in-memory dict-based index or the
/// disk-backed HashIndex without caring about the backing store.
/// C++ parity: src/storage/index/hash_index.h (virtual interface layer).
/// </summary>
public interface INodeIndex
{
    /// <summary>Insert or overwrite the offset stored for <paramref name="key"/>.</summary>
    void Put(object key, long nodeOffset);

    /// <summary>
    /// Returns true and sets <paramref name="nodeOffset"/> if the key exists.
    /// Returns false if the key is not indexed.
    /// </summary>
    bool TryLookup(object key, out long nodeOffset);

    /// <summary>
    /// Returns all tracked offsets for <paramref name="key"/>, preserving insertion order.
    /// Returns false if the key is not indexed.
    /// </summary>
    bool TryLookupAll(object key, out IReadOnlyList<long> nodeOffsets);

    /// <summary>
    /// Remove the specific (key, offset) entry from the index.
    /// Returns true if the entry was found and removed.
    /// </summary>
    bool Remove(object key, long nodeOffset);

    /// <summary>Number of distinct keys in this index.</summary>
    long Count { get; }

    /// <summary>Enumerate all indexed keys and their tracked offsets.</summary>
    IEnumerable<KeyValuePair<object, IReadOnlyList<long>>> EnumerateEntries();

    /// <summary>Remove all entries from the index.</summary>
    void Clear();

    /// <summary>
    /// Collect all offsets whose key is a string starting with <paramref name="prefix"/>.
    /// Returns false if no matching keys are found.
    /// Default implementation: linear scan over <see cref="EnumerateEntries"/>.
    /// </summary>
    bool TryLookupByPrefix(string prefix, out IReadOnlyList<long> nodeOffsets)
    {
        nodeOffsets = System.Array.Empty<long>();
        var result = new System.Collections.Generic.List<long>();
        foreach (var (key, offsets) in EnumerateEntries())
        {
            if (key is string s && s.StartsWith(prefix, System.StringComparison.Ordinal))
                result.AddRange(offsets);
        }
        if (result.Count == 0) return false;
        nodeOffsets = result;
        return true;
    }
}
