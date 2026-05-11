namespace BogDb.Core.Extraction;

/// <summary>
/// Raised when an extracted graph payload violates the transport/runtime contract.
/// </summary>
public sealed class GraphShardValidationException : InvalidOperationException
{
    public GraphShardValidationException(string message)
        : base(message)
    {
    }
}
