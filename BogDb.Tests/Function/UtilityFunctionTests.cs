using System;
using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class UtilityFunctionTests
{
    [Fact] public void Coalesce_FirstNonNull()
        => Assert.Equal("b", FunctionDispatcher.Invoke("coalesce", [null, "b", "c"]));

    [Fact] public void Coalesce_AllNull()
        => Assert.Null(FunctionDispatcher.Invoke("coalesce", [null, null]));

    [Fact] public void IfNull_ReturnsFallback()
        => Assert.Equal(42L, FunctionDispatcher.Invoke("ifnull", [null, 42L]));

    [Fact] public void IfNull_ReturnsPrimary()
        => Assert.Equal(7L, FunctionDispatcher.Invoke("ifnull", [7L, 42L]));

    [Fact] public void NullIf_EqualReturnsNull()
        => Assert.Null(FunctionDispatcher.Invoke("nullif", [5L, 5L]));

    [Fact] public void NullIf_DifferentReturnsFirst()
        => Assert.Equal(5L, FunctionDispatcher.Invoke("nullif", [5L, 6L]));

    [Fact] public void If_TrueBranch()
        => Assert.Equal("yes", FunctionDispatcher.Invoke("if", [true, "yes", "no"]));

    [Fact] public void If_FalseBranch()
        => Assert.Equal("no", FunctionDispatcher.Invoke("if", [false, "yes", "no"]));

    [Fact] public void Uuid_ProducesGuidFormat()
    {
        var result = FunctionDispatcher.Invoke("gen_random_uuid", []) as string;
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result, out _));
    }

    [Fact] public void Random_InZeroOneRange()
    {
        var v = (double)FunctionDispatcher.Invoke("random", [])!;
        Assert.InRange(v, 0.0, 1.0);
    }

    [Fact] public void Hash_SameValueSameHash()
    {
        var h1 = FunctionDispatcher.Invoke("hash", ["hello"]);
        var h2 = FunctionDispatcher.Invoke("hash", ["hello"]);
        Assert.Equal(h1, h2);
    }

    [Fact] public void Hash_NullReturnsNull()
        => Assert.Null(FunctionDispatcher.Invoke("hash", [null]));

    [Fact] public void Md5_KnownHash()
    {
        var result = FunctionDispatcher.Invoke("md5", [""]) as string;
        // MD5 of empty string
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", result);
    }

    [Fact] public void BoolAnd_TrueTrue()
        => Assert.Equal(true, FunctionDispatcher.Invoke("bool_and", [true, true]));

    [Fact] public void BoolOr_FalseTrue()
        => Assert.Equal(true, FunctionDispatcher.Invoke("bool_or", [false, true]));
}
