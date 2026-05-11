-- corpus-temporal-arithmetic.cypher
-- G-GOLD-001 (renamed from corpus-interval-functions):
-- Date/timestamp arithmetic and part-extraction functions.
-- Targets the confirmed-supported C# surface:
--   date_add / date_diff / date_trunc / date_part / extract
--   timestamp_add / timestamp_diff / timestamp_year / timestamp_month /
--   timestamp_day / timestamp_hour / timestamp_minute / timestamp_second /
--   timestamp_millisecond
--   make_date / make_timestamp / epoch_ms / to_epoch_ms / epoch_s
-- Interval constructor/conversion and arithmetic coverage now includes:
--   interval / duration / to_years / to_months / to_days / to_hours /
--   to_minutes / to_seconds / to_milliseconds / to_microseconds
--   date +/- interval / date - date / interval date_part for core fields
-- Remaining G-010 depth still includes broader native parity semantics.

-- QUERY: date_add_days
RETURN date_add('day', date('2024-01-01'), 10) AS result;

-- QUERY: date_add_months
RETURN date_add('month', date('2024-01-31'), 3) AS result;

-- QUERY: date_add_years
RETURN date_add('year', date('2020-02-29'), 4) AS result;

-- QUERY: date_add_hours
RETURN date_add('hour', date('2024-06-15'), 36) AS result;

-- QUERY: date_diff_days
RETURN date_diff('day', date('2024-01-01'), date('2024-03-15')) AS diff_days;

-- QUERY: date_diff_months
RETURN date_diff('month', date('2023-03-01'), date('2024-09-01')) AS diff_months;

-- QUERY: date_diff_years
RETURN date_diff('year', date('2000-01-01'), date('2024-01-01')) AS diff_years;

-- QUERY: date_diff_hours
RETURN date_diff('hour', date('2024-01-01'), date('2024-01-02')) AS diff_hours;

-- QUERY: date_trunc_year
RETURN date_trunc('year', date('2024-07-15')) AS truncated;

-- QUERY: date_trunc_month
RETURN date_trunc('month', date('2024-07-15')) AS truncated;

-- QUERY: date_part_year
RETURN date_part('year', date('2024-07-15')) AS y;

-- QUERY: date_part_month
RETURN date_part('month', date('2024-07-15')) AS m;

-- QUERY: date_part_day
RETURN date_part('day', date('2024-07-15')) AS d;

-- QUERY: date_part_quarter
RETURN date_part('quarter', date('2024-07-15')) AS q;

-- QUERY: extract_alias
RETURN extract('month', date('2024-07-15')) AS m;

-- QUERY: timestamp_add_hours
RETURN timestamp_add('hour', timestamp('2024-01-01T12:00:00'), 5) AS result;

-- QUERY: timestamp_add_days
RETURN timestamp_add('day', timestamp('2024-01-01T00:00:00'), 7) AS result;

-- QUERY: timestamp_add_minutes
RETURN timestamp_add('minute', timestamp('2024-01-01T10:00:00'), 90) AS result;

-- QUERY: timestamp_diff_hours
RETURN timestamp_diff('hour', timestamp('2024-01-01T06:00:00'), timestamp('2024-01-01T18:00:00')) AS diff_hours;

-- QUERY: timestamp_diff_days
RETURN timestamp_diff('day', timestamp('2024-01-01T00:00:00'), timestamp('2024-01-15T00:00:00')) AS diff_days;

-- QUERY: timestamp_year_part
RETURN timestamp_year(timestamp('2024-07-15T10:30:00')) AS y;

-- QUERY: timestamp_month_part
RETURN timestamp_month(timestamp('2024-07-15T10:30:00')) AS m;

-- QUERY: timestamp_day_part
RETURN timestamp_day(timestamp('2024-07-15T10:30:00')) AS d;

-- QUERY: timestamp_hour_part
RETURN timestamp_hour(timestamp('2024-07-15T10:30:00')) AS h;

