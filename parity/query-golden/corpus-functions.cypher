-- corpus-functions.cypher
-- Deterministic scalar function coverage for utility/string/cast-adjacent behavior.

-- QUERY: coalesce_basic
RETURN coalesce(NULL, 'fallback') AS v;

-- QUERY: ifnull_basic
RETURN ifnull(NULL, 42) AS v;

-- QUERY: nullif_equal
RETURN nullif(5, 5) AS v;

-- QUERY: typeof_int64
RETURN typeof(42) AS t;

-- QUERY: printf_integer
RETURN printf('Value: %d', 42) AS v;

-- QUERY: base64_roundtrip
RETURN base64_decode(base64_encode('Hello BogDb')) AS v;

-- QUERY: hex_roundtrip
RETURN from_hex(to_hex(255)) AS v;

-- QUERY: bit_and_octet_length
RETURN bit_length('abc') AS bits, octet_length('abc') AS octets;
