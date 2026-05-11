using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// End-to-end tests for the scalar function gap burndown: aliases, bitwise,
/// math, utility, date, and list functions added for C++ parity.
/// </summary>
public class ScalarFunctionGapTests
{
    private static QueryResult Q(string cypher)
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        return conn.Query(cypher);
    }

    // ── Tier 1: String Aliases ───────────────────────────────────────────────

    [Fact]
    public void Prefix_AliasForStartsWith()
    {
        var r = Q("RETURN prefix('hello', 'hel')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True((bool)r.GetNext().GetValue(0)!);
    }

    [Fact]
    public void Suffix_AliasForEndsWith()
    {
        var r = Q("RETURN suffix('hello', 'llo')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True((bool)r.GetNext().GetValue(0)!);
    }

    [Fact]
    public void StrSplit_AliasForSplit()
    {
        var r = Q("RETURN str_split('a,b,c', ',')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }

    [Fact]
    public void StringToArray_AliasForSplit()
    {
        var r = Q("RETURN string_to_array('a-b-c', '-')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }

    // ── Tier 2: Bitwise Operators ────────────────────────────────────────────

    [Fact]
    public void BitwiseAnd_Works()
    {
        var r = Q("RETURN bitwise_and(12, 10)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(8L, r.GetNext().GetValue(0));  // 1100 & 1010 = 1000
    }

    [Fact]
    public void BitwiseOr_Works()
    {
        var r = Q("RETURN bitwise_or(12, 10)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(14L, r.GetNext().GetValue(0)); // 1100 | 1010 = 1110
    }

    [Fact]
    public void BitwiseXor_Works()
    {
        var r = Q("RETURN bitwise_xor(12, 10)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(6L, r.GetNext().GetValue(0));  // 1100 ^ 1010 = 0110
    }

    [Fact]
    public void BitshiftLeft_Works()
    {
        var r = Q("RETURN bitshift_left(1, 3)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(8L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void BitshiftRight_Works()
    {
        var r = Q("RETURN bitshift_right(16, 2)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(4L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Negate_Works()
    {
        var r = Q("RETURN negate(42)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(-42L, r.GetNext().GetValue(0));
    }

    // ── Tier 3: Math Functions ───────────────────────────────────────────────

    [Fact]
    public void Gamma_OfFive_Equals24()
    {
        var r = Q("RETURN gamma(5)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = (double)r.GetNext().GetValue(0)!;
        Assert.InRange(val, 23.9, 24.1); // Γ(5) = 4! = 24
    }

    [Fact]
    public void Lgamma_OfFive()
    {
        var r = Q("RETURN lgamma(5)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = (double)r.GetNext().GetValue(0)!;
        Assert.InRange(val, 3.17, 3.19); // ln(24) ≈ 3.178
    }

    [Fact]
    public void Setseed_DoesNotThrow()
    {
        var r = Q("RETURN setseed(0.42)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
    }

    // ── Tier 4: Utility Functions ────────────────────────────────────────────

    [Fact]
    public void Greatest_ReturnsMax()
    {
        var r = Q("RETURN greatest(3, 7, 1, 5)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(7L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Least_ReturnsMin()
    {
        var r = Q("RETURN least(3, 7, 1, 5)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Greatest_WithStrings()
    {
        var r = Q("RETURN greatest('apple', 'banana', 'cherry')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("cherry", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Least_WithStrings()
    {
        var r = Q("RETURN least('apple', 'banana', 'cherry')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("apple", r.GetNext().GetValue(0));
    }

    [Fact]
    public void ConstantOrNull_ReturnsValue_WhenSecondArgIsNull()
    {
        var r = Q("RETURN constant_or_null(42, null)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(42L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void DbVersion_ReturnsString()
    {
        var r = Q("RETURN db_version()");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var ver = r.GetNext().GetValue(0)?.ToString();
        Assert.False(string.IsNullOrEmpty(ver));
    }

    [Fact]
    public void CatalogVersion_ReturnsNumber()
    {
        var r = Q("RETURN catalog_version()");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Typeof_ReturnsTypeName()
    {
        var r = Q("RETURN typeof(42)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("INT64", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Typeof_String()
    {
        var r = Q("RETURN typeof('hello')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("STRING", r.GetNext().GetValue(0));
    }

    // ── Tier 5: Date Functions ───────────────────────────────────────────────

    [Fact]
    public void Century_Returns21stCentury()
    {
        var r = Q("RETURN century('2025-03-15')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(21L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Dayname_ReturnsDayOfWeek()
    {
        // 2025-03-15 is Saturday
        var r = Q("RETURN dayname('2025-03-15')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("Saturday", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Monthname_ReturnsMonthName()
    {
        var r = Q("RETURN monthname('2025-03-15')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("March", r.GetNext().GetValue(0));
    }

    [Fact]
    public void LastDay_ReturnsLastDayOfMonth()
    {
        var r = Q("RETURN last_day('2025-02-15')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("2025-02-28", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Datepart_AliasForDatePart()
    {
        var r = Q("RETURN datepart('year', '2025-03-15')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(2025L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Datetrunc_AliasForDateTrunc()
    {
        var r = Q("RETURN datetrunc('month', '2025-03-15T10:30:00')");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(val);
        Assert.Contains("2025-03-01", val);
    }

    // ── Tier 6: List/Array Functions ─────────────────────────────────────────

    [Fact]
    public void ListAnyValue_ReturnsFirstNonNull()
    {
        var r = Q("RETURN list_any_value([null, null, 42, 99])");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(42L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void ArrayIndexof_Returns1BasedPosition()
    {
        var r = Q("RETURN array_indexof([10, 20, 30], 20)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(2L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void ArrayPosition_SameAsArrayIndexof()
    {
        var r = Q("RETURN array_position([10, 20, 30], 30)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(3L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void ArrayPrepend_PrependsThenReturnsList()
    {
        var r = Q("RETURN array_prepend([2, 3], 1)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }
}
