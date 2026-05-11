-- corpus-temporal.cypher
-- Deterministic temporal/date/timestamp scalar function coverage.

-- QUERY: date_parse
RETURN date('2024-03-19') AS d;

-- QUERY: timestamp_parse
RETURN timestamp('2024-03-19T10:30:00Z') AS ts;

-- QUERY: make_date_basic
RETURN make_date(2024, 3, 19) AS d;

-- QUERY: make_timestamp_basic
RETURN make_timestamp(2024, 3, 19, 10, 30, 0) AS ts;

-- QUERY: date_part_quarter
RETURN date_part('quarter', '2024-01-15') AS q;

-- QUERY: date_trunc_month
RETURN date_trunc('month', '2024-03-19') AS d;

-- QUERY: to_epoch_ms_basic
RETURN to_epoch_ms('2024-01-01 00:00:00') AS ms;

-- QUERY: timestamp_parts
RETURN timestamp_year('2026-03-20 12:00:00') AS y, timestamp_month('2026-03-20 12:00:00') AS m;
