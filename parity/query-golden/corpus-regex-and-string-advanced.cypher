-- corpus-regex-and-string-advanced.cypher
-- G-GOLD-006: Advanced string and regex function coverage.
-- Targets functions NOT already covered in corpus-string-functions.cypher.
-- C# surface (StringFunctions.cs):
--   levenshtein / editdistance
--   initcap
--   concat_ws
--   split_part
--   printf / format / format_string (%s %d %f %x %o %e %%)
--   regexp_full_match, regexp_extract, regexp_extract_all
--   regexp_replace, regexp_split_to_array
--   base64_encode / base64_decode
--   url_encode / url_decode
--   to_hex / from_hex
--   bit_length, octet_length (string overload)
--   position / locate / instr
--   left, right
--   chr, ascii / unicode

-- ── Edit distance ─────────────────────────────────────────────────────────────

-- QUERY: levenshtein_same
RETURN levenshtein('kitten', 'kitten') AS dist;

-- QUERY: levenshtein_diff
RETURN levenshtein('kitten', 'sitting') AS dist;

-- QUERY: levenshtein_empty
RETURN levenshtein('', 'abc') AS dist;

-- QUERY: editdistance_alias
RETURN editdistance('abc', 'aXc') AS dist;

-- ── Title case ────────────────────────────────────────────────────────────────

-- QUERY: initcap_basic
RETURN initcap('hello world') AS v;

-- QUERY: initcap_mixed
RETURN initcap('UPPER lower MIXED') AS v;

-- ── concat_ws ────────────────────────────────────────────────────────────────

-- QUERY: concat_ws_comma
RETURN concat_ws(', ', 'Alice', 'Bob', 'Carol') AS v;

-- QUERY: concat_ws_dash
RETURN concat_ws('-', '2024', '07', '15') AS v;

-- QUERY: concat_ws_empty_sep
RETURN concat_ws('', 'foo', 'bar') AS v;

-- ── split_part ────────────────────────────────────────────────────────────────

-- QUERY: split_part_first
RETURN split_part('a,b,c', ',', 1) AS v;

-- QUERY: split_part_middle
RETURN split_part('a,b,c', ',', 2) AS v;

-- QUERY: split_part_last
RETURN split_part('a,b,c', ',', 3) AS v;

-- ── printf / format ───────────────────────────────────────────────────────────

-- QUERY: printf_string
RETURN printf('Hello, %s!', 'World') AS v;

-- QUERY: printf_int
RETURN printf('Count: %d', 42) AS v;

-- QUERY: printf_float
RETURN printf('Pi: %.2f', 3.14159) AS v;

-- QUERY: printf_hex
RETURN printf('Hex: %x', 255) AS v;

-- QUERY: printf_octal
RETURN printf('Oct: %o', 8) AS v;

-- QUERY: printf_percent_escape
RETURN printf('100%%') AS v;

-- QUERY: printf_multi_arg
RETURN printf('%s is %d years old', 'Alice', 30) AS v;

-- QUERY: format_alias
RETURN format('%d + %d = %d', 1, 2, 3) AS v;

-- ── Regex extended ────────────────────────────────────────────────────────────

-- QUERY: regexp_full_match_true
RETURN regexp_full_match('hello123', '[a-z]+[0-9]+') AS v;

-- QUERY: regexp_full_match_false
RETURN regexp_full_match('hello123!', '[a-z]+[0-9]+') AS v;

-- QUERY: regexp_replace_basic
RETURN regexp_replace('foo bar foo', 'foo', 'baz') AS v;

-- QUERY: regexp_replace_digits
RETURN regexp_replace('price: 42 dollars', '[0-9]+', 'N') AS v;

-- QUERY: regexp_extract_group0
RETURN regexp_extract('2024-07-15', '[0-9]+', 0) AS v;

-- QUERY: regexp_extract_no_match
RETURN regexp_extract('hello', '[0-9]+', 0) AS v;

-- QUERY: regexp_split_to_array
RETURN regexp_split_to_array('one two  three', '\\s+') AS v;

-- ── Encoding ──────────────────────────────────────────────────────────────────

-- QUERY: base64_encode
RETURN base64_encode('hello') AS v;

-- QUERY: base64_decode
RETURN base64_decode('aGVsbG8=') AS v;

-- QUERY: base64_roundtrip
RETURN base64_decode(base64_encode('round trip test')) AS v;

-- QUERY: url_encode
RETURN url_encode('hello world & more') AS v;

-- QUERY: url_decode
RETURN url_decode('hello%20world%20%26%20more') AS v;

-- QUERY: to_hex_255
RETURN to_hex(255) AS v;

-- QUERY: to_hex_16
RETURN to_hex(16) AS v;

-- QUERY: from_hex
RETURN from_hex('ff') AS v;

-- QUERY: hex_roundtrip
RETURN from_hex(to_hex(1234)) AS v;

-- ── Byte/bit length ───────────────────────────────────────────────────────────

-- QUERY: bit_length_ascii
RETURN bit_length('hello') AS v;

-- QUERY: octet_length_string
RETURN octet_length('hello') AS v;

-- ── Position / location ───────────────────────────────────────────────────────

-- QUERY: position_found
RETURN position('hello world', 'world') AS v;

-- QUERY: position_not_found
RETURN position('hello', 'xyz') AS v;

-- QUERY: locate_alias
RETURN locate('abcdef', 'cd') AS v;

-- QUERY: instr_alias
RETURN instr('foobar', 'bar') AS v;

-- ── Left / right ──────────────────────────────────────────────────────────────

-- QUERY: left_basic
RETURN left('hello world', 5) AS v;

-- QUERY: right_basic
RETURN right('hello world', 5) AS v;

-- QUERY: left_exceeds
RETURN left('hi', 100) AS v;

-- ── Character codes ───────────────────────────────────────────────────────────

-- QUERY: ascii_basic
RETURN ascii('A') AS v;

-- QUERY: unicode_alias
RETURN unicode('Z') AS v;

-- QUERY: chr_basic
RETURN chr(65) AS v;

-- QUERY: chr_roundtrip
RETURN chr(ascii('Q')) AS v;
