using BogDb.Core.Common;

namespace BogDb.Core.Catalog;

public sealed class ForeignTableCatalogEntry : TableCatalogEntry
{
    public ForeignTableCatalogEntry(string name)
        : base(CatalogEntryType.FOREIGN_TABLE_ENTRY, name) {}

    public override TableType GetTableType() => TableType.FOREIGN;

    public override TableCatalogEntry Copy()
    {
        var copy = new ForeignTableCatalogEntry(Name);
        copy.SetOID(OID);
        copy.SetTimestamp(Timestamp);
        copy.SetDeleted(IsDeleted);
        copy.SetHasParent(HasParent);
        copy.SetComment(_comment);
        foreach (var property in GetProperties())
        {
            copy.AddProperty(new PropertyDefinition(
                new ColumnDefinition(property.ColumnDef.Name, property.ColumnDef.Type, property.ColumnDef.DeclaredType),
                property.DefaultExpressionName));
        }
        return copy;
    }

    public override void Serialize(System.IO.BinaryWriter writer)
    {
        base.Serialize(writer);
    }

    public new static ForeignTableCatalogEntry Deserialize(System.IO.BinaryReader reader)
    {
        // 1. Base Class Fields (written in TableCatalogEntry.Serialize after the Type byte)
        string name = reader.ReadString();
        ulong oid = reader.ReadUInt64();
        ulong timestamp = reader.ReadUInt64();
        bool isDeleted = reader.ReadBoolean();
        bool hasParent = reader.ReadBoolean();
        string comment = reader.ReadString();
        int numProperties = reader.ReadInt32();

        var entry = new ForeignTableCatalogEntry(name);
        entry.SetOID(oid);
        entry.SetTimestamp(timestamp);
        entry.SetDeleted(isDeleted);
        entry.SetHasParent(hasParent);
        entry.SetComment(comment);
        DeserializeProperties(reader, entry, numProperties);
        return entry;
    }
}
