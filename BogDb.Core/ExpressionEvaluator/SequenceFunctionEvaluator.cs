using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Handles auto-incrementing ID bindings natively aligned to Storage checkpoints.
/// For test bed and Phase 9 parity: `id()` scalar mapping execution.
/// </summary>
public static class SequenceFunctionEvaluator
{
    public static void NextVal(ValueVector result, ref long currentSequenceCounter)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            result.SetValue<long>(i, currentSequenceCounter++);
            result.SetNull(i, false);
        }
    }

    public static void CurrVal(ValueVector result, long currentSequenceCounter)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            result.SetValue<long>(i, currentSequenceCounter);
            result.SetNull(i, false);
        }
    }
}
