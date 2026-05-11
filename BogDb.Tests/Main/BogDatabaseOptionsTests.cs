using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.Storage;
using BogDb.Core.Transaction;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class BogDatabaseOptionsTests
{
    [Fact]
    public void CreateInMemory_WithOptions_AppliesBufferManagerLimits()
    {
        var options = new BogDatabaseOptions()
            .WithBufferPoolSizeBytes(8 * 4096)
            .WithMaxMappedDatabaseSizeBytes(64 * 4096);

        using var db = BogDatabase.CreateInMemory(options);

        Assert.Equal(8 * 4096, db.Options.BufferPoolSizeBytes);
        Assert.Equal(64 * 4096, db.Options.MaxMappedDatabaseSizeBytes);
        Assert.Equal(8 * 4096, db.BufferManager.MemoryLimit);
    }

    [Fact]
    public void Open_WithOptions_ClonesOptionsForLiveDatabase()
    {
        var options = new BogDatabaseOptions()
            .WithBufferPoolSizeBytes(16 * 4096)
            .WithMaxMappedDatabaseSizeBytes(128 * 4096)
            .WithReadOnly()
            .WithReadCommittedRecoveryState();

        using var db = BogDatabase.Open(":memory:", options);
        options.WithBufferPoolSizeBytes(32 * 4096);
        options.WithReadOnly(false);

        Assert.Equal(16 * 4096, db.Options.BufferPoolSizeBytes);
        Assert.Equal(16 * 4096, db.BufferManager.MemoryLimit);
        Assert.True(db.Options.ReadOnly);
        Assert.True(db.Options.ReadCommittedRecoveryState);
    }

    [Fact]
    public void Open_ThrowsDatabaseBusyException_WhenDatabaseAlreadyOpen()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_db_open_lock_{Guid.NewGuid()}");
        Directory.CreateDirectory(dbPath);

        try
        {
            using var first = BogDatabase.Open(dbPath);

            var ex = Assert.Throws<DatabaseBusyException>(() => BogDatabase.Open(dbPath));

            Assert.Equal(dbPath, ex.DatabasePath);
            Assert.Contains("currently in use by another writer", ex.Message);
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                try { Directory.Delete(dbPath, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void Open_ReadOnly_AllowsConcurrentOpenWithWriter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_db_readonly_open_{Guid.NewGuid()}");

        try
        {
            using (var seedDb = BogDatabase.Open(dbPath))
            using (var seedConn = new BogConnection(seedDb))
            {
                seedConn.BeginWriteTransaction();
                seedConn.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    ["id"] = BogDb.Core.Common.LogicalTypeID.STRING,
                    ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
                });
                seedConn.UpsertNodeById("Person", "p1", new System.Collections.Generic.Dictionary<string, object>
                {
                    ["id"] = "p1",
                    ["name"] = "Ada"
                });
                seedConn.Commit();

                using var readOnlyDb = BogDatabase.Open(dbPath, new BogDatabaseOptions().WithReadOnly());
                using var readOnlyConn = new BogConnection(readOnlyDb);

                var result = readOnlyConn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.True(result.HasNext());
                var row = result.GetNext();
                Assert.Equal("p1", row.GetString(0));
                Assert.Equal("Ada", row.GetString(1));
                Assert.True(readOnlyDb.IsReadOnly);
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                try { Directory.Delete(dbPath, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void Open_ReadOnly_CanOptionallyIncludeCommittedRecoveryState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_db_readonly_recovery_{Guid.NewGuid()}");
        BogDatabase? db = null;

        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var committedTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var committedProps = new Dictionary<string, object>
            {
                ["id"] = "p2",
                ["name"] = "Grace"
            };
            db.NodeTables["Person"].Upsert(committedTx, "p2", committedProps);
            db.GraphLog.AppendNode("Person", "p2", committedProps);
            db.TransactionManager.Commit(conn.ClientContext, committedTx);

            SimulateCrashClose(db);
            db = null;

            using var snapshotDb = BogDatabase.Open(dbPath, new BogDatabaseOptions().WithReadOnly());
            using var snapshotConn = new BogConnection(snapshotDb);
            Assert.Null(snapshotConn.ReadNode("Person", "p2"));

            using var committedDb = BogDatabase.Open(
                dbPath,
                new BogDatabaseOptions().WithReadOnly().WithReadCommittedRecoveryState());
            using var committedConn = new BogConnection(committedDb);
            var recoveredNode = committedConn.ReadNode("Person", "p2");
            Assert.NotNull(recoveredNode);
            Assert.Equal("Grace", recoveredNode!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                try { Directory.Delete(dbPath, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Open_ReadOnly_InSeparateProcess_CanQueryWhileWriterProcessHoldsDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_db_process_readonly_{Guid.NewGuid()}");
        var signalDir = Path.Combine(Path.GetTempPath(), $"bogdb_db_process_signals_{Guid.NewGuid()}");
        Directory.CreateDirectory(signalDir);

        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object>
                {
                    ["id"] = "p1",
                    ["name"] = "Ada"
                });
                conn.Commit();
            }

            var writerReadyFile = Path.Combine(signalDir, "writer.ready");
            var writerReleaseFile = Path.Combine(signalDir, "writer.release");
            var readerResultFile = Path.Combine(signalDir, "reader-result.json");

            using var writer = StartProcessHost("writer-hold", dbPath, writerReadyFile, writerReleaseFile);
            await WaitForFileAsync(writerReadyFile);

            using var reader = StartProcessHost("readonly-query", dbPath, readerResultFile, bool.FalseString);
            await WaitForExitAsync(reader, 15000);
            Assert.Equal(0, reader.ExitCode);

            var payload = JsonSerializer.Deserialize<ProcessHostQueryResult>(
                await File.ReadAllTextAsync(readerResultFile));
            Assert.NotNull(payload);
            Assert.Equal(new[] { "id", "name" }, payload!.Columns);
            Assert.Single(payload.Rows);
            Assert.Equal("p1", payload.Rows[0]["id"]?.ToString());
            Assert.Equal("Ada", payload.Rows[0]["name"]?.ToString());

            await File.WriteAllTextAsync(writerReleaseFile, "release");
            await WaitForExitAsync(writer, 15000);
            Assert.Equal(0, writer.ExitCode);
        }
        finally
        {
            if (Directory.Exists(signalDir))
            {
                try { Directory.Delete(signalDir, recursive: true); } catch { }
            }

            if (Directory.Exists(dbPath))
            {
                try { Directory.Delete(dbPath, recursive: true); } catch { }
            }
        }
    }

    private static Process StartProcessHost(params string[] args)
    {
        var helperDllPath = GetProcessHostDllPath();
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "exec", helperDllPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi);
        Assert.NotNull(process);
        return process!;
    }

    private static string GetProcessHostDllPath()
    {
        var repoRoot = FindRepoRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var helperDllPath = Path.Combine(
            repoRoot,
            "BogDb.Tests.ProcessHost",
            "bin",
            configuration,
            "net9.0",
            "BogDb.Tests.ProcessHost.dll");

        Assert.True(File.Exists(helperDllPath), $"Process host not found at {helperDllPath}");
        return helperDllPath;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BogDb.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repo root (BogDb.slnx not found).");
    }

    private static async Task WaitForFileAsync(string path, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Timed out waiting for file '{path}'.");

            await Task.Delay(100);
        }
    }

    private static async Task WaitForExitAsync(Process process, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return;
        }
        catch (OperationCanceledException)
        {
        }

        try { process.Kill(entireProcessTree: true); } catch { }
        throw new TimeoutException($"Process '{process.ProcessName}' did not exit within {timeoutMs} ms.\nSTDOUT:\n{await process.StandardOutput.ReadToEndAsync()}\nSTDERR:\n{await process.StandardError.ReadToEndAsync()}");
    }

    private static void SimulateCrashClose(BogDatabase db)
    {
        var databaseType = typeof(BogDatabase);

        var graphLogProp = databaseType.GetProperty("GraphLog", BindingFlags.Instance | BindingFlags.NonPublic);
        (graphLogProp?.GetValue(db) as IDisposable)?.Dispose();

        var storageManagerProp = databaseType.GetProperty("StorageManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        (storageManagerProp?.GetValue(db) as IDisposable)?.Dispose();

        db.BufferManager.Dispose();
    }

    private sealed class ProcessHostQueryResult
    {
        public string[] Columns { get; set; } = Array.Empty<string>();
        public Dictionary<string, object?>[] Rows { get; set; } = Array.Empty<Dictionary<string, object?>>();
    }
}
