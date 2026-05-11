using System;
using Xunit;
using BogDb.Core.Common;

namespace BogDb.Tests.ExpressionEvaluator;

public class StringFunctionTests
{
    [Fact]
    public void Concat_CorrectlyConcatenatesStrings()
    {
        using var leftVector = new ValueVector(LogicalTypeID.STRING, 1);
        using var rightVector = new ValueVector(LogicalTypeID.STRING, 1);
        using var resultVector = new ValueVector(LogicalTypeID.STRING, 1);

        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(leftVector, 0, "Hell");
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(rightVector, 0, "o");

        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.Concat(leftVector, rightVector, resultVector);

        var resultStr = resultVector.GetValue<KuString>(0).GetAsString();
        Assert.Equal("Hello", resultStr);
    }

    [Fact]
    public void StringPredicates_CorrectlyEvaluate()
    {
        using var leftVector = new ValueVector(LogicalTypeID.STRING, 3);
        using var rightVector = new ValueVector(LogicalTypeID.STRING, 3);
        using var resultVector = new ValueVector(LogicalTypeID.BOOL, 3);

        // Test 1: Contains true
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(leftVector, 0, "Hello World");
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(rightVector, 0, "lo W");

        // Test 2: StartsWith false
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(leftVector, 1, "Hello World");
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(rightVector, 1, "World");

        // Test 3: EndsWith true
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(leftVector, 2, "Hello World");
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(rightVector, 2, "World");

        // Execute Contains
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.Contains(leftVector, rightVector, resultVector);
        Assert.Equal(1, resultVector.GetValue<byte>(0)); // true

        // Execute StartsWith
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.StartsWith(leftVector, rightVector, resultVector);
        Assert.Equal(0, resultVector.GetValue<byte>(1)); // false

        // Execute EndsWith
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.EndsWith(leftVector, rightVector, resultVector);
        Assert.Equal(1, resultVector.GetValue<byte>(2)); // true
    }
    [Fact]
    public void StringFormatting_CorrectlyEvaluatesLowerUpperLength()
    {
        using var inputVector = new ValueVector(LogicalTypeID.STRING, 3);
        using var resultStrVector = new ValueVector(LogicalTypeID.STRING, 3);
        using var resultIntVector = new ValueVector(LogicalTypeID.INT64, 3);

        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(inputVector, 0, "TESTing");
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(inputVector, 1, "BogDb");
        inputVector.SetNull(2, true);

        // Test Length
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.Length(inputVector, resultIntVector);
        Assert.Equal(7, resultIntVector.GetValue<long>(0));
        Assert.Equal(5, resultIntVector.GetValue<long>(1));
        Assert.True(resultIntVector.IsNull(2));

        // Test Lower
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.Lower(inputVector, resultStrVector);
        Assert.Equal("testing", resultStrVector.GetValue<KuString>(0).GetAsString());
        Assert.Equal("bogdb", resultStrVector.GetValue<KuString>(1).GetAsString());
        Assert.True(resultStrVector.IsNull(2));

        // Test Upper
        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.Upper(inputVector, resultStrVector);
        Assert.Equal("TESTING", resultStrVector.GetValue<KuString>(0).GetAsString());
        Assert.Equal("BOGDB", resultStrVector.GetValue<KuString>(1).GetAsString());
        Assert.True(resultStrVector.IsNull(2));
    }
}
