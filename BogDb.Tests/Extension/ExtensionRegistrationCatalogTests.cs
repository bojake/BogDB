using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;
using BogDb.Core.Common.FileSystem;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Extension;

public class ExtensionRegistrationCatalogTests
{
    private sealed class DummyStandaloneTableFunction : ITableFunction
    {
        public string Name => "dummy_scan";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)> { ("value", "STRING") };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            yield return new Dictionary<string, object?> { ["value"] = "ok" };
        }
    }

    private sealed class DummyFileSystem : VirtualFileSystem
    {
        public override BogDb.Core.Common.FileSystem.FileInfo OpenFile(string path, FileFlags flags) =>
            throw new System.NotSupportedException();

        public override void CreateDirectory(string path) =>
            throw new System.NotSupportedException();

        public override void RemoveFileIfExists(string path) =>
            throw new System.NotSupportedException();

        public override bool FileExists(string path) => false;

        public override IReadOnlyList<string> GetPathsInDirectory(string path) =>
            System.Array.Empty<string>();

        public override string JoinPath(string baseDir, string name) => $"{baseDir}/{name}";
    }

    private sealed class DummyStorageExtension : IStorageExtension
    {
        public string Name => "dummy_storage";

        public bool CanHandle(string dbType) =>
            string.Equals(dbType, Name, System.StringComparison.OrdinalIgnoreCase);

        public AttachedDatabaseHandle Attach(
            BogDatabase database,
            string alias,
            string path,
            IReadOnlyDictionary<string, object?> options)
        {
            var tables = new Dictionary<string, AttachedTableInfo>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["items"] = new(
                    "items",
                    new[] { ("value", "STRING") }.ToList())
            };

            return new AttachedDatabaseHandle(alias, Name, path, Name, tables);
        }

        public IEnumerable<Dictionary<string, object?>> Scan(
            AttachedDatabaseHandle attachedDatabase,
            string? tableName)
        {
            yield return new Dictionary<string, object?> { ["value"] = "ok" };
        }
    }

    [Fact]
    public void DatabaseOwnsStandaloneAndScalarExtensionRegistries()
    {
        using var db = BogDatabase.CreateInMemory();

        db.StandaloneTableFunctionRegistry.Register(new DummyStandaloneTableFunction());
        db.ScalarFunctionRegistry.Register("dummy_scalar", args => $"hello:{args.Length}");

        Assert.True(db.StandaloneTableFunctionRegistry.Contains("dummy_scan"));
        Assert.True(db.ScalarFunctionRegistry.Contains("dummy_scalar"));
        Assert.True(db.StandaloneTableFunctionRegistry.TryGet("dummy_scan", out var tf));
        Assert.True(db.ScalarFunctionRegistry.TryGet("dummy_scalar", out var sf));
        Assert.Equal("dummy_scan", tf.Name);
        Assert.Equal("hello:2", sf(new object?[] { 1L, 2L }));
    }

    [Fact]
    public void DatabaseRegistersFileSystemsStorageExtensionsAndOptions()
    {
        using var db = BogDatabase.CreateInMemory();
        var fs = new DummyFileSystem();
        var storage = new DummyStorageExtension();

        db.RegisterFileSystem("http", fs);
        db.RegisterStorageExtension(storage.Name, storage);
        db.AddExtensionOption("http_timeout", LogicalTypeID.INT64, 30L);
        db.SetExtensionOption("http_timeout", 45L);

        Assert.True(db.TryGetFileSystem("http", out var resolvedFs));
        Assert.Same(fs, resolvedFs);
        Assert.True(db.TryGetStorageExtension("dummy_storage", out var resolvedStorage));
        Assert.Same(storage, resolvedStorage);
        Assert.True(db.TryGetExtensionOption("http_timeout", out var option));
        Assert.Equal(LogicalTypeID.INT64, option.Type);
        Assert.Equal(30L, option.DefaultValue);
        Assert.Equal(45L, db.GetExtensionOptionValue("http_timeout"));
    }
}
