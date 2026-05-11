// This file retains the OrderByDirection enum for C++ header parity.
// The actual OrderBy physical operator is in BogDb.Core.Processor.Operator.OrderBy.
// Do NOT create an OrderBy class here — use OrderBy.OrderBy (the other namespace).

namespace BogDb.Core.Processor.Operator.Order;

/// <summary>
/// Sort direction qualifier used by the ORDER BY physical operator.
/// C++ parity: order_by_key_encoder.h
/// </summary>
public enum OrderByDirection : byte
{
    ASC  = 0,
    DESC = 1,
}
