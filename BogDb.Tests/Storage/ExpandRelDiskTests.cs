using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Binder;
using BogDb.Core.Parser;
using BogDb.Core.Processor.Operator;
using ProcContext = BogDb.Core.Processor.ExecutionContext;

namespace BogDb.Tests.Storage;

public class ExpandRelDiskTests
{
    private static QueryNode MakeNode(string varName, string tableName)
        => new QueryNode(varName, varName, new List<string> { tableName },
            new List<PropertyExpression>(),
            new List<(string Key, Expression Value)>());

    [Fact]
    public void ExpandRel_Uses_Disk_Backend()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-expandrel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alice" });
                db.GraphLog.AppendNode("Person", 2L, new Dictionary<string, object> { ["name"] = "bob" });
                db.GraphLog.AppendRel("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2020 });
                var srcNode = MakeNode("a", "Person");
                var dstNode = MakeNode("b", "Person");
                var rel = new QueryRel("r", new List<string> { "KNOWS" }, new List<QueryRelConnection>(), ArrowDirection.RIGHT,
                    srcNode, dstNode,
                    new List<PropertyExpression>(),
                    new List<(string Key, Expression Value)>());

                var child = new BogDb.Core.Processor.Operator.Scan.ScanNodeProperty(db, "Person", "a", 1);
                var expand = new ExpandRel(db, rel, child, 2);
                var ctx = new ProcContext(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, db.BufferManager);

                // Should find at least one expanded neighbour
                Assert.True(expand.GetNextTuple(ctx));
                Assert.NotNull(ctx.CurrentNodeId);
                Assert.False(expand.GetNextTuple(ctx));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }
}
