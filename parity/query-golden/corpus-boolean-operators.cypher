-- corpus-boolean-operators.cypher
-- G-GOLD-005: Boolean operator and three-valued logic coverage.
-- Targets the confirmed-supported C# surface:
--   AND, OR, NOT, XOR as ExpressionType operators in the evaluator
--   IS NULL, IS NOT NULL as parser-native expressions
--   bool_and, bool_or, bool_xor as scalar functions (UtilityFunctions.cs)
--   Three-valued logic: TRUE, FALSE, NULL semantics per ISO/SQL standard

-- ── AND truth table ──────────────────────────────────────────────────────────

-- QUERY: and_true_true
RETURN true AND true AS v;

-- QUERY: and_true_false
RETURN true AND false AS v;

-- QUERY: and_false_true
RETURN false AND true AS v;

-- QUERY: and_false_false
RETURN false AND false AS v;

-- QUERY: and_true_null
RETURN true AND NULL AS v;

-- QUERY: and_false_null
RETURN false AND NULL AS v;

-- QUERY: and_null_true
RETURN NULL AND true AS v;

-- QUERY: and_null_false
RETURN NULL AND false AS v;

-- QUERY: and_null_null
RETURN NULL AND NULL AS v;

-- ── OR truth table ───────────────────────────────────────────────────────────

-- QUERY: or_true_true
RETURN true OR true AS v;

-- QUERY: or_true_false
RETURN true OR false AS v;

-- QUERY: or_false_false
RETURN false OR false AS v;

-- QUERY: or_true_null
RETURN true OR NULL AS v;

-- QUERY: or_false_null
RETURN false OR NULL AS v;

-- QUERY: or_null_null
RETURN NULL OR NULL AS v;

-- ── NOT ──────────────────────────────────────────────────────────────────────

-- QUERY: not_true
RETURN NOT true AS v;

-- QUERY: not_false
RETURN NOT false AS v;

-- QUERY: not_null
RETURN NOT NULL AS v;

-- ── XOR ──────────────────────────────────────────────────────────────────────

-- QUERY: xor_true_true
RETURN true XOR true AS v;

-- QUERY: xor_true_false
RETURN true XOR false AS v;

-- QUERY: xor_false_true
RETURN false XOR true AS v;

-- QUERY: xor_false_false
RETURN false XOR false AS v;

-- ── IS NULL / IS NOT NULL ────────────────────────────────────────────────────

-- QUERY: is_null_on_null
RETURN NULL IS NULL AS v;

-- QUERY: is_null_on_value
RETURN 42 IS NULL AS v;

-- QUERY: is_not_null_on_value
RETURN 'hello' IS NOT NULL AS v;

-- QUERY: is_not_null_on_null
RETURN NULL IS NOT NULL AS v;

-- ── Compound expressions ─────────────────────────────────────────────────────

-- QUERY: compound_and_or
RETURN (true AND false) OR true AS v;

-- QUERY: compound_not_and
RETURN NOT (true AND false) AS v;

-- QUERY: compound_not_or
RETURN NOT (false OR false) AS v;

-- QUERY: null_short_circuit_and
RETURN (false AND NULL) AS v;

-- QUERY: null_short_circuit_or
RETURN (true OR NULL) AS v;

-- ── Scalar function aliases ───────────────────────────────────────────────────

-- QUERY: bool_and_fn
RETURN bool_and(true, true) AS v;

-- QUERY: bool_and_fn_false
RETURN bool_and(true, false) AS v;

-- QUERY: bool_or_fn
RETURN bool_or(false, true) AS v;

-- QUERY: bool_or_fn_false
RETURN bool_or(false, false) AS v;

-- QUERY: bool_xor_fn
RETURN bool_xor(true, false) AS v;

-- QUERY: bool_xor_fn_same
RETURN bool_xor(true, true) AS v;
