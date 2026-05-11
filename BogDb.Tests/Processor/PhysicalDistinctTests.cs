using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Processor.Operator;
using BogDb.Core.Processor.Operator.Distinct;
using Xunit;

namespace BogDb.Tests.Processor;

public sealed class PhysicalDistinctTests
{
    private sealed class MockScanOperator : PhysicalOperator
    {
        private readonly List<object?[]> _rows;
        private int _position;

        public MockScanOperator(IEnumerable<object?[]> rows)
            : base(PhysicalOperatorType.SCAN_NODE_ID, 0)
        {
            _rows = rows.ToList();
        }

        public override bool GetNextTuple(BogDb.Core.Processor.ExecutionContext context)
        {
            if (_position >= _rows.Count)
                return false;

            context.CurrentProjectionRow = (object?[])_rows[_position].Clone();
            _position++;
            return true;
        }
    }

    [Fact]
    public void PhysicalDistinct_UsesDedicatedPhysicalOperatorType()
    {
        var op = new PhysicalDistinct(new MockScanOperator(new[] { new object?[] { 1L } }), 7);
        Assert.Equal(PhysicalOperatorType.DISTINCT, op.OperatorType);
    }

    [Fact]
    public void PhysicalDistinct_DoesNotCollapseTypeDistinctValues_WithSameText()
    {
        var child = new MockScanOperator(new[]
        {
            new object?[] { 1L },
            new object?[] { "1" },
            new object?[] { 1L }
        });
        var distinct = new PhysicalDistinct(child, 1);
        var context = new BogDb.Core.Processor.ExecutionContext(null, null);

        var rows = new List<object?[]>();
        while (distinct.GetNextTuple(context))
            rows.Add((object?[])context.CurrentProjectionRow!.Clone());

        Assert.Equal(2, rows.Count);
        Assert.IsType<long>(rows[0][0]!);
        Assert.IsType<string>(rows[1][0]!);
        Assert.Equal(1L, rows[0][0]);
        Assert.Equal("1", rows[1][0]);
    }
}
