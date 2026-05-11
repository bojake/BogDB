using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using BogDb.Core.Common;
using BogDb.Core.Parser;
using BogDb.Core.Parser.Query;
using BogDb.Core.Parser.Antlr4;
using Xunit;

namespace BogDb.Tests.Parser;

public class ParserTests
{
    private List<Statement> ParseQuery(string query)
    {
        var inputStream = new AntlrInputStream(query);
        var lexer = new CypherLexer(inputStream);
        var commonTokenStream = new CommonTokenStream(lexer);
        var parser = new CypherParser(commonTokenStream);
        var root = parser.ku_Statements();
        var transformer = new Transformer(root);
        return transformer.Transform();
    }

    [Fact]
    public void Transformer_ParsesSymbolicName_Correctly()
    {
        var statements = ParseQuery("MATCH (n:Person) RETURN n;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        Assert.Equal(1, query.GetNumSingleQueries());
    }

    [Fact]
    public void Transformer_BuildsMatchWhereReturnQueryShape()
    {
        var statements = ParseQuery("MATCH (n:Person) WHERE n.age = 40 RETURN n.age;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var singleQuery = query.GetSingleQuery(0);

        Assert.Equal(1, singleQuery.GetNumReadingClauses());
        Assert.Equal(0, singleQuery.GetNumUpdatingClauses());
        Assert.True(singleQuery.HasReturnClause());

        var matchClause = Assert.IsType<MatchClause>(singleQuery.GetReadingClause(0));
        Assert.True(matchClause.HasWherePredicate());
        Assert.Equal("Person", matchClause.GetPatternElement(0).GetFirstNodePattern().TableNames[0]);

        var where = matchClause.GetWherePredicate();
        Assert.Equal(ExpressionType.EQUALS, where.GetExpressionType());
        var whereLeft = Assert.IsType<ParsedPropertyExpression>(where.GetChild(0));
        var whereRight = Assert.IsType<ParsedLiteralExpression>(where.GetChild(1));
        Assert.Equal("age", whereLeft.PropertyName);
        Assert.Equal("n", Assert.IsType<ParsedVariableExpression>(whereLeft.GetChildExpression()).VariableName);
        Assert.Equal(40L, whereRight.Value);

        var returnClause = singleQuery.GetReturnClause();
        var projection = Assert.Single(returnClause.ProjectionBody.ProjectionExpressions);
        Assert.Equal(ExpressionType.PROPERTY, projection.GetExpressionType());
        var projectedProperty = Assert.IsType<ParsedPropertyExpression>(projection);
        Assert.Equal("age", projectedProperty.PropertyName);
        Assert.Equal("n", Assert.IsType<ParsedVariableExpression>(projectedProperty.GetChildExpression()).VariableName);
    }

    [Fact]
    public void Transformer_ParsesTransactionStatements()
    {
        var beginStatements = ParseQuery("BEGIN TRANSACTION");
        var begin = Assert.IsType<TransactionStatement>(Assert.Single(beginStatements));
        Assert.Equal(TransactionCommand.BEGIN, begin.Command);
        Assert.False(begin.IsReadOnly);

        var beginReadOnlyStatements = ParseQuery("BEGIN TRANSACTION READ ONLY");
        var beginReadOnly = Assert.IsType<TransactionStatement>(Assert.Single(beginReadOnlyStatements));
        Assert.Equal(TransactionCommand.BEGIN, beginReadOnly.Command);
        Assert.True(beginReadOnly.IsReadOnly);

        var commitStatements = ParseQuery("COMMIT");
        var commit = Assert.IsType<TransactionStatement>(Assert.Single(commitStatements));
        Assert.Equal(TransactionCommand.COMMIT, commit.Command);

        var rollbackStatements = ParseQuery("ROLLBACK");
        var rollback = Assert.IsType<TransactionStatement>(Assert.Single(rollbackStatements));
        Assert.Equal(TransactionCommand.ROLLBACK, rollback.Command);
    }

    [Fact]
    public void ParsedExpression_SupportsDeepNesting()
    {
        // Arrange
        var root = new TestParsedExpression(ExpressionType.AND, "AND");
        var leftChild = new TestParsedExpression(ExpressionType.EQUALS, "=");
        var rightChild = new TestParsedExpression(ExpressionType.GREATER_THAN, ">");
        
        root.SetChild(0, leftChild);
        root.SetChild(1, rightChild);

        // Act & Assert
        Assert.Equal(ExpressionType.AND, root.GetExpressionType());
        Assert.Equal(2, root.GetNumChildren());
        Assert.Equal(ExpressionType.EQUALS, root.GetChild(0).GetExpressionType());
    }

    [Fact]
    public void Transformer_ExpandsSetMapAssignmentIntoPropertyEqualsItems()
    {
        var statements = ParseQuery("MATCH (n:Person) SET n = {age: 40, name: 'Ada'} RETURN n;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var setClause = Assert.IsType<SetClause>(single.GetUpdatingClause(0));

        Assert.Equal(2, setClause.SetItems.Count);

        var first = setClause.SetItems[0];
        Assert.Equal(ExpressionType.EQUALS, first.GetExpressionType());
        var firstLeft = Assert.IsType<ParsedPropertyExpression>(first.GetChild(0));
        var firstRight = Assert.IsType<ParsedLiteralExpression>(first.GetChild(1));
        Assert.Equal("n", Assert.IsType<ParsedVariableExpression>(firstLeft.GetChildExpression()).VariableName);
        Assert.Equal("age", firstLeft.PropertyName);
        Assert.Equal(40L, firstRight.Value);

        var second = setClause.SetItems[1];
        Assert.Equal(ExpressionType.EQUALS, second.GetExpressionType());
        var secondLeft = Assert.IsType<ParsedPropertyExpression>(second.GetChild(0));
        var secondRight = Assert.IsType<ParsedLiteralExpression>(second.GetChild(1));
        Assert.Equal("n", Assert.IsType<ParsedVariableExpression>(secondLeft.GetChildExpression()).VariableName);
        Assert.Equal("name", secondLeft.PropertyName);
        Assert.Equal("Ada", secondRight.Value);
    }

    [Fact]
    public void Transformer_PreservesCastTargetType_AsSecondArgument()
    {
        var statements = ParseQuery("RETURN CAST(7 AS INT16);");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var ret = single.GetReturnClause();
        var castExpr = Assert.IsType<ParsedFunctionExpression>(Assert.Single(ret.ProjectionBody.ProjectionExpressions));

        Assert.Equal("CAST", castExpr.FunctionName);
        Assert.Equal(2, castExpr.GetNumChildren());

        var valueArg = Assert.IsType<ParsedLiteralExpression>(castExpr.GetChild(0));
        var typeArg = Assert.IsType<ParsedLiteralExpression>(castExpr.GetChild(1));

        Assert.Equal(7L, valueArg.Value);
        Assert.Equal("INT16", typeArg.Value);
    }

    [Fact]
    public void Transformer_ParsesSearchedCaseExpression_AsFunction()
    {
        var statements = ParseQuery("RETURN CASE WHEN n.age > 30 THEN 1 ELSE 0 END;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var ret = single.GetReturnClause();
        var caseExpr = Assert.IsType<ParsedFunctionExpression>(Assert.Single(ret.ProjectionBody.ProjectionExpressions));

        Assert.Equal("CASE", caseExpr.FunctionName);
        Assert.Equal(3, caseExpr.GetNumChildren());
        Assert.Equal(ExpressionType.GREATER_THAN, caseExpr.GetChild(0).GetExpressionType());
        Assert.Equal(1L, Assert.IsType<ParsedLiteralExpression>(caseExpr.GetChild(1)).Value);
        Assert.Equal(0L, Assert.IsType<ParsedLiteralExpression>(caseExpr.GetChild(2)).Value);
    }

    [Fact]
    public void Transformer_ParsesParameterExpression()
    {
        var statements = ParseQuery("RETURN $minAge;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var ret = single.GetReturnClause();
        var paramExpr = Assert.IsType<ParsedParameterExpression>(Assert.Single(ret.ProjectionBody.ProjectionExpressions));

        Assert.Equal("minAge", paramExpr.ParameterName);
        Assert.Equal("$minAge", paramExpr.GetRawName());
    }

    [Fact]
    public void Transformer_ParsesNodePatternPropertyBagParameter()
    {
        var statements = ParseQuery("MATCH (n:Person $props) RETURN n;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var matchClause = Assert.IsType<MatchClause>(single.GetReadingClause(0));
        var node = matchClause.GetPatternElement(0).GetFirstNodePattern();

        var propertyBag = Assert.IsType<ParsedParameterExpression>(node.PropertyBagExpression);
        Assert.Equal("props", propertyBag.ParameterName);
        Assert.Empty(node.PropertyKeyValues);
    }

    [Fact]
    public void Transformer_PreservesSetPropertyBagParameterAssignment()
    {
        var statements = ParseQuery("MATCH (n:Person) SET n = $props RETURN n;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var setClause = Assert.IsType<SetClause>(single.GetUpdatingClause(0));

        var setItem = Assert.Single(setClause.SetItems);
        Assert.Equal(ExpressionType.EQUALS, setItem.GetExpressionType());
        Assert.Equal("n", Assert.IsType<ParsedVariableExpression>(setItem.GetChild(0)).VariableName);
        Assert.Equal("props", Assert.IsType<ParsedParameterExpression>(setItem.GetChild(1)).ParameterName);
    }

    [Fact]
    public void Transformer_PreservesProfileQueryOption()
    {
        var statements = ParseQuery("PROFILE RETURN 1 AS v;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));

        Assert.True(query.GetIsProfile());
        Assert.Equal(1, query.GetNumSingleQueries());
    }

    [Fact]
    public void Transformer_PreservesExplainQueryOption()
    {
        var statement = Assert.Single(ParseQuery("EXPLAIN LOGICAL RETURN 1 AS v;"));
        var explain = Assert.IsType<ExplainStatement>(statement);

        Assert.Equal(BogDb.Core.Common.ExplainType.LOGICAL_PLAN, explain.ExplainType);
        Assert.IsType<RegularQuery>(explain.StatementToExplain);
    }

    [Fact]
    public void Transformer_DefaultExplain_IsPhysicalPlan()
    {
        var statement = Assert.Single(ParseQuery("EXPLAIN RETURN 1 AS v;"));
        var explain = Assert.IsType<ExplainStatement>(statement);

        Assert.Equal(BogDb.Core.Common.ExplainType.PHYSICAL_PLAN, explain.ExplainType);
        Assert.IsType<RegularQuery>(explain.StatementToExplain);
    }

    [Fact]
    public void Transformer_LowersNonCsvLoadFrom_IntoInQueryCall()
    {
        var statements = ParseQuery("LOAD FROM $path RETURN *;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);

        var clause = Assert.IsType<InQueryCallClause>(single.GetReadingClause(0));
        var function = Assert.IsType<ParsedFunctionExpression>(clause.FunctionExpression);

        Assert.Equal("LOAD_FROM", function.FunctionName);
        Assert.Equal("path", Assert.IsType<ParsedParameterExpression>(function.GetChild(0)).ParameterName);
    }

    [Fact]
    public void Transformer_LowersTypedLoadFrom_ColumnDefinitions_IntoFunctionArguments()
    {
        var statements = ParseQuery("LOAD WITH HEADERS (id INT64, name STRING) FROM $path RETURN *;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);

        var clause = Assert.IsType<InQueryCallClause>(single.GetReadingClause(0));
        var function = Assert.IsType<ParsedFunctionExpression>(clause.FunctionExpression);

        Assert.Equal("LOAD_FROM", function.FunctionName);
        Assert.Equal("path", Assert.IsType<ParsedParameterExpression>(function.GetChild(0)).ParameterName);
        Assert.Equal("id", Assert.IsType<ParsedLiteralExpression>(function.GetChild(1)).Value);
        Assert.Equal("INT64", Assert.IsType<ParsedLiteralExpression>(function.GetChild(2)).Value);
        Assert.Equal("name", Assert.IsType<ParsedLiteralExpression>(function.GetChild(3)).Value);
        Assert.Equal("STRING", Assert.IsType<ParsedLiteralExpression>(function.GetChild(4)).Value);
    }

    [Fact]
    public void Transformer_PreservesStructLiteral_FieldExpressions()
    {
        var statements = ParseQuery("RETURN {name: $name, age: 20 + 1, nested: {enabled: true}} AS s;");
        var query = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var single = query.GetSingleQuery(0);
        var ret = single.GetReturnClause();
        var structExpr = Assert.IsType<ParsedFunctionExpression>(Assert.Single(ret.ProjectionBody.ProjectionExpressions));

        Assert.Equal("STRUCT_LITERAL", structExpr.FunctionName);
        Assert.Equal(6, structExpr.GetNumChildren());
        Assert.Equal("name", Assert.IsType<ParsedLiteralExpression>(structExpr.GetChild(0)).Value);
        Assert.IsType<ParsedParameterExpression>(structExpr.GetChild(1));
        Assert.Equal("age", Assert.IsType<ParsedLiteralExpression>(structExpr.GetChild(2)).Value);
        Assert.IsType<ParsedFunctionExpression>(structExpr.GetChild(3));
        Assert.Equal("nested", Assert.IsType<ParsedLiteralExpression>(structExpr.GetChild(4)).Value);
        Assert.IsType<ParsedFunctionExpression>(structExpr.GetChild(5));
    }

    // ─── G-002: Extended Statement Transforms ──────────────────────────────────

    [Fact]
    public void Transformer_ParsesCreateSequence_WithOptions()
    {
        var statements = ParseQuery("CREATE SEQUENCE my_seq INCREMENT BY 5 START WITH 100 CYCLE");
        var seq = Assert.IsType<CreateSequenceStatement>(Assert.Single(statements));
        Assert.Equal("my_seq", seq.SequenceName);
        Assert.False(seq.IfNotExists);
        Assert.Equal(5L, seq.IncrementBy);
        Assert.Equal(100L, seq.StartWith);
        Assert.True(seq.Cycle);
    }

    [Fact]
    public void Transformer_ParsesCreateSequence_IfNotExists()
    {
        var statements = ParseQuery("CREATE SEQUENCE IF NOT EXISTS counter");
        var seq = Assert.IsType<CreateSequenceStatement>(Assert.Single(statements));
        Assert.Equal("counter", seq.SequenceName);
        Assert.True(seq.IfNotExists);
        Assert.Equal(1L, seq.IncrementBy);
        Assert.False(seq.Cycle);
    }

    [Fact]
    public void Transformer_ParsesCreateType()
    {
        var statements = ParseQuery("CREATE TYPE my_type AS INT64");
        var type = Assert.IsType<CreateTypeStatement>(Assert.Single(statements));
        Assert.Equal("my_type", type.TypeName);
        Assert.Equal("INT64", type.DataType);
    }

    [Fact]
    public void Transformer_ParsesCommentOn()
    {
        var statements = ParseQuery("COMMENT ON TABLE Person IS 'A table of people'");
        var comment = Assert.IsType<CommentOnStatement>(Assert.Single(statements));
        Assert.Equal("Person", comment.TableName);
        Assert.Equal("A table of people", comment.Comment);
    }

    [Fact]
    public void Transformer_ParsesExportDatabase()
    {
        var statements = ParseQuery("EXPORT DATABASE '/tmp/backup'");
        var export = Assert.IsType<ExportDatabaseStatement>(Assert.Single(statements));
        Assert.Equal("/tmp/backup", export.ExportPath);
    }

    [Fact]
    public void Transformer_ParsesImportDatabase()
    {
        var statements = ParseQuery("IMPORT DATABASE '/tmp/backup'");
        var import = Assert.IsType<ImportDatabaseStatement>(Assert.Single(statements));
        Assert.Equal("/tmp/backup", import.ImportPath);
    }

    [Theory]
    [InlineData("CREATE SEQUENCE my_seq", "CREATE SEQUENCE")]
    [InlineData("CREATE TYPE t AS INT64", "CREATE TYPE")]
    [InlineData("COMMENT ON TABLE Person IS 'desc'", "COMMENT ON")]
    [InlineData("EXPORT DATABASE '/tmp/out'", "EXPORT DATABASE")]
    [InlineData("IMPORT DATABASE '/tmp/in'", "IMPORT DATABASE")]
    public void Binder_ProducesClearErrorMessage_ForUnimplementedStatementTypes(string query, string expectedPhrase)
    {
        var statements = ParseQuery(query);
        var statement = Assert.Single(statements);
        var catalog = new BogDb.Core.Catalog.Catalog();
        var binder = new BogDb.Core.Binder.Binder(catalog);
        var ex = Assert.Throws<NotSupportedException>(() => binder.Bind(statement));
        Assert.Contains(expectedPhrase, ex.Message);
        Assert.Contains("not yet implemented", ex.Message);
    }

    // ─── G-016: Non-Reserved Keyword Labels & Reverse Arrows ────────────────────

    [Theory]
    [InlineData("MATCH (p:Profile) RETURN p;", "Profile")]
    [InlineData("MATCH (t:Table) RETURN t;", "Table")]
    [InlineData("MATCH (g:Group) RETURN g;", "Group")]
    [InlineData("MATCH (j:Join) RETURN j;", "Join")]
    [InlineData("MATCH (h:Hint) RETURN h;", "Hint")]
    [InlineData("MATCH (o:On) RETURN o;", "On")]
    [InlineData("MATCH (s:Single) RETURN s;", "Single")]
    public void Transformer_ParsesReservedKeyword_AsNodeLabel(string query, string expectedLabel)
    {
        var statements = ParseQuery(query);
        var queryAst = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var match = Assert.IsType<MatchClause>(queryAst.GetSingleQuery(0).GetReadingClause(0));
        var nodePattern = match.GetPatternElement(0).GetFirstNodePattern();
        Assert.Contains(expectedLabel, nodePattern.TableNames);
    }

    [Fact]
    public void Transformer_ParsesReverseArrow_AsLeftDirection()
    {
        var statements = ParseQuery("MATCH (a:A)<-[:REL]-(b:B) RETURN a;");
        var queryAst = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var match = Assert.IsType<MatchClause>(queryAst.GetSingleQuery(0).GetReadingClause(0));
        var patternElement = match.GetPatternElement(0);
        var chain = patternElement.GetPatternElementChain(0);
        Assert.Equal(ArrowDirection.LEFT, chain.RelPattern.Direction);
    }

    [Fact]
    public void Transformer_ParsesMixedArrowDirections_InSingleChain()
    {
        // a->b<-c: forward then reverse
        var statements = ParseQuery("MATCH (a:A)-[:R1]->(b:B)<-[:R2]-(c:C) RETURN a, b, c;");
        var queryAst = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var match = Assert.IsType<MatchClause>(queryAst.GetSingleQuery(0).GetReadingClause(0));
        var patternElement = match.GetPatternElement(0);
        var chain0 = patternElement.GetPatternElementChain(0);
        var chain1 = patternElement.GetPatternElementChain(1);
        Assert.Equal(ArrowDirection.RIGHT, chain0.RelPattern.Direction);
        Assert.Equal(ArrowDirection.LEFT, chain1.RelPattern.Direction);
    }

    [Fact]
    public void Transformer_ParsesUndirectedRelationship()
    {
        var statements = ParseQuery("MATCH (a:A)-[:REL]-(b:B) RETURN a;");
        var queryAst = Assert.IsType<RegularQuery>(Assert.Single(statements));
        var match = Assert.IsType<MatchClause>(queryAst.GetSingleQuery(0).GetReadingClause(0));
        var patternElement = match.GetPatternElement(0);
        var chain = patternElement.GetPatternElementChain(0);
        Assert.Equal(ArrowDirection.BOTH, chain.RelPattern.Direction);
    }

    // Test Stub class avoiding abstract limitation
    private class TestParsedExpression : ParsedExpression
    {
        public TestParsedExpression(ExpressionType type, string rawName) : base(type, rawName)
        {
            // Inject dummy children to enable SetChild test
            _children.Add(null);
            _children.Add(null);
        }

        public override ParsedExpression Copy()
        {
            return new TestParsedExpression(_type, _rawName);
        }
    }
}
