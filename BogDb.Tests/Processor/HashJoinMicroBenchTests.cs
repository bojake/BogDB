using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Processor.Operator;

namespace BogDb.Tests.Processor;

/// <summary>
/// Tests structural key hashing used by ValueHashJoin, OptionalJoin, and MarkJoin.
/// Replaces the former HashJoinMicroBenchTests that tested the deleted SIMD HashJoinProber.
/// </summary>
public class HashJoinMicroBenchTests
{
    [Fact]
    public void StructuralHash_LongValues_ProduceConsistentHashes()
    {
        // Verify that the structural hash/equality helpers produce correct results 
        // for long values (the most common join key type)
        Assert.Equal(OptionalJoin.StructuralHash(42L), OptionalJoin.StructuralHash(42L));
        Assert.NotEqual(OptionalJoin.StructuralHash(42L), OptionalJoin.StructuralHash(43L));
    }

    [Fact]
    public void StructuralHash_ListValues_ProduceStructuralEquality()
    {
        var list1 = new List<object> { 1L, 2L, 3L };
        var list2 = new List<object> { 1L, 2L, 3L };
        var list3 = new List<object> { 1L, 2L, 4L };

        Assert.Equal(OptionalJoin.StructuralHash(list1), OptionalJoin.StructuralHash(list2));
        Assert.NotEqual(OptionalJoin.StructuralHash(list1), OptionalJoin.StructuralHash(list3));
        Assert.True(OptionalJoin.StructuralEquals(list1, list2));
        Assert.False(OptionalJoin.StructuralEquals(list1, list3));
    }

    [Fact]
    public void StructuralHash_NullValues_Handled()
    {
        Assert.Equal(0, OptionalJoin.StructuralHash(null));
        Assert.True(OptionalJoin.StructuralEquals(null, null));
        Assert.False(OptionalJoin.StructuralEquals(null, 42L));
        Assert.False(OptionalJoin.StructuralEquals(42L, null));
    }

    [Fact]
    public void StructuralJoinKey_Equality_WorksCorrectly()
    {
        var key1 = new StructuralJoinKey(new object?[] { 1L, "hello" });
        var key2 = new StructuralJoinKey(new object?[] { 1L, "hello" });
        var key3 = new StructuralJoinKey(new object?[] { 1L, "world" });

        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
    }
}
