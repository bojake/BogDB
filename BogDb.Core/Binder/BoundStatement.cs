using BogDb.Core.Common;

namespace BogDb.Core.Binder;

public readonly record struct BoundColumnDefinition(string Name, LogicalTypeID Type, string DeclaredType);

/// <summary>
/// Base class for all bound (semantically validated) statements.
/// Mirrors C++ BoundStatement from binder/bound_statement.h
/// </summary>
public abstract class BoundStatement
{
    public StatementType StatementType { get; }

    protected BoundStatement(StatementType statementType)
    {
        StatementType = statementType;
    }
}

public class BoundCreateTableInfo
{
    public string TableName { get; }
    public IReadOnlyList<BoundColumnDefinition> Properties { get; }

    public BoundCreateTableInfo(string tableName, List<BoundColumnDefinition> properties)
    {
        TableName = tableName;
        Properties = properties;
    }
}

public abstract class BoundCreateTableBase : BoundStatement
{
    public BoundCreateTableInfo Info { get; }

    protected BoundCreateTableBase(BoundCreateTableInfo info) : base(StatementType.CREATE_TABLE)
    {
        Info = info;
    }
}

public class BoundCreateNodeTable : BoundCreateTableBase
{
    public BoundCreateNodeTable(BoundCreateTableInfo info) : base(info) {}
}

public class BoundCreateRelTable : BoundCreateTableBase
{
    public ulong SrcTableId { get; }
    public ulong DstTableId { get; }

    public BoundCreateRelTable(BoundCreateTableInfo info, ulong srcTableId, ulong dstTableId) : base(info)
    {
        SrcTableId = srcTableId;
        DstTableId = dstTableId;
    }
}

public class BoundDropTable : BoundStatement
{
    public ulong TableId { get; }

    public BoundDropTable(ulong tableId) : base(StatementType.DROP)
    {
        TableId = tableId;
    }
}

public class BoundAlterTable : BoundStatement
{
    public ulong TableId { get; }

    public BoundAlterTable(ulong tableId) : base(StatementType.ALTER)
    {
        TableId = tableId;
    }
}

public sealed class BoundCreateMacro : BoundStatement
{
    public string Name { get; }
    public IReadOnlyList<Parser.MacroParameter> Parameters { get; }
    public Parser.ParsedExpression BodyExpression { get; }

    public BoundCreateMacro(string name, IReadOnlyList<Parser.MacroParameter> parameters, Parser.ParsedExpression bodyExpression)
        : base(StatementType.CREATE_MACRO)
    {
        Name = name;
        Parameters = parameters;
        BodyExpression = bodyExpression;
    }
}

public sealed class BoundAttachDatabase : BoundStatement
{
    public string Path { get; }
    public string Alias { get; }
    public string DbType { get; }
    public IReadOnlyDictionary<string, object?> Options { get; }

    public BoundAttachDatabase(string path, string alias, string dbType, IReadOnlyDictionary<string, object?> options)
        : base(StatementType.ATTACH_DATABASE)
    {
        Path = path;
        Alias = alias;
        DbType = dbType;
        Options = options;
    }
}

public sealed class BoundDetachDatabase : BoundStatement
{
    public string DbName { get; }

    public BoundDetachDatabase(string dbName)
        : base(StatementType.DETACH_DATABASE)
    {
        DbName = dbName;
    }
}

public sealed class BoundUseDatabase : BoundStatement
{
    public string DbName { get; }

    public BoundUseDatabase(string dbName)
        : base(StatementType.USE_DATABASE)
    {
        DbName = dbName;
    }
}
