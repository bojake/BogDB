using BogDb.Core.Common;
using Xunit;

namespace BogDb.Tests.Common;

public class LogicalTypeUtilsTests
{
    [Fact]
    public void IsDate_ShouldIdentifyDateTypes()
    {
        Assert.True(LogicalTypeUtils.IsDate(LogicalTypeID.DATE));
        Assert.False(LogicalTypeUtils.IsDate(LogicalTypeID.TIMESTAMP));
        Assert.False(LogicalTypeUtils.IsDate(LogicalTypeID.STRING));
    }

    [Fact]
    public void IsTimestamp_ShouldIdentifyAllTimestamps()
    {
        Assert.True(LogicalTypeUtils.IsTimestamp(LogicalTypeID.TIMESTAMP));
        Assert.True(LogicalTypeUtils.IsTimestamp(LogicalTypeID.TIMESTAMP_NS));
        Assert.True(LogicalTypeUtils.IsTimestamp(LogicalTypeID.TIMESTAMP_TZ));
        Assert.False(LogicalTypeUtils.IsTimestamp(LogicalTypeID.DATE));
    }

    [Fact]
    public void IsIntegral_ShouldIdentifyUnsignedAndSigned()
    {
        Assert.True(LogicalTypeUtils.IsIntegral(LogicalTypeID.INT64));
        Assert.True(LogicalTypeUtils.IsIntegral(LogicalTypeID.UINT8));
        Assert.True(LogicalTypeUtils.IsIntegral(LogicalTypeID.INT128));
        Assert.False(LogicalTypeUtils.IsIntegral(LogicalTypeID.DOUBLE));
    }

    [Fact]
    public void IsNested_ShouldIdentifyComplexTypes()
    {
        Assert.True(LogicalTypeUtils.IsNested(LogicalTypeID.LIST));
        Assert.True(LogicalTypeUtils.IsNested(LogicalTypeID.STRUCT));
        Assert.True(LogicalTypeUtils.IsNested(LogicalTypeID.MAP));
        Assert.False(LogicalTypeUtils.IsNested(LogicalTypeID.STRING));
    }

    [Fact]
    public void GetFixedTypeSize_ShouldReturnExplicitCByteSizes()
    {
        Assert.Equal(8u, LogicalTypeUtils.GetFixedTypeSize(PhysicalTypeID.INT64));
        Assert.Equal(4u, LogicalTypeUtils.GetFixedTypeSize(PhysicalTypeID.FLOAT));
        Assert.Equal(16u, LogicalTypeUtils.GetFixedTypeSize(PhysicalTypeID.INTERNAL_ID));
        Assert.Equal(16u, LogicalTypeUtils.GetFixedTypeSize(PhysicalTypeID.STRING));
        Assert.Equal(12u, LogicalTypeUtils.GetFixedTypeSize(PhysicalTypeID.LIST));
    }
}
