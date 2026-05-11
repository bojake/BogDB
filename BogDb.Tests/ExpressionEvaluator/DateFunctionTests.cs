using System;
using Xunit;
using BogDb.Core.Common;

namespace BogDb.Tests.ExpressionEvaluator;

public class DateFunctionTests
{
    [Fact]
    public void DayName_CorrectlyExtractsDayOfWeek()
    {
        // 1970-01-01 is Thursday
        int daysSinceEpoch = 0; 

        using var dateVector = new ValueVector(LogicalTypeID.DATE, 1);
        using var resultVector = new ValueVector(LogicalTypeID.STRING, 1);

        dateVector.SetValue<int>(0, daysSinceEpoch);

        BogDb.Core.ExpressionEvaluator.DateFunctionEvaluator.DayName(dateVector, resultVector);

        var resultStr = resultVector.GetValue<KuString>(0).GetAsString();
        Assert.Equal("Thursday", resultStr);
    }

    [Fact]
    public void DatePart_CorrectlyExtractsYearAndMonth()
    {
        // 2026-03-16 in days from 1970-01-01 -> ~20528
        var targetDate = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc);
        int daysSinceEpoch = (int)(targetDate - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;

        using var partVector = new ValueVector(LogicalTypeID.STRING, 2);
        using var dateVector = new ValueVector(LogicalTypeID.DATE, 2);
        using var resultVector = new ValueVector(LogicalTypeID.INT64, 2);

        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(partVector, 0, "year");
        dateVector.SetValue<int>(0, daysSinceEpoch);

        BogDb.Core.ExpressionEvaluator.StringFunctionEvaluator.SetKuString(partVector, 1, "month");
        dateVector.SetValue<int>(1, daysSinceEpoch);

        BogDb.Core.ExpressionEvaluator.DateFunctionEvaluator.DatePart(partVector, dateVector, resultVector);

        Assert.Equal(2026, resultVector.GetValue<long>(0));
        Assert.Equal(3, resultVector.GetValue<long>(1));
    }
}
