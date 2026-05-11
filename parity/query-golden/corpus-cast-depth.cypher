-- corpus-cast-depth.cypher
-- G-GOLD-004: Type cast function depth coverage.
-- Targets the confirmed-supported C# surface in CastFunctions.cs:
--   Integer casts: to_int8/16/32/64, to_uint8/16/32/64,
--                  int8/int16/int32/int64/integer/bigint/tinyint/smallint
--                  ubigint/uinteger/usmallint/utinyint aliases
--   Float casts:   to_float/to_double/to_float32, float/double/real/decimal aliases
--   Bool cast:     to_bool/tobool/boolean/bool
--   String cast:   to_string/tostring/str/string/varchar
--   Date cast:     to_date
--   Timestamp cast: to_timestamp
--   Generic:       cast(value, 'type'), try_cast
--   Type check:    typeof, pg_typeof
-- All casts are null-safe (return null instead of throwing on bad input).

-- QUERY: int64_from_string
RETURN to_int64('42') AS v;

-- QUERY: int64_from_float_string
RETURN to_int64('3.7') AS v;

-- QUERY: int32_from_int
RETURN to_int32(1000000) AS v;

-- QUERY: int16_from_int
RETURN to_int16(32000) AS v;

-- QUERY: int8_from_int
RETURN to_int8(100) AS v;

-- QUERY: uint64_from_int
RETURN to_uint64(999) AS v;

-- QUERY: uint32_from_int
RETURN to_uint32(500) AS v;

-- QUERY: uint16_from_int
RETURN to_uint16(65000) AS v;

-- QUERY: uint8_from_int
RETURN to_uint8(200) AS v;

-- QUERY: integer_alias
RETURN integer(42) AS v;

-- QUERY: bigint_alias
RETURN bigint(100) AS v;

-- QUERY: smallint_alias
RETURN smallint(1000) AS v;

-- QUERY: tinyint_alias
RETURN tinyint(50) AS v;

-- QUERY: double_from_int
RETURN to_double(7) AS v;

-- QUERY: double_from_string
RETURN to_double('3.14') AS v;

-- QUERY: float_from_int
RETURN to_float(42) AS v;

-- QUERY: float32_from_int
RETURN to_float32(123) AS v;

-- QUERY: bool_true_from_int
RETURN to_bool(1) AS v;

-- QUERY: bool_false_from_int
RETURN to_bool(0) AS v;

-- QUERY: bool_from_string_true
RETURN to_bool('true') AS v;

-- QUERY: bool_from_string_false
RETURN to_bool('false') AS v;

-- QUERY: string_from_int
RETURN to_string(42) AS v;

-- QUERY: string_from_double
RETURN to_string(3.14) AS v;

-- QUERY: string_from_bool
RETURN to_string(true) AS v;

-- QUERY: varchar_alias
RETURN varchar(100) AS v;

-- QUERY: str_alias
RETURN str(99) AS v;

-- QUERY: to_date_from_string
RETURN to_date('2024-07-15') AS v;

-- QUERY: to_timestamp_from_string
RETURN to_timestamp('2024-07-15T10:00:00') AS v;

-- QUERY: cast_to_int64
RETURN cast(42.9, 'int64') AS v;

-- QUERY: cast_to_double
RETURN cast(7, 'double') AS v;

-- QUERY: cast_to_bool
RETURN cast(1, 'bool') AS v;

-- QUERY: cast_to_string
RETURN cast(123, 'string') AS v;

-- QUERY: cast_to_date
RETURN cast('2024-01-15', 'date') AS v;

-- QUERY: try_cast_alias
RETURN try_cast(99, 'int64') AS v;

-- QUERY: null_propagation_int
RETURN to_int64(NULL) AS v;

-- QUERY: null_propagation_double
RETURN to_double(NULL) AS v;

-- QUERY: null_propagation_bool
RETURN to_bool(NULL) AS v;

-- QUERY: null_propagation_string
RETURN to_string(NULL) AS v;

-- QUERY: typeof_int
RETURN typeof(42) AS t;

-- QUERY: typeof_double
RETURN typeof(3.14) AS t;

-- QUERY: typeof_string
RETURN typeof('hello') AS t;

-- QUERY: typeof_bool
RETURN typeof(true) AS t;

-- QUERY: typeof_null
RETURN typeof(NULL) AS t;

-- QUERY: pg_typeof_alias
RETURN pg_typeof(100) AS t;

-- QUERY: int_int_chain
RETURN to_int32(to_int64('255')) AS v;

-- QUERY: double_to_string_to_double
RETURN to_double(to_string(2.71828)) AS v;
