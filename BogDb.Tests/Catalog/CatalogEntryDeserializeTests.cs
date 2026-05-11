using System;
using System.IO;
using Xunit;
using BogDb.Core.Catalog;
using BogDb.Core.Common;

namespace BogDb.Tests.Catalog;

public class CatalogEntryDeserializeTests
{
    [Fact]
    public void CatalogEntry_Deserialize_ForeignTableEntry_Succeeds()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CatalogEntryType.FOREIGN_TABLE_ENTRY);
            writer.Write("ForeignPeople");
            writer.Write((ulong)42);
            writer.Write((ulong)1001);
            writer.Write(false);
            writer.Write(false);
            writer.Write("foreign comment");
            writer.Write(0); // stubbed property count
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var entry = CatalogEntry.Deserialize(reader);

        var foreign = Assert.IsType<ForeignTableCatalogEntry>(entry);
        Assert.Equal("ForeignPeople", foreign.Name);
        Assert.Equal((ulong)42, foreign.OID);
        Assert.Equal(TableType.FOREIGN, foreign.GetTableType());
    }

    [Fact]
    public void CatalogEntry_Deserialize_GenericFunctionEntry_Succeeds()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CatalogEntryType.SCALAR_FUNCTION_ENTRY);
            writer.Write("toUpper");
            writer.Write((ulong)9);
            writer.Write((ulong)2002);
            writer.Write(false);
            writer.Write(false);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var entry = CatalogEntry.Deserialize(reader);

        var generic = Assert.IsType<GenericCatalogEntry>(entry);
        Assert.Equal(CatalogEntryType.SCALAR_FUNCTION_ENTRY, generic.Type);
        Assert.Equal("toUpper", generic.Name);
        Assert.Equal((ulong)9, generic.OID);
    }

    [Fact]
    public void CatalogEntry_Deserialize_IndexEntry_PreservesStructuredMetadata()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CatalogEntryType.INDEX_ENTRY);
            writer.Write(IndexCatalogEntry.EncodeName("Person", "name", "HASH", LogicalTypeID.STRING));
            writer.Write((ulong)12);
            writer.Write((ulong)3003);
            writer.Write(false);
            writer.Write(false);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var entry = CatalogEntry.Deserialize(reader);

        var index = Assert.IsType<IndexCatalogEntry>(entry);
        Assert.Equal("Person", index.TableName);
        Assert.Equal("name", index.PropertyName);
        Assert.Equal("HASH", index.IndexTypeName);
        Assert.Equal(LogicalTypeID.STRING, index.PropertyType);
    }

    [Fact]
    public void CatalogEntry_Deserialize_LegacyIndexEntryName_RemainsReadable()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CatalogEntryType.INDEX_ENTRY);
            writer.Write("Person\u001Fname");
            writer.Write((ulong)13);
            writer.Write((ulong)3004);
            writer.Write(false);
            writer.Write(false);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var entry = CatalogEntry.Deserialize(reader);

        var index = Assert.IsType<IndexCatalogEntry>(entry);
        Assert.Equal("Person", index.TableName);
        Assert.Equal("name", index.PropertyName);
        Assert.Equal("HASH", index.IndexTypeName);
        Assert.Null(index.PropertyType);
    }

    [Fact]
    public void CatalogEntry_Deserialize_UnknownType_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)250); // Unknown type
            writer.Write("x");
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.Throws<InvalidDataException>(() => CatalogEntry.Deserialize(reader));
    }

    [Fact]
    public void CatalogEntry_Deserialize_NodeTableEntry_RestoresPropertyDefinitions()
    {
        var source = new NodeTableCatalogEntry("Person", 0);
        source.SetOID(7);
        source.SetTimestamp(99);
        source.SetComment("node table");
        source.AddProperty(new PropertyDefinition(new ColumnDefinition("id", LogicalTypeID.INT64)));
        source.AddProperty(new PropertyDefinition(new ColumnDefinition("embedding", LogicalTypeID.LIST, "FLOAT[]")));

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            source.Serialize(writer);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var entry = CatalogEntry.Deserialize(reader);

        var node = Assert.IsType<NodeTableCatalogEntry>(entry);
        Assert.Equal("Person", node.Name);
        Assert.Equal(2, node.GetNumProperties());
        Assert.Equal(LogicalTypeID.INT64, node.GetProperty("id").Type);
        Assert.Equal(LogicalTypeID.LIST, node.GetProperty("embedding").Type);
        Assert.Equal("FLOAT[]", node.GetProperty("embedding").DeclaredType);
    }
}
