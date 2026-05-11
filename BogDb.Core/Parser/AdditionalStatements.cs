using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Parser;

/// <summary>
/// Represents CREATE SEQUENCE [IF NOT EXISTS] name [options].
/// </summary>
public sealed class CreateSequenceStatement : Statement
{
    public string SequenceName { get; }
    public bool IfNotExists { get; }
    public long IncrementBy { get; }
    public long? MinValue { get; }
    public long? MaxValue { get; }
    public long? StartWith { get; }
    public bool Cycle { get; }

    public CreateSequenceStatement(
        string sequenceName,
        bool ifNotExists = false,
        long incrementBy = 1,
        long? minValue = null,
        long? maxValue = null,
        long? startWith = null,
        bool cycle = false)
        : base(StatementType.CREATE_SEQUENCE)
    {
        SequenceName = sequenceName;
        IfNotExists = ifNotExists;
        IncrementBy = incrementBy;
        MinValue = minValue;
        MaxValue = maxValue;
        StartWith = startWith;
        Cycle = cycle;
    }
}

/// <summary>
/// Represents CREATE TYPE name AS dataType.
/// </summary>
public sealed class CreateTypeStatement : Statement
{
    public string TypeName { get; }
    public string DataType { get; }

    public CreateTypeStatement(string typeName, string dataType)
        : base(StatementType.CREATE_TYPE)
    {
        TypeName = typeName;
        DataType = dataType;
    }
}

/// <summary>
/// Represents COMMENT ON TABLE name IS 'description'.
/// </summary>
public sealed class CommentOnStatement : Statement
{
    public string TableName { get; }
    public string Comment { get; }

    public CommentOnStatement(string tableName, string comment)
        : base(StatementType.COMMENT_ON)
    {
        TableName = tableName;
        Comment = comment;
    }
}

/// <summary>
/// Represents EXPORT DATABASE 'path' [(options)].
/// </summary>
public sealed class ExportDatabaseStatement : Statement
{
    public string ExportPath { get; }
    public IReadOnlyDictionary<string, object?> Options { get; }

    public ExportDatabaseStatement(string exportPath, IReadOnlyDictionary<string, object?>? options = null)
        : base(StatementType.EXPORT_DATABASE)
    {
        ExportPath = exportPath;
        Options = options ?? new Dictionary<string, object?>();
    }
}

/// <summary>
/// Represents IMPORT DATABASE 'path'.
/// </summary>
public sealed class ImportDatabaseStatement : Statement
{
    public string ImportPath { get; }

    public ImportDatabaseStatement(string importPath)
        : base(StatementType.IMPORT_DATABASE)
    {
        ImportPath = importPath;
    }
}
