using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BogDb.Core.Common;

/// <summary>
/// Lightweight query-time interval value mirroring the native months/days/micros shape.
/// It is intentionally runtime-focused for function evaluation and stringification.
/// </summary>
public readonly record struct BogDbInterval(int Months, int Days, long Microseconds) : IComparable, IComparable<BogDbInterval>
{
    private static readonly Regex IsoDurationRegex = new(
        @"^(?<sign>[+-])?P(?:(?<years>\d+)Y)?(?:(?<months>\d+)M)?(?:(?<weeks>\d+)W)?(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WordDurationRegex = new(
        @"(?<value>[+-]?\d+(?:\.\d+)?)\s*(?<unit>[A-Za-z]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex TimeLiteralRegex = new(
        @"^(?<hours>\d{1,9}):(?<minutes>\d{2}):(?<seconds>\d{2})(?:\.(?<fraction>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const long MicrosecondsPerSecond = 1_000_000L;
    private const long MicrosecondsPerMinute = 60L * MicrosecondsPerSecond;
    private const long MicrosecondsPerHour = 60L * MicrosecondsPerMinute;
    private const long MicrosecondsPerDay = 24L * MicrosecondsPerHour;
    private const int DaysPerMonthForDivision = 30;
    private const int MonthsPerQuarter = 3;
    private const int MonthsPerYear = 12;
    private const int MonthsPerDecade = 120;
    private const int MonthsPerCentury = 1_200;
    private const int MonthsPerMillennium = 12_000;
    private const int DaysPerWeek = 7;
    private const long MicrosecondsPerMonth = DaysPerMonthForDivision * MicrosecondsPerDay;

    public static BogDbInterval FromYears(long years) => new(checked((int)(years * 12L)), 0, 0);
    public static BogDbInterval FromMonths(long months) => new(checked((int)months), 0, 0);
    public static BogDbInterval FromDays(long days) => new(0, checked((int)days), 0);
    public static BogDbInterval FromHours(double hours) => new(0, 0, checked((long)Math.Round(hours * MicrosecondsPerHour)));
    public static BogDbInterval FromMinutes(double minutes) => new(0, 0, checked((long)Math.Round(minutes * MicrosecondsPerMinute)));
    public static BogDbInterval FromSeconds(double seconds) => new(0, 0, checked((long)Math.Round(seconds * MicrosecondsPerSecond)));
    public static BogDbInterval FromMilliseconds(double milliseconds) => new(0, 0, checked((long)Math.Round(milliseconds * 1_000d)));
    public static BogDbInterval FromMicroseconds(double microseconds) => new(0, 0, checked((long)Math.Round(microseconds)));

    public static BogDbInterval operator +(BogDbInterval left, BogDbInterval right) =>
        new(checked(left.Months + right.Months), checked(left.Days + right.Days), checked(left.Microseconds + right.Microseconds));

    public static BogDbInterval operator -(BogDbInterval left, BogDbInterval right) =>
        new(checked(left.Months - right.Months), checked(left.Days - right.Days), checked(left.Microseconds - right.Microseconds));

    public BogDbInterval Divide(long divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException();
        if (divisor < 0)
            return new BogDbInterval(-Months, -Days, -Microseconds).Divide(-divisor);

        var monthsRemainder = Months % divisor;
        var carriedDays = checked((long)Days + checked(monthsRemainder * DaysPerMonthForDivision));
        var daysRemainder = carriedDays % divisor;

        return new BogDbInterval(
            checked((int)(Months / divisor)),
            checked((int)(carriedDays / divisor)),
            checked((Microseconds + checked(daysRemainder * MicrosecondsPerDay)) / divisor));
    }

    public DateOnly ApplyToDate(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .AddMonths(Months)
            .AddDays(Days);
        if (Microseconds != 0)
            dateTime = dateTime.AddTicks(Microseconds * 10L);
        return DateOnly.FromDateTime(dateTime);
    }

    public DateTimeOffset ApplyToTimestamp(DateTimeOffset timestamp)
    {
        var result = timestamp.AddMonths(Months).AddDays(Days);
        if (Microseconds != 0)
            result = result.AddTicks(Microseconds * 10L);
        return result;
    }

    public int CompareTo(object? obj)
    {
        if (obj is null)
            return 1;
        if (obj is BogDbInterval other)
            return CompareTo(other);
        throw new ArgumentException("Object must be a BogDbInterval.", nameof(obj));
    }

    public int CompareTo(BogDbInterval other)
    {
        NormalizeForOrdering(this, out var leftMonths, out var leftDays, out var leftMicros);
        NormalizeForOrdering(other, out var rightMonths, out var rightDays, out var rightMicros);

        var monthCompare = leftMonths.CompareTo(rightMonths);
        if (monthCompare != 0)
            return monthCompare;

        var dayCompare = leftDays.CompareTo(rightDays);
        if (dayCompare != 0)
            return dayCompare;

        return leftMicros.CompareTo(rightMicros);
    }

    public long GetPart(string? part)
    {
        part = part?.ToLowerInvariant();
        var (normalizedDays, normalizedMicros) = NormalizeDayAndTime();
        var timeSign = Math.Sign(normalizedMicros);
        var timeParts = GetTimeParts(Math.Abs(normalizedMicros));
        return part switch
        {
            "year" => Months / 12,
            "decade" => Months / MonthsPerDecade,
            "century" => Months / MonthsPerCentury,
            "millennium" => Months / MonthsPerMillennium,
            "month" => Months % 12,
            "quarter" => Months / 3,
            "week" => normalizedDays / 7,
            "day" => normalizedDays,
            "hour" => timeParts.Hours * timeSign,
            "minute" => timeParts.Minutes * timeSign,
            "second" => timeParts.Seconds * timeSign,
            "millisecond" => timeParts.Milliseconds * timeSign,
            "microsecond" => timeParts.Microseconds * timeSign,
            _ => 0L,
        };
    }

    public override string ToString()
    {
        if (Months == 0 && Days == 0 && Microseconds == 0)
            return "PT0S";

        var (normalizedDays, normalizedMicros) = NormalizeDayAndTime();
        if (HasMixedNonZeroSigns(Months, normalizedDays, normalizedMicros))
            return ToSignedComponentString(normalizedDays, normalizedMicros);

        var sb = new StringBuilder();
        var negative = Months < 0 || normalizedDays < 0 || normalizedMicros < 0;
        var totalMonths = Math.Abs(Months);
        long totalDays = Math.Abs(normalizedDays);
        var totalMicros = Math.Abs(normalizedMicros);

        if (negative)
            sb.Append('-');

        sb.Append('P');
        var years = totalMonths / 12;
        var months = totalMonths % 12;
        if (years != 0) sb.Append(years.ToString(CultureInfo.InvariantCulture)).Append('Y');
        if (months != 0) sb.Append(months.ToString(CultureInfo.InvariantCulture)).Append('M');
        if (totalDays != 0) sb.Append(totalDays.ToString(CultureInfo.InvariantCulture)).Append('D');

        var (hours, minutes, seconds, milliseconds, microseconds) = GetTimeParts(totalMicros);
        var hasTime = hours != 0 || minutes != 0 || seconds != 0 || milliseconds != 0 || microseconds != 0;
        if (hasTime)
        {
            sb.Append('T');
            if (hours != 0) sb.Append(hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            if (minutes != 0) sb.Append(minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            var secondValue = seconds + (milliseconds / 1000d) + (microseconds / 1_000_000d);
            if (secondValue != 0d)
            {
                var text = secondValue % 1d == 0d
                    ? secondValue.ToString("0", CultureInfo.InvariantCulture)
                    : secondValue.ToString("0.######", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
                sb.Append(text).Append('S');
            }
        }

        return sb.ToString();
    }

    public static bool TryParse(string? text, out BogDbInterval interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (TryParseIsoDuration(text, out interval))
            return true;

        if (TryParseTimeLiteral(text, out interval))
            return true;

        return TryParseWordDuration(text, out interval);
    }

    private static bool TryParseIsoDuration(string text, out BogDbInterval interval)
    {
        interval = default;
        var match = IsoDurationRegex.Match(text);
        if (!match.Success)
            return false;

        var sign = match.Groups["sign"].Value == "-" ? -1 : 1;
        var years = ParseInt(match.Groups["years"].Value);
        var months = ParseInt(match.Groups["months"].Value);
        var weeks = ParseInt(match.Groups["weeks"].Value);
        var days = ParseInt(match.Groups["days"].Value);
        var hours = ParseInt(match.Groups["hours"].Value);
        var minutes = ParseInt(match.Groups["minutes"].Value);
        var seconds = ParseDouble(match.Groups["seconds"].Value);

        interval = new BogDbInterval(
            sign * ((years * 12) + months),
            sign * ((weeks * 7) + days),
            sign * checked((long)Math.Round((hours * MicrosecondsPerHour) + (minutes * MicrosecondsPerMinute) + (seconds * MicrosecondsPerSecond))));
        return true;
    }

    private static void NormalizeForOrdering(BogDbInterval input, out long months, out long days, out long micros)
    {
        var extraMonthsFromDays = input.Days / DaysPerMonthForDivision;
        var extraMonthsFromMicros = input.Microseconds / MicrosecondsPerMonth;
        var remainingDays = input.Days - (int)(extraMonthsFromDays * DaysPerMonthForDivision);
        var remainingMicros = input.Microseconds - (extraMonthsFromMicros * MicrosecondsPerMonth);

        var extraDaysFromMicros = remainingMicros / MicrosecondsPerDay;
        remainingMicros -= extraDaysFromMicros * MicrosecondsPerDay;

        months = input.Months + extraMonthsFromDays + extraMonthsFromMicros;
        days = remainingDays + extraDaysFromMicros;
        micros = remainingMicros;
    }

    private static bool TryParseWordDuration(string text, out BogDbInterval interval)
    {
        interval = default;
        var normalized = text.Replace(",", " ", StringComparison.Ordinal);
        var matches = WordDurationRegex.Matches(normalized);
        var remainder = WordDurationRegex.Replace(normalized, " ").Trim();
        if (matches.Count == 0 && !TryParseTimeLiteral(remainder, out interval))
            return false;

        if (matches.Count == 0)
            return true;

        int months = 0;
        int days = 0;
        long micros = 0;

        foreach (Match match in matches)
        {
            var value = ParseDouble(match.Groups["value"].Value);
            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            switch (unit)
            {
                case "year":
                case "years":
                case "yr":
                case "yrs":
                case "y":
                    AddMonthsWithFraction(ref months, ref days, value, MonthsPerYear);
                    break;
                case "month":
                case "months":
                case "mon":
                case "mons":
                    AddMonthsWithFraction(ref months, ref days, value, 1);
                    break;
                case "week":
                case "weeks":
                case "w":
                    AddDaysWithFraction(ref days, ref micros, value, DaysPerWeek);
                    break;
                case "day":
                case "days":
                case "d":
                case "dayofmonth":
                    AddDaysWithFraction(ref days, ref micros, value, 1);
                    break;
                case "hour":
                case "hours":
                case "hr":
                case "hrs":
                case "h":
                    micros = checked(micros + (long)Math.Round(value * MicrosecondsPerHour));
                    break;
                case "minute":
                case "minutes":
                case "min":
                case "mins":
                case "m":
                    micros = checked(micros + (long)Math.Round(value * MicrosecondsPerMinute));
                    break;
                case "second":
                case "seconds":
                case "sec":
                case "secs":
                case "s":
                    micros = checked(micros + (long)Math.Round(value * MicrosecondsPerSecond));
                    break;
                case "millisecond":
                case "milliseconds":
                case "msec":
                case "msecs":
                case "ms":
                    micros = checked(micros + (long)Math.Round(value * 1_000d));
                    break;
                case "microsecond":
                case "microseconds":
                case "usecond":
                case "useconds":
                case "usec":
                case "usecs":
                case "us":
                    micros = checked(micros + (long)Math.Round(value));
                    break;
                case "quarter":
                case "quarters":
                    AddMonthsWithFraction(ref months, ref days, value, MonthsPerQuarter);
                    break;
                case "decade":
                case "decades":
                case "dec":
                case "decs":
                    AddMonthsWithFraction(ref months, ref days, value, MonthsPerDecade);
                    break;
                case "century":
                case "centuries":
                case "cent":
                case "c":
                    AddMonthsWithFraction(ref months, ref days, value, MonthsPerCentury);
                    break;
                case "millennium":
                case "millennia":
                case "millenium":
                case "milleniums":
                case "mils":
                    AddMonthsWithFraction(ref months, ref days, value, MonthsPerMillennium);
                    break;
                default:
                    return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(remainder))
        {
            if (!TryParseTimeLiteral(remainder, out var timeLiteral))
                return false;
            micros = checked(micros + timeLiteral.Microseconds);
        }

        interval = new BogDbInterval(months, days, micros);
        return true;
    }

    private static bool TryParseTimeLiteral(string text, out BogDbInterval interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = TimeLiteralRegex.Match(text.Trim());
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["hours"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
            return false;

        var minutes = ParseInt(match.Groups["minutes"].Value);
        var seconds = ParseInt(match.Groups["seconds"].Value);
        var fraction = match.Groups["fraction"].Value;
        long micros = checked((long)hours * MicrosecondsPerHour)
                    + checked((long)minutes * MicrosecondsPerMinute)
                    + checked((long)seconds * MicrosecondsPerSecond);
        if (!string.IsNullOrEmpty(fraction))
        {
            micros = checked(micros + ParseFractionalMicroseconds(fraction));
        }

        interval = new BogDbInterval(0, 0, micros);
        return true;
    }

    private static void AddMonthsWithFraction(ref int months, ref int days, double value, int monthMultiplier)
    {
        var scaled = value * monthMultiplier;
        var wholeMonths = (int)Math.Truncate(scaled);
        var fractionalMonths = scaled - wholeMonths;
        months = checked(months + wholeMonths);
        if (fractionalMonths != 0d)
        {
            days = checked(days + (int)Math.Round(fractionalMonths * DaysPerMonthForDivision));
        }
    }

    private static void AddDaysWithFraction(ref int days, ref long micros, double value, int dayMultiplier)
    {
        var scaled = value * dayMultiplier;
        var wholeDays = (int)Math.Truncate(scaled);
        var fractionalDays = scaled - wholeDays;
        days = checked(days + wholeDays);
        if (fractionalDays != 0d)
        {
            micros = checked(micros + (long)Math.Round(fractionalDays * MicrosecondsPerDay));
        }
    }

    private static long ParseFractionalMicroseconds(string fraction)
    {
        var micros = 0L;
        var scale = 100_000L;
        foreach (var ch in fraction)
        {
            if (!char.IsDigit(ch))
                break;
            if (scale > 0)
            {
                micros += (ch - '0') * scale;
                scale /= 10;
            }
            else if (ch >= '5')
            {
                micros += 1;
                break;
            }
        }
        return micros;
    }

    private (long Hours, long Minutes, long Seconds, long Milliseconds, long Microseconds) GetTimeParts()
        => GetTimeParts(Math.Abs(Microseconds));

    private (long Days, long Microseconds) NormalizeDayAndTime()
    {
        try
        {
            var totalMicros = checked(checked((long)Days * MicrosecondsPerDay) + Microseconds);
            return (totalMicros / MicrosecondsPerDay, totalMicros % MicrosecondsPerDay);
        }
        catch (OverflowException)
        {
            return (Days, Microseconds);
        }
    }

    private static bool HasMixedNonZeroSigns(int months, long days, long micros)
    {
        var sign = 0;
        foreach (var value in new long[] { months, days, micros })
        {
            if (value == 0)
                continue;

            var currentSign = value > 0 ? 1 : -1;
            if (sign == 0)
            {
                sign = currentSign;
                continue;
            }

            if (sign != currentSign)
                return true;
        }

        return false;
    }

    private string ToSignedComponentString(long normalizedDays, long normalizedMicros)
    {
        var parts = new StringBuilder();
        AppendSignedMonthParts(parts, Months);
        AppendSignedValue(parts, normalizedDays, "day");

        var absMicros = Math.Abs(normalizedMicros);
        var timeSign = Math.Sign(normalizedMicros);
        var (hours, minutes, seconds, milliseconds, microseconds) = GetTimeParts(absMicros);
        AppendSignedValue(parts, hours * timeSign, "hour");
        AppendSignedValue(parts, minutes * timeSign, "minute");
        AppendSignedValue(parts, seconds * timeSign, "second");
        AppendSignedValue(parts, milliseconds * timeSign, "millisecond");
        AppendSignedValue(parts, microseconds * timeSign, "microsecond");

        return parts.Length == 0 ? "PT0S" : parts.ToString();
    }

    private static void AppendSignedMonthParts(StringBuilder sb, int months)
    {
        if (months == 0)
            return;

        var sign = Math.Sign(months);
        var totalMonths = Math.Abs(months);
        var years = totalMonths / 12;
        var remainingMonths = totalMonths % 12;
        AppendSignedValue(sb, years * sign, "year");
        AppendSignedValue(sb, remainingMonths * sign, "month");
    }

    private static void AppendSignedValue(StringBuilder sb, long value, string unit)
    {
        if (value == 0)
            return;

        if (sb.Length > 0)
            sb.Append(' ');

        sb.Append(value.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Math.Abs(value) == 1 ? unit : $"{unit}s");
    }

    private static (long Hours, long Minutes, long Seconds, long Milliseconds, long Microseconds) GetTimeParts(long totalMicros)
    {
        var hours = totalMicros / MicrosecondsPerHour;
        totalMicros %= MicrosecondsPerHour;
        var minutes = totalMicros / MicrosecondsPerMinute;
        totalMicros %= MicrosecondsPerMinute;
        var seconds = totalMicros / MicrosecondsPerSecond;
        totalMicros %= MicrosecondsPerSecond;
        var milliseconds = totalMicros / 1_000L;
        var microseconds = totalMicros % 1_000L;
        return (hours, minutes, seconds, milliseconds, microseconds);
    }

    private static int ParseInt(string value)
        => string.IsNullOrEmpty(value) ? 0 : int.Parse(value, CultureInfo.InvariantCulture);

    private static double ParseDouble(string value)
        => string.IsNullOrEmpty(value) ? 0d : double.Parse(value, CultureInfo.InvariantCulture);
}
