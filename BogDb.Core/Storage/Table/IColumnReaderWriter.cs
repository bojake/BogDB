namespace BogDb.Core.Storage.Table;

/// <summary>
/// Read-only column access interface.
/// C++ parity: column_reader_writer.h (read path)
/// Separates the read path from the write path for streaming I/O,
/// compression-aware reads, and future page-level lazy evaluation.
/// </summary>
public interface IColumnReader
{
    string Name { get; }
    long Count { get; }

    /// <summary>
    /// Read a single value at the given row offset.
    /// </summary>
    object? Lookup(long rowOffset);

    /// <summary>
    /// MVCC-aware read: returns the value visible to the transaction.
    /// </summary>
    object? Lookup(Transaction.Transaction tx, long rowOffset);

    /// <summary>
    /// Read committed value visible at the given commit version.
    /// </summary>
    object? LookupCommitted(ulong visibleCommitVersion, long rowOffset);

    /// <summary>
    /// Streaming scan: yields values lazily from startOffset for numValues rows.
    /// </summary>
    IEnumerable<object?> Scan(long startOffset, long numValues);

    /// <summary>
    /// MVCC-aware streaming scan.
    /// </summary>
    IEnumerable<object?> Scan(Transaction.Transaction tx, long startOffset, long numValues);
}

/// <summary>
/// Write-only column access interface.
/// C++ parity: column_reader_writer.h (write path)
/// </summary>
public interface IColumnWriter
{
    /// <summary>
    /// Append a value to the end of the column.
    /// </summary>
    void Append(object? value);

    /// <summary>
    /// Overwrite a value at the given row offset (non-transactional).
    /// </summary>
    void Update(long rowOffset, object? value);

    /// <summary>
    /// MVCC-aware write: records a versioned update visible only to the transaction.
    /// </summary>
    void Update(Transaction.Transaction tx, long rowOffset, object? value);

    /// <summary>
    /// Truncate the column to the given count.
    /// </summary>
    void Truncate(long newCount);
}
