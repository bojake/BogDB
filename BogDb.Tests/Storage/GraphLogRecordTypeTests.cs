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
/// recovery, while its rel enumeration silently dropped the edge. The log now has one shared parser
/// (<c>GraphLogFormat</c>); <see cref="EveryDefinedRecordType_RoundTripsAndApplies"/> is the guard that
/// catches the same class of drift for any record type added later.
///
/// The byte layout below is written out by hand on purpose: it is an independent oracle for the
/// on-disk format, so a change to <c>GraphLogFormat.WriteRecord</c> cannot quietly redefine it.
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

    /// <summary>
    /// Every record type the enum defines must survive write → read → apply. This is the anti-drift
    /// guard: adding a member to <c>GraphLogRecordType</c> without giving it shape rules
    /// (<c>HasSecondId</c>/<c>HasProperties</c>) or an apply rule fails here rather than at a user's
    /// recovery. The exact-consumption assert is what catches a wrong shape — a record that reads too
    /// few or too many bytes desyncs everything after it.
    /// </summary>
    [Fact]
    public void EveryDefinedRecordType_RoundTripsAndApplies()
    {
        foreach (var type in Enum.GetValues<GraphLogRecordType>())
        {
            var props = new Dictionary<string, object> { ["p"] = 42L };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                GraphLogFormat.WriteRecord(w, type, "T", 1L, 2L, props);

            ms.Position = 0;
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            Assert.True(GraphLogFormat.TryReadRecord(r, out var record), $"{type} did not read back");

            Assert.Equal(type, record.Type);
            Assert.Equal("T", record.TableName);
            Assert.Equal(1L, Convert.ToInt64(record.Id));
            Assert.Equal(ms.Length, ms.Position);   // consumed exactly its own bytes — no desync

            if (GraphLogFormat.HasSecondId(type))
                Assert.Equal(2L, Convert.ToInt64(record.Id2));
            else
                Assert.Null(record.Id2);

            if (GraphLogFormat.HasProperties(type))
                Assert.Equal(42L, Convert.ToInt64(record.Properties["p"]));

            // Must have an apply rule — GraphLogFormat.Apply throws for a type it does not handle.
            GraphLogFormat.Apply(record,
                new Dictionary<string, NodeTableData>(),
                new Dictionary<string, RelTableData>());
        }
    }

    [Fact]
    public void UnknownRecordType_ThrowsRatherThanMisparsingTheRest()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)99);
            w.Write("T");
            GraphDataSerializer.WriteValue(w, 1L);
        }

        ms.Position = 0;
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() => GraphLogFormat.TryReadRecord(r, out _));
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
