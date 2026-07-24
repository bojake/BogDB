using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BogDb.Core.Main;
using BogDb.Core.Storage;
using Xunit;

namespace BogDb.Tests.Storage;

/// <summary>
/// Graph-log record-type coverage for the recovery reader.
///
/// Record types written by <c>GraphLogWriter</c>: 1 = node upsert, 2 = rel upsert, 3 = node delete,
/// 4 = rel delete, 5 = rel insert (written by relationship MERGE via <c>AppendRelInsert</c>).
/// Types 2 and 5 share a wire shape (id2 followed by properties).
///
/// Regression: <c>GraphStore</c>'s reader recognized only types 1–4 and threw
/// "Unknown graph log record type: 5" — so any graph log containing a MERGE'd relationship aborted
/// recovery, while its rel enumeration silently dropped the edge.
/// </summary>
public class GraphLogRecordTypeTests
{
    private const byte RelUpsert = 2;
    private const byte RelDelete = 4;
    private const byte RelInsert = 5;   // written by relationship MERGE

    private static void WriteRecord(BinaryWriter w, byte recordType, string table, object from, object to,
        Dictionary<string, object>? props)
    {
        w.Write(recordType);
        w.Write(table);
        GraphDataSerializer.WriteValue(w, from);
        if (recordType is 2 or 4 or 5)
            GraphDataSerializer.WriteValue(w, to);
        if (recordType is 1 or 2 or 5)
            GraphDataSerializer.WriteProperties(w, props ?? new Dictionary<string, object>());
    }

    private static string NewLogDir(Action<BinaryWriter> write)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bogdb-graphlog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        using var fs = new FileStream(Path.Combine(dir, "graph-log.bin"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var w = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);
        write(w);
        return dir;
    }

    [Fact]
    public void RelationshipInsertRecord_FromMerge_IsReadAndSurfacedByGraphStore()
    {
        var dir = NewLogDir(w => WriteRecord(w, RelInsert, "K", 1L, 2L, new Dictionary<string, object> { ["w"] = 7L }));
        try
        {
            var rels = new GraphStore(dir, inMemory: false).EnumerateRels("K").ToList();
            Assert.Single(rels);
            Assert.Equal(1L, Convert.ToInt64(rels[0].Key.From));
            Assert.Equal(2L, Convert.ToInt64(rels[0].Key.To));
            Assert.Equal(7L, Convert.ToInt64(rels[0].Value["w"]));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void RelationshipInsertRecord_ParsesWithoutDesyncingFollowingRecords()
    {
        // A type-5 record must consume exactly its own bytes; otherwise the next record is misparsed.
        // Here a later delete of the same edge must still be understood, removing it from the result.
        var dir = NewLogDir(w =>
        {
            WriteRecord(w, RelInsert, "K", 1L, 2L, new Dictionary<string, object> { ["w"] = 7L });
            WriteRecord(w, RelUpsert, "K", 3L, 4L, new Dictionary<string, object> { ["w"] = 9L });
            WriteRecord(w, RelDelete, "K", 1L, 2L, null);
        });
        try
        {
            var rels = new GraphStore(dir, inMemory: false).EnumerateRels("K").ToList();
            Assert.Single(rels);                                       // 1->2 was inserted then deleted
            Assert.Equal(3L, Convert.ToInt64(rels[0].Key.From));
            Assert.Equal(4L, Convert.ToInt64(rels[0].Key.To));
            Assert.Equal(9L, Convert.ToInt64(rels[0].Value["w"]));
        }
        finally { TryDelete(dir); }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
