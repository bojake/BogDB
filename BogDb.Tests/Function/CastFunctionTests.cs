using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class CastFunctionTests
{
    [Fact] public void ToInt64_FromString()
        => Assert.Equal(42L, FunctionDispatcher.Invoke("to_int64", ["42"]));

    [Fact] public void ToInt64_FromDouble()
        => Assert.Equal(3L, FunctionDispatcher.Invoke("int64", [3.9]));

    [Fact] public void ToDouble_FromString()
        => Assert.Equal(3.14, (double)FunctionDispatcher.Invoke("to_double", ["3.14"])!, 5);

    [Fact] public void ToBool_FromInt()
        => Assert.Equal(true, FunctionDispatcher.Invoke("bool", [1L]));

    [Fact] public void ToBool_FromZero()
        => Assert.Equal(false, FunctionDispatcher.Invoke("bool", [0L]));

    [Fact] public void ToString_FromInt()
        => Assert.Equal("42", FunctionDispatcher.Invoke("to_string", [42L]));

    [Fact] public void Cast_IntToString()
        => Assert.Equal(7L, FunctionDispatcher.Invoke("cast", [7L, "int64"]));

    [Fact] public void Cast_StringToInt()
        => Assert.Equal(99L, FunctionDispatcher.Invoke("cast", ["99", "int64"]));

    [Fact] public void Cast_BadStringReturnsNull()
        => Assert.Null(FunctionDispatcher.Invoke("cast", ["abc", "int64"]));

    [Fact] public void ToDate_ParsesDate()
        => Assert.Equal("2024-03-19", FunctionDispatcher.Invoke("to_date", ["2024-03-19"]));

    [Fact] public void Typeof_Int()
        => Assert.Equal("INT64", FunctionDispatcher.Invoke("typeof", [1L]));

    [Fact] public void Typeof_String()
        => Assert.Equal("STRING", FunctionDispatcher.Invoke("typeof", ["hello"]));

    [Fact] public void Typeof_Null()
        => Assert.Equal("NULL", FunctionDispatcher.Invoke("typeof", [null]));
}
