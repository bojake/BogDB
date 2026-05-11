using System;
using System.IO;
using BogDb.Core.Common;

namespace BogDb.Core.Catalog;

/// <summary>
/// Structured catalog entry for persisted indexes.
/// The serialized payload remains backward-compatible with the historical
/// GenericCatalogEntry INDEX_ENTRY shape by storing structured metadata in the
/// entry name. Older two-segment names still deserialize correctly.
/// </summary>
public sealed class IndexCatalogEntry : CatalogEntry
{
    private const char SegmentSeparator = '\u001F';

    public string TableName { get; private set; }
    public string PropertyName { get; private set; }
    public string IndexTypeName { get; private set; }
    public LogicalTypeID? PropertyType { get; private set; }

    public IndexCatalogEntry(
        string tableName,
        string propertyName,
        string indexTypeName = "HASH",
        LogicalTypeID? propertyType = null)
        : base(CatalogEntryType.INDEX_ENTRY,
            EncodeName(tableName, propertyName, indexTypeName, propertyType))
    {
        TableName = tableName;
        PropertyName = propertyName;
        IndexTypeName = indexTypeName;
        PropertyType = propertyType;
    }

    public static string EncodeName(
        string tableName,
        string propertyName,
        string indexTypeName = "HASH",
        LogicalTypeID? propertyType = null)
    {
        var encoded = $"{tableName}{SegmentSeparator}{propertyName}";
        if (!string.IsNullOrWhiteSpace(indexTypeName) || propertyType.HasValue)
            encoded += $"{SegmentSeparator}{indexTypeName}";
        if (propertyType.HasValue)
            encoded += $"{SegmentSeparator}{(byte)propertyType.Value}";
        return encoded;
    }

    public static bool TryDecodeName(
        string name,
        out string tableName,
        out string propertyName,
        out string indexTypeName,
        out LogicalTypeID? propertyType)
    {
        tableName = string.Empty;
        propertyName = string.Empty;
        indexTypeName = "HASH";
        propertyType = null;

        var segments = name.Split(SegmentSeparator);
        if (segments.Length < 2 ||
            string.IsNullOrWhiteSpace(segments[0]) ||
            string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        tableName = segments[0];
        propertyName = segments[1];

        if (segments.Length >= 3 && !string.IsNullOrWhiteSpace(segments[2]))
            indexTypeName = segments[2];

        if (segments.Length >= 4 &&
            byte.TryParse(segments[3], out var propertyTypeByte) &&
            Enum.IsDefined(typeof(LogicalTypeID), propertyTypeByte))
        {
            propertyType = (LogicalTypeID)propertyTypeByte;
        }

        return true;
    }

    public override void Rename(string newName)
    {
        base.Rename(newName);
        if (!TryDecodeName(newName, out var tableName, out var propertyName, out var indexTypeName,
                out var propertyType))
        {
            throw new InvalidDataException($"Malformed index catalog entry name: {newName}");
        }

        TableName = tableName;
        PropertyName = propertyName;
        IndexTypeName = indexTypeName;
        PropertyType = propertyType;
    }

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write(Name);
        writer.Write(OID);
        writer.Write(Timestamp);
        writer.Write(IsDeleted);
        writer.Write(HasParent);
    }

    public static IndexCatalogEntry Deserialize(BinaryReader reader)
    {
        var name = reader.ReadString();
        var oid = reader.ReadUInt64();
        var timestamp = reader.ReadUInt64();
        var isDeleted = reader.ReadBoolean();
        var hasParent = reader.ReadBoolean();

        if (!TryDecodeName(name, out var tableName, out var propertyName, out var indexTypeName,
                out var propertyType))
        {
            throw new InvalidDataException($"Malformed persisted index catalog entry name: {name}");
        }

        var entry = new IndexCatalogEntry(tableName, propertyName, indexTypeName, propertyType);
        entry.SetOID(oid);
        entry.SetTimestamp(timestamp);
        entry.SetDeleted(isDeleted);
        entry.SetHasParent(hasParent);
        return entry;
    }
}
