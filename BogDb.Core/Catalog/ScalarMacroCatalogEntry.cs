using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antlr4.Runtime;
using BogDb.Core.Parser;
using BogDb.Core.Parser.Antlr4;

namespace BogDb.Core.Catalog;

public sealed class ScalarMacroCatalogEntry : CatalogEntry
{
    private readonly List<MacroParameter> _parameters;
    private ParsedExpression _bodyExpression;

    public IReadOnlyList<MacroParameter> Parameters => _parameters;
    public ParsedExpression BodyExpression => _bodyExpression;

    public ScalarMacroCatalogEntry(string name, IReadOnlyList<MacroParameter> parameters, ParsedExpression bodyExpression)
        : base(CatalogEntryType.SCALAR_MACRO_ENTRY, name)
    {
        _parameters = parameters
            .Select(p => new MacroParameter(p.Name, p.DefaultExpression?.Copy()))
            .ToList();
        _bodyExpression = bodyExpression.Copy();
    }

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write(Name);
        writer.Write(OID);
        writer.Write(Timestamp);
        writer.Write(IsDeleted);
        writer.Write(HasParent);
        writer.Write(_parameters.Count);
        foreach (var parameter in _parameters)
        {
            writer.Write(parameter.Name);
            writer.Write(parameter.DefaultExpression != null);
            if (parameter.DefaultExpression != null)
                writer.Write(parameter.DefaultExpression.GetRawName());
        }

        writer.Write(_bodyExpression.GetRawName());
    }

    public new static ScalarMacroCatalogEntry Deserialize(BinaryReader reader)
    {
        var name = reader.ReadString();
        var oid = reader.ReadUInt64();
        var timestamp = reader.ReadUInt64();
        var isDeleted = reader.ReadBoolean();
        var hasParent = reader.ReadBoolean();
        var parameterCount = reader.ReadInt32();
        var parameters = new List<MacroParameter>(parameterCount);
        for (var i = 0; i < parameterCount; i++)
        {
            var parameterName = reader.ReadString();
            var hasDefault = reader.ReadBoolean();
            ParsedExpression? defaultExpression = null;
            if (hasDefault)
                defaultExpression = ParseExpression(reader.ReadString());
            parameters.Add(new MacroParameter(parameterName, defaultExpression));
        }

        var bodyExpression = ParseExpression(reader.ReadString());
        var entry = new ScalarMacroCatalogEntry(name, parameters, bodyExpression);
        entry.SetOID(oid);
        entry.SetTimestamp(timestamp);
        entry.SetDeleted(isDeleted);
        entry.SetHasParent(hasParent);
        return entry;
    }

    private static ParsedExpression ParseExpression(string expressionText)
    {
        var inputStream = new AntlrInputStream($"RETURN {expressionText};");
        var lexer = new CypherLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new CypherParser(tokenStream);
        var root = parser.ku_Statements();
        var transformer = new Transformer(root);
        var statement = transformer.Transform().OfType<RegularQuery>().FirstOrDefault();
        var singleQuery = statement?.GetSingleQuery(0);
        if (singleQuery == null ||
            !singleQuery.HasReturnClause() ||
            singleQuery.GetReturnClause().ProjectionBody.ProjectionExpressions.Count == 0)
            throw new InvalidDataException($"Could not parse macro expression '{expressionText}'.");

        return singleQuery.GetReturnClause().ProjectionBody.ProjectionExpressions[0].Copy();
    }
}