-- QUERY: timestamp_minute_part
RETURN timestamp_minute(timestamp('2024-07-15T10:30:00')) AS mi;

-- QUERY: timestamp_second_part
RETURN timestamp_second(timestamp('2024-07-15T10:30:45')) AS s;

-- QUERY: timestamp_millisecond_part
RETURN timestamp_millisecond(timestamp('2024-07-15T10:30:45')) AS ms;

-- QUERY: make_date
RETURN make_date(2024, 7, 15) AS d;

-- QUERY: make_timestamp
RETURN make_timestamp(2024, 7, 15, 10, 30, 0) AS ts;

-- QUERY: epoch_ms_roundtrip
RETURN to_epoch_ms(timestamp('1970-01-01T00:00:01')) AS ms;

-- QUERY: epoch_s_roundtrip
RETURN epoch_s(0) AS epoch_start;

-- QUERY: date_component_extractors_alias
RETURN year(date('2024-07-15'))   AS y,
       month(date('2024-07-15'))  AS m,
       day(date('2024-07-15'))    AS d;

-- QUERY: date_trunc_day
RETURN date_trunc('day', timestamp('2024-07-15T14:32:10')) AS truncated;

-- QUERY: date_trunc_hour
RETURN date_trunc('hour', timestamp('2024-07-15T14:32:10')) AS truncated;

-- QUERY: date_diff_chained
RETURN date_diff('day', date_add('month', date('2024-01-01'), 6), date('2024-07-01')) AS gap_days;

-- QUERY: interval_constructor_days
RETURN interval('3 days') AS delta;

-- QUERY: interval_constructor_word_mix
RETURN duration('2 years 5 days') AS delta;

-- QUERY: interval_to_years
RETURN to_years(2) AS delta;

-- QUERY: date_plus_interval
RETURN date('2024-01-01') + interval('3 days') AS shifted;

-- QUERY: date_minus_date_interval
RETURN date('2024-01-10') - date('2024-01-01') AS delta;

-- QUERY: timestamp_minus_timestamp_interval
RETURN timestamp('2024-01-03T00:00:00Z') - timestamp('2024-01-01T00:00:00Z') AS delta;

-- QUERY: timestamp_plus_interval_preserves_time
RETURN timestamp('2024-01-01T12:00:00') + interval('2 days') AS shifted;

-- QUERY: timestamp_minus_interval_preserves_time
RETURN timestamp('2024-01-03T12:00:00') - interval('2 days') AS shifted;

-- QUERY: interval_plus_timestamp_preserves_time
RETURN interval('2 days') + timestamp('2024-01-01T12:00:00') AS shifted;

