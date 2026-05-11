using System;
using Antlr4.Runtime;
using BogDb.Core.Binder;
using BogDb.Core.Catalog;
using BogDb.Core.Common;
using BogDb.Core.Parser;
using BogDb.Core.Parser.Antlr4;
using Xunit;

namespace BogDb.Tests.Binder;

public class BinderTests
{
    private class DummyParsedExpression : ParsedExpression
    {
        public DummyParsedExpression(ExpressionType type) : base(type) 
        { 
            _children.Add(null);
            _children.Add(null);
        }
        public override ParsedExpression Copy() => new DummyParsedExpression(_type);
    }
    
    private class DummyVariableParsedExpression : ParsedExpression
    {
        public DummyVariableParsedExpression(string name) : base(ExpressionType.VARIABLE, name) { }
        public override ParsedExpression Copy() => new DummyVariableParsedExpression(_rawName);
    }

    [Fact]
    public void BinderScope_ThrowsWhenVariableMissing()
    {
        // Arrange
        var scope = new BinderScope();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => scope.GetExpression("unknown_var"));
        Assert.Contains("not found in scope", ex.Message);
    }

    [Fact]
    public void BinderScope_CanRetrieveMemorizedVariables()
    {
        // Arrange
        var scope = new BinderScope();
        var nodeExp = new NodeExpression("my_node");

        // Act
        scope.AddExpression("my_node", nodeExp);

        // Assert
        Assert.True(scope.Contains("my_node"));
        Assert.Equal(nodeExp, scope.GetExpression("my_node"));
    }

    [Fact]
    public void ExpressionBinder_ThrowsOnIncompatibleComparison()
    {
        // Arrange
        var catalog = new BogDb.Core.Catalog.Catalog();
        var binder = new BogDb.Core.Binder.Binder(catalog);
        var expBinder = binder.ExpressionBinder;

        var parsedLeft = new DummyVariableParsedExpression("strVar");
        var parsedRight = new DummyVariableParsedExpression("intVar");
        
        var compExpr = new DummyParsedExpression(ExpressionType.EQUALS);
        compExpr.SetChild(0, parsedLeft);
        compExpr.SetChild(1, parsedRight);
        
        // Mock variables of different types in scope
        binder.Scope.AddExpression("strVar", new DummyExp(LogicalTypeID.STRING));
        binder.Scope.AddExpression("intVar", new DummyExp(LogicalTypeID.INT64));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => expBinder.BindExpression(compExpr));
        Assert.Contains("Cannot compare differing types", ex.Message);
    }

    [Fact]
    public void Binder_BindsMatchWhereReturnQuery()
    {
        var catalog = new BogDb.Core.Catalog.Catalog();
        var binder = new BogDb.Core.Binder.Binder(catalog);
        var tx = new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1UL, 1UL);

        var person = new NodeTableCatalogEntry("Person", 0);
        person.AddProperty(new PropertyDefinition(new ColumnDefinition("age", LogicalTypeID.INT64)));
        catalog.CreateTableEntry(tx, person);

        var inputStream = new AntlrInputStream("MATCH (n:Person) WHERE n.age = 40 RETURN n.age");
        var lexer = new CypherLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new CypherParser(tokens);
        var statement = new Transformer(parser.ku_Statements()).Transform()[0];

        var bound = Assert.IsType<BoundRegularQuery>(binder.Bind(statement));
        var singleQuery = bound.GetSingleQuery(0);
        var matchClause = Assert.IsType<BoundMatchClause>(singleQuery.GetReadingClause(0));
        Assert.NotNull(matchClause.WherePredicate);
        Assert.True(singleQuery.HasReturnClause());
        Assert.Single(singleQuery.ReturnClause!.Items);
        Assert.Equal(LogicalTypeID.INT64, singleQuery.ReturnClause.Items[0].Expression.DataType);
    }

    [Fact]
    public void Binder_BindsMatchQuery_NodeHasPropertyExpressions()
    {
        var catalog = new BogDb.Core.Catalog.Catalog();
        var tx = new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1UL, 1UL);
        var person = new NodeTableCatalogEntry("Person", 0);
        person.AddProperty(new PropertyDefinition(new ColumnDefinition("age", LogicalTypeID.INT64)));
        catalog.CreateTableEntry(tx, person);

        var binder = new BogDb.Core.Binder.Binder(catalog);
        var inputStream = new AntlrInputStream("MATCH (n:Person) RETURN n.age");
        var lexer = new CypherLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new CypherParser(tokens);
        var statement = new Transformer(parser.ku_Statements()).Transform()[0];

        var bound = Assert.IsType<BoundRegularQuery>(binder.Bind(statement));
        var singleQuery = bound.GetSingleQuery(0);
        var matchClause = Assert.IsType<BoundMatchClause>(singleQuery.GetReadingClause(0));
        var node = matchClause.QueryGraph.GetQueryNode(0);
        Assert.Equal("n", node.VariableName);
        var propExpr = node.GetPropertyExpression("age");
        Assert.NotNull(propExpr);
        Assert.Equal(LogicalTypeID.INT64, propExpr!.DataType);
    }

    [Fact]
    public void Binder_Throws_WhenNodeLabelNotInCatalog()
    {
        var catalog = new BogDb.Core.Catalog.Catalog();
        var binder = new BogDb.Core.Binder.Binder(catalog);

        var inputStream = new AntlrInputStream("MATCH (n:Ghost) RETURN n");
        var lexer = new CypherLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new CypherParser(tokens);
        var statement = new Transformer(parser.ku_Statements()).Transform()[0];

        var ex = Assert.Throws<InvalidOperationException>(() => binder.Bind(statement));
        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void ExpressionBinder_BindsUnhandledExpressionType_AsOpaqueFunction()
    {
        var catalog = new BogDb.Core.Catalog.Catalog();
        var binder = new BogDb.Core.Binder.Binder(catalog);
        var expBinder = binder.ExpressionBinder;

        binder.Scope.AddExpression("x", new DummyExp(LogicalTypeID.INT64));
        var parsed = new DummyParsedExpression(ExpressionType.CASE_ELSE);
        parsed.SetChild(0, new DummyVariableParsedExpression("x"));

        var bound = expBinder.BindExpression(parsed);

        var opaque = Assert.IsType<BoundFunctionExpression>(bound);
        Assert.Equal("CASE_ELSE", opaque.FunctionName);
        Assert.Single(opaque.Arguments);
        Assert.Equal(LogicalTypeID.INT64, opaque.Arguments[0].DataType);
    }

    private class DummyExp : Expression
    {
        public DummyExp(LogicalTypeID type) : base(ExpressionType.VARIABLE, type) { }
    }
}
