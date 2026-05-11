using BogDb.Core.Catalog;
using BogDb.Core.Common;
using Xunit;

namespace BogDb.Tests.Common;

public class DeclaredTypeDescriptorTests
{
    [Fact]
    public void Parse_PrimitiveType_PreservesLeafAndRuntimeType()
    {
        var descriptor = DeclaredTypeDescriptor.Parse("INT64");

        Assert.Equal("INT64", descriptor.DeclaredType);
        Assert.Equal(LogicalTypeID.INT64, descriptor.RuntimeType);
        Assert.Equal(LogicalTypeID.INT64, descriptor.LeafType);
        Assert.Equal(0, descriptor.ListDepth);
        Assert.False(descriptor.IsNestedList);
    }

    [Fact]
    public void Parse_SingleDimensionList_PreservesLeafType()
    {
        var descriptor = DeclaredTypeDescriptor.Parse("FLOAT[]");

        Assert.Equal(LogicalTypeID.LIST, descriptor.RuntimeType);
        Assert.Equal(LogicalTypeID.FLOAT, descriptor.LeafType);
        Assert.Equal(1, descriptor.ListDepth);
        Assert.True(descriptor.IsNestedList);
    }

    [Fact]
    public void Parse_MultiDimensionList_PreservesLeafTypeAndDepth()
    {
        var descriptor = DeclaredTypeDescriptor.Parse("INT64[][]");

        Assert.Equal(LogicalTypeID.LIST, descriptor.RuntimeType);
        Assert.Equal(LogicalTypeID.INT64, descriptor.LeafType);
        Assert.Equal(2, descriptor.ListDepth);
    }

    [Fact]
    public void ColumnDefinition_ExposesStructuredDeclaredTypeMetadata()
    {
        var column = new ColumnDefinition("embedding", LogicalTypeID.LIST, "FLOAT[]");

        Assert.Equal(LogicalTypeID.FLOAT, column.LeafType);
        Assert.Equal(1, column.ListDepth);
        Assert.Equal(LogicalTypeID.LIST, column.TypeDescriptor.RuntimeType);
    }
}
