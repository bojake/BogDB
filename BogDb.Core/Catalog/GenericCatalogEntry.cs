using System.IO;

namespace BogDb.Core.Catalog;

/// <summary>
/// Generic fallback entry for catalog entry types that currently do not have
/// dedicated C# classes. This preserves deserialization progress and metadata.
/// </summary>
public sealed class GenericCatalogEntry : CatalogEntry
{
    public GenericCatalogEntry(CatalogEntryType type, string name) : base(type, name) {}

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write(Name);
        writer.Write(OID);
        writer.Write(Timestamp);
        writer.Write(IsDeleted);
        writer.Write(HasParent);
    }

    public static GenericCatalogEntry Deserialize(CatalogEntryType type, BinaryReader reader)
    {
        var name = reader.ReadString();
        var oid = reader.ReadUInt64();
        var timestamp = reader.ReadUInt64();
        var isDeleted = reader.ReadBoolean();
        var hasParent = reader.ReadBoolean();

        var entry = new GenericCatalogEntry(type, name);
        entry.SetOID(oid);
        entry.SetTimestamp(timestamp);
        entry.SetDeleted(isDeleted);
        entry.SetHasParent(hasParent);
        return entry;
    }
}
