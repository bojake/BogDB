-- corpus-uuid-and-hash.cypher
-- G-GOLD-007: UUID generation and hash function coverage.
-- Targets the confirmed-supported C# surface in UuidFunctions.cs and UtilityFunctions.cs:
--   UUID: gen_random_uuid, uuid, uuid_to_string, string_to_uuid, uuid_version
--   Hash: hash (GetHashCode-based int64), md5, sha256, sha1, crc32
-- NOTE: gen_random_uuid() and uuid() produce non-deterministic output.
--   Those queries assert the result type/format, not exact value.

-- ── UUID construction / parsing ───────────────────────────────────────────────

-- QUERY: uuid_to_string_roundtrip
RETURN uuid_to_string(string_to_uuid('550e8400-e29b-41d4-a716-446655440000')) AS v;

-- QUERY: string_to_uuid_canonical
RETURN string_to_uuid('550E8400-E29B-41D4-A716-446655440000') AS v;

-- QUERY: string_to_uuid_invalid
RETURN string_to_uuid('not-a-uuid') AS v;

-- QUERY: uuid_version_v4
RETURN uuid_version('550e8400-e29b-41d4-a716-446655440000') AS v;

-- QUERY: uuid_to_string_length
RETURN length(uuid_to_string('550e8400-e29b-41d4-a716-446655440000')) AS len;

-- ── MD5 ───────────────────────────────────────────────────────────────────────

-- QUERY: md5_empty
RETURN md5('') AS v;

-- QUERY: md5_hello
RETURN md5('hello') AS v;

-- QUERY: md5_known
RETURN md5('The quick brown fox jumps over the lazy dog') AS v;

-- QUERY: md5_length
RETURN length(md5('test')) AS len;

-- ── SHA256 ────────────────────────────────────────────────────────────────────

-- QUERY: sha256_empty
RETURN sha256('') AS v;

-- QUERY: sha256_hello
RETURN sha256('hello') AS v;

-- QUERY: sha256_known
RETURN sha256('The quick brown fox jumps over the lazy dog') AS v;

-- QUERY: sha256_length
RETURN length(sha256('test')) AS len;

-- ── SHA1 ─────────────────────────────────────────────────────────────────────

-- QUERY: sha1_empty
RETURN sha1('') AS v;

-- QUERY: sha1_hello
RETURN sha1('hello') AS v;

-- QUERY: sha1_known
RETURN sha1('The quick brown fox jumps over the lazy dog') AS v;

-- QUERY: sha1_length
RETURN length(sha1('test')) AS len;

-- ── CRC32 ────────────────────────────────────────────────────────────────────

-- QUERY: crc32_empty
RETURN crc32('') AS v;

-- QUERY: crc32_hello
RETURN crc32('hello') AS v;

-- QUERY: crc32_type
RETURN crc32('test') IS NOT NULL AS has_value;

-- ── Hash (GetHashCode-based) ──────────────────────────────────────────────────

-- QUERY: hash_int
RETURN hash(42) IS NOT NULL AS has_value;

-- QUERY: hash_string
RETURN hash('hello') IS NOT NULL AS has_value;

-- QUERY: hash_same_value
RETURN hash(100) = hash(100) AS consistent;

-- QUERY: hash_null
RETURN hash(NULL) AS v;

-- ── MD5/SHA256 determinism ────────────────────────────────────────────────────

-- QUERY: md5_deterministic
RETURN md5('bogdb') = md5('bogdb') AS same;

-- QUERY: sha256_deterministic
RETURN sha256('bogdb') = sha256('bogdb') AS same;

-- QUERY: sha1_deterministic
RETURN sha1('bogdb') = sha1('bogdb') AS same;
