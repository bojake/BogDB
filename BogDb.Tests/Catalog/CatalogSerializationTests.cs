using System;
using System.IO;
using Xunit;
using BogDb.Core.Catalog;
using BogDb.Core.Storage;
using BogDb.Core.Transaction;

namespace BogDb.Tests.Catalog;

public class CatalogSerializationTests : IDisposable
{
    private readonly string _tempDbDir;

    public CatalogSerializationTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), "BogDbCatalogTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDbDir);
    }

    [Fact]
    public void Catalog_SerializesToStorage_AndRecoversSuccessfully()
    {
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDbDir))
        {
            try { Directory.Delete(_tempDbDir, true); } catch { /* Ignore file-in-use Windows teardown locks */ }
        }
    }

    [Fact]
    public void Catalog_SerializesLargeBoundary_Successfully()
    {
    }
}
