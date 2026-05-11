-- corpus-blob-functions.cypher
-- G-GOLD-002: Blob scalar function coverage.
-- Targets the confirmed-supported C# surface:
--   octet_length(blob/string) — byte count
--   encode(string)            — string → blob (UTF-8 bytes)
--   decode(blob/string)       — blob → UTF-8 string (throws on invalid bytes)
-- Out of scope: BLOB() / TO_BLOB() constructors and blob comparisons are
--   type-level operations, not scalar functions in FunctionDispatcher.
--   See parity/unsupported-tracker.md for BLOB type depth.

-- QUERY: octet_length_ascii
RETURN octet_length('hello') AS len;

-- QUERY: octet_length_empty
RETURN octet_length('') AS len;

-- QUERY: octet_length_multibyte
RETURN octet_length('café') AS len;

-- QUERY: octet_length_null
RETURN octet_length(NULL) AS len;

-- QUERY: encode_ascii
RETURN encode('hello') AS b;

-- QUERY: encode_empty
RETURN encode('') AS b;

-- QUERY: encode_special_chars
RETURN encode('foo bar baz') AS b;

-- QUERY: decode_ascii
RETURN decode('hello world') AS s;

-- QUERY: decode_empty
RETURN decode('') AS s;

-- QUERY: decode_round_trip
RETURN decode(encode('round trip')) AS s;

-- QUERY: decode_utf8_valid
RETURN decode(encode('caf\u00e9')) AS s;

-- QUERY: octet_length_after_encode
RETURN octet_length(encode('test')) AS len;
