namespace BogDb.Core.Main;

/// <summary>
/// C#-native database open options for embedded hosts.
/// Keep this surface limited to options the current runtime can honor end to end.
/// </summary>
public sealed class BogDatabaseOptions
{
    public const long DefaultBufferPoolSizeBytes = 268435456;
    public const long DefaultMaxMappedDatabaseSizeBytes = 1073741824;

    public long BufferPoolSizeBytes { get; private set; } = DefaultBufferPoolSizeBytes;
    public long MaxMappedDatabaseSizeBytes { get; private set; } = DefaultMaxMappedDatabaseSizeBytes;
    public bool ReadOnly { get; private set; }
    public bool ReadCommittedRecoveryState { get; private set; }

    public BogDatabaseOptions WithBufferPoolSizeBytes(long bytes)
    {
        if (bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));

        BufferPoolSizeBytes = bytes;
        return this;
    }

    public BogDatabaseOptions WithMaxMappedDatabaseSizeBytes(long bytes)
    {
        if (bytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));

        MaxMappedDatabaseSizeBytes = bytes;
        return this;
    }

    public BogDatabaseOptions WithReadOnly(bool readOnly = true)
    {
        ReadOnly = readOnly;
        return this;
    }

    public BogDatabaseOptions WithReadCommittedRecoveryState(bool readCommittedRecoveryState = true)
    {
        ReadCommittedRecoveryState = readCommittedRecoveryState;
        return this;
    }

    internal BogDatabaseOptions Clone()
        => new BogDatabaseOptions()
            .WithBufferPoolSizeBytes(BufferPoolSizeBytes)
            .WithMaxMappedDatabaseSizeBytes(MaxMappedDatabaseSizeBytes)
            .WithReadOnly(ReadOnly)
            .WithReadCommittedRecoveryState(ReadCommittedRecoveryState);
}