-- QUERY: timestamp_minus_timestamp_mixed_day_time
RETURN timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00') AS delta,
       date_part('day', timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00')) AS days,
       date_part('minute', timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00')) AS minutes;

-- QUERY: interval_negative_fractional_minutes
RETURN interval('-1.5 minutes') AS delta,
       date_part('minute', interval('-1.5 minutes')) AS minutes,
       date_part('second', interval('-1.5 minutes')) AS seconds;

-- QUERY: interval_date_part_year
RETURN date_part('year', to_years(4)) AS years;

-- QUERY: interval_date_part_quarter_week
RETURN date_part('quarter', to_months(14)) AS quarter,
       date_part('week', to_days(15)) AS week;

-- QUERY: interval_mixed_day_time_canonicalization
RETURN to_days(1) - to_hours(1) AS delta,
       date_part('day', to_days(1) - to_hours(1)) AS days,
       date_part('hour', to_days(1) - to_hours(1)) AS hours;

-- QUERY: interval_mixed_month_day_signs
RETURN to_months(1) - to_days(1) AS delta,
       date_part('month', to_months(1) - to_days(1)) AS months,
       date_part('day', to_months(1) - to_days(1)) AS days;

-- QUERY: interval_unary_negate
RETURN -to_days(2) AS delta,
       date_part('day', -to_days(2)) AS days;

-- QUERY: interval_negative_time_parts
RETURN -to_hours(2) AS delta,
       date_part('hour', -to_hours(2)) AS hours;

-- QUERY: interval_negative_cross_day_parts
RETURN -(to_hours(25) + to_minutes(30)) AS delta,
       date_part('day', -(to_hours(25) + to_minutes(30))) AS days,
       date_part('hour', -(to_hours(25) + to_minutes(30))) AS hours,
       date_part('minute', -(to_hours(25) + to_minutes(30))) AS minutes;

-- QUERY: interval_plus_interval_same_type
RETURN to_days(2) + to_days(3) AS delta,
       date_part('day', to_days(2) + to_days(3)) AS days;

-- QUERY: interval_plus_interval_mixed_month_day
RETURN to_months(1) + to_days(15) AS delta,
       date_part('month', to_months(1) + to_days(15)) AS months,
       date_part('day',   to_months(1) + to_days(15)) AS days;

-- QUERY: interval_plus_interval_time_overflow_to_days
RETURN to_hours(23) + to_hours(2) AS delta,
       date_part('day',  to_hours(23) + to_hours(2)) AS days,
       date_part('hour', to_hours(23) + to_hours(2)) AS hours;

-- QUERY: interval_date_minus_interval
RETURN date('2024-01-10') - interval('3 days') AS result;

-- QUERY: interval_to_minutes_hour_normalize
RETURN to_minutes(70) AS delta,
       date_part('hour',   to_minutes(70)) AS hours,
       date_part('minute', to_minutes(70)) AS minutes;

-- QUERY: interval_to_milliseconds_second_normalize
RETURN to_milliseconds(1500) AS delta,
       date_part('second',      to_milliseconds(1500)) AS seconds,
       date_part('millisecond', to_milliseconds(1500)) AS milliseconds;

-- QUERY: interval_to_seconds_minute_normalize
RETURN to_seconds(90) AS delta,
       date_part('minute', to_seconds(90)) AS minutes,
       date_part('second', to_seconds(90)) AS seconds;

-- QUERY: interval_reverse_mixed_sign
RETURN to_days(5) - to_months(1) AS delta,
       date_part('month', to_days(5) - to_months(1)) AS months,
       date_part('day',   to_days(5) - to_months(1)) AS days;

-- QUERY: interval_mixed_month_day_time_signs
RETURN to_months(1) - to_days(1) - to_hours(25) AS delta,
       date_part('month', to_months(1) - to_days(1) - to_hours(25)) AS months,
       date_part('day',   to_months(1) - to_days(1) - to_hours(25)) AS days,
       date_part('hour',  to_months(1) - to_days(1) - to_hours(25)) AS hours;

-- QUERY: interval_zero_value
RETURN to_seconds(0) AS delta;

-- QUERY: interval_iso_roundtrip
RETURN interval('P1Y2M3DT4H5M6S') AS delta;

-- QUERY: interval_cast_from_string
RETURN CAST('3 days' AS INTERVAL) AS delta;

-- QUERY: interval_list_nested_stringification
RETURN [to_days(1), to_hours(25), 'tag'] AS values;

-- QUERY: interval_map_nested_stringification
RETURN map(['day', 'hour'], [to_days(1), to_hours(25)]) AS parts;

-- QUERY: interval_struct_nested_stringification
RETURN struct_pack('day', to_days(1), 'hour', to_hours(25)) AS parts;

-- QUERY: interval_struct_deep_nested_stringification
RETURN struct_pack('nested', struct_pack('delta', to_days(1)), 'listy', [to_hours(25)]) AS parts;

-- QUERY: interval_list_structural_equality
RETURN [to_days(1), to_hours(25)] = [to_days(1), to_hours(25)] AS eq;

-- QUERY: interval_struct_structural_equality
RETURN struct_pack('day', to_days(1), 'hour', to_hours(25)) =
       struct_pack('day', to_days(1), 'hour', to_hours(25)) AS eq;

-- QUERY: interval_list_contains_nested_membership
RETURN list_contains([[to_days(1)], [to_days(2)]], [to_days(1)]) AS ok;

-- QUERY: interval_list_unique_nested_values
RETURN list_unique([[to_days(1)], [to_days(1)], [to_days(2)]]) AS vals;

-- QUERY: interval_distinct_nested_lists
UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals
WITH DISTINCT vals
RETURN vals
ORDER BY tostring(vals);

-- QUERY: interval_count_distinct_nested_lists
UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals
RETURN count(DISTINCT vals) AS n;

-- QUERY: interval_group_by_nested_lists
UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals
RETURN vals, count(*) AS n
ORDER BY tostring(vals);

-- QUERY: interval_order_by_nested_lists
UNWIND [[to_days(2)], [to_days(1)]] AS vals
RETURN vals
ORDER BY vals;

-- QUERY: interval_topk_nested_lists
UNWIND [[to_days(2)], [to_days(1)]] AS vals
RETURN vals
ORDER BY vals
LIMIT 1;

-- QUERY: interval_divide_integer
RETURN interval('3 years 2 days 13 hours 2 minutes') / 3 AS delta,
       date_part('year', interval('3 years 2 days 13 hours 2 minutes') / 3) AS y,
       date_part('hour', interval('3 years 2 days 13 hours 2 minutes') / 3) AS h,
       date_part('minute', interval('3 years 2 days 13 hours 2 minutes') / 3) AS m,
       date_part('second', interval('3 years 2 days 13 hours 2 minutes') / 3) AS s;

-- QUERY: interval_divide_negative_integer
RETURN -interval('3 days 3 hours') / 3 AS delta,
       date_part('day', -interval('3 days 3 hours') / 3) AS d,
       date_part('hour', -interval('3 days 3 hours') / 3) AS h;

-- QUERY: interval_extended_native_units
RETURN interval('3 quarter') AS quarter,
       interval('3 decade') AS decade,
       interval('3 century') AS century,
       interval('3 millennium') AS millennium;

-- QUERY: interval_fractional_month_family
RETURN interval('1.5 month') AS month,
       interval('1.5 year') AS year,
       interval('1.5 quarter') AS quarter;

-- QUERY: interval_time_literal_and_abbreviations
RETURN interval('35:10:00') AS time_only,
       interval('1 year 12:00:00') AS trailing_time,
       interval('2 yrs 3 mons 4 d 5 h 6 m 7 s 8 ms 9 us') AS abbreviated;

-- QUERY: interval_extended_calendar_parts
RETURN date_part('year', interval('1234 years 5 months')) AS y,
       date_part('decade', interval('1234 years 5 months')) AS dec,
       date_part('century', interval('1234 years 5 months')) AS c,
       date_part('millennium', interval('1234 years 5 months')) AS mil,
       date_part('month', interval('1234 years 5 months')) AS m;

-- QUERY: interval_ordered_comparisons
RETURN interval('1 month') <= interval('30 days') AS month_le_30_days,
       interval('1 month') >= interval('30 days') AS month_ge_30_days,
       interval('1 month') < interval('31 days') AS month_lt_31_days,
       interval('30 days 1 hour') > interval('1 month') AS thirty_days_hour_gt_month;

-- QUERY: interval_canonicalization_overflow
RETURN interval('36500 days 2400 hours 3600000 milliseconds');

-- QUERY: interval_mixed_sign_evaluations
RETURN interval('1 month - 20 days 5 hours'),
       interval('-1 year 2 months -10 days'),
       date('2026-06-15') + interval('1 month - 40 days');

-- QUERY: interval_leap_year_extrema
RETURN date('2024-02-29') + interval('1 year'),
       date('2024-02-29') + interval('4 years'),
       date('2026-01-31') + interval('1 month');
