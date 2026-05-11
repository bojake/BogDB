using BogDb.Core.Catalog;
using BogDb.Core.Common;
using System.Threading.Tasks;
using Xunit;

namespace BogDb.Tests.Catalog;

public class CatalogTests
{
    [Fact]
    public void CatalogSet_ShouldGenerateUniqueOIDs()
    {
        using var set = new CatalogSet();
        
        var id1 = set.GetNextOID();
        var id2 = set.GetNextOID();
        
        Assert.Equal(0ul, id1);
        Assert.Equal(1ul, id2);
    }

    [Fact]
    public void CatalogSet_InternalCatalogs_ShouldStartAtHighOID()
    {
        using var internalSet = new CatalogSet(isInternal: true);
        var id = internalSet.GetNextOID();
        
        Assert.True(id >= CatalogSet.INTERNAL_CATALOG_SET_START_OID);
    }
    
    [Fact]
    public void Catalog_ShouldAddNewTables()
    {
        using var catalog = new BogDb.Core.Catalog.Catalog();
        var tx = new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1, 10);
        
        var nodeTable = new NodeTableCatalogEntry("Person", 0);
        
        catalog.CreateTableEntry(tx, nodeTable);

        Assert.True(catalog.ContainsTable(tx, "Person"));
        
        var retrieved = catalog.GetTableCatalogEntry(tx, "Person");
        Assert.NotNull(retrieved);
        Assert.Equal("Person", retrieved.Name);
        Assert.Equal(TableType.NODE, retrieved.GetTableType());
    }

    [Fact]
    public void CatalogSet_ThreadSafety_ShouldNotThrowExceptionsOnConcurrentAccess()
    {
        using var set = new CatalogSet();
        var tx = new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1, 10);

        Parallel.For(0, 1000, i => 
        {
            var node = new NodeTableCatalogEntry($"Table_{i}", 0);
            set.CreateEntry(tx, node);
        });

        // 1000 successfully written items inside the ReaderWriterLockSlim bounds
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(set.ContainsEntry(tx, $"Table_{i}"));
        }
    }

    [Fact]
    public void Catalog_IndexEntries_RoundTripStructuredMetadata_AndRemainAddressableByTableProperty()
    {
        using var catalog = new BogDb.Core.Catalog.Catalog();
        var tx = new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1, 10);

        catalog.CreateIndexEntry("Person", "name", propertyType: LogicalTypeID.STRING);
        Assert.True(catalog.ContainsIndexEntry("Person", "name"));

        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            catalog.Serialize(writer);
        }

        ms.Position = 0;
        using var reader = new System.IO.BinaryReader(ms);
        using var restored = BogDb.Core.Catalog.Catalog.Deserialize(reader);

        var indexEntry = Assert.Single(restored.GetIndexEntries());
        Assert.Equal("Person", indexEntry.TableName);
        Assert.Equal("name", indexEntry.PropertyName);
        Assert.Equal("HASH", indexEntry.IndexTypeName);
        Assert.Equal(LogicalTypeID.STRING, indexEntry.PropertyType);
        Assert.True(restored.ContainsIndexEntry("Person", "name"));

        restored.RenameIndexEntry(tx, "Person", "name", "Human", "display_name");
        Assert.False(restored.ContainsIndexEntry("Person", "name"));
        Assert.True(restored.ContainsIndexEntry("Human", "display_name"));
        var renamed = Assert.Single(restored.GetIndexEntries());
        Assert.Equal("Human", renamed.TableName);
        Assert.Equal("display_name", renamed.PropertyName);
        Assert.Equal(LogicalTypeID.STRING, renamed.PropertyType);
    }
}
