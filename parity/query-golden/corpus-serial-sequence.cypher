-- corpus-serial-sequence.cypher
-- G-GOLD-009: Sequence function coverage.
-- NOTE: The C++ serial.test tests serial *property* auto-increment in graph data
--   (tinysnb-serial dataset). In the C# port, the equivalent surface is the
--   in-memory sequence scalar functions: nextval, currval, setval.
--   There is no CREATE SEQUENCE DDL support (see G-011 pattern — DDL is unrouted).
--
-- Confirmed-supported C# surface (SequenceFunctions.cs):
--   nextval(name)           — auto-creates sequence; increments and returns current value
--   currval(name)           — returns current value without incrementing (null before any nextval)
--   setval(name, value)     — resets sequence counter; returns the new value
--
-- SCHEMA
-- (none required — these are pure scalar functions)

-- SETUP
RETURN 1 AS setup_complete;

-- QUERY: nextval_first_call
RETURN nextval('seq_a') AS v;

-- QUERY: nextval_second_call
RETURN nextval('seq_a') AS v;

-- QUERY: nextval_third_call
RETURN nextval('seq_a') AS v;

-- QUERY: currval_after_nextval
RETURN currval('seq_a') AS v;

-- QUERY: currval_before_any_nextval
RETURN currval('seq_b') AS v;

-- QUERY: nextval_independent_sequences
RETURN nextval('seq_c') AS c, nextval('seq_d') AS d;

-- QUERY: setval_reset
RETURN setval('seq_e', 100) AS v;

-- QUERY: nextval_after_setval
RETURN nextval('seq_e') AS v;

-- QUERY: currval_after_setval_nextval
RETURN currval('seq_e') AS v;

-- QUERY: setval_to_zero
RETURN setval('seq_f', 0) AS v;

-- QUERY: nextval_from_zero
RETURN nextval('seq_f') AS v;

-- QUERY: nextval_is_not_null
RETURN nextval('seq_g') IS NOT NULL AS has_value;

-- QUERY: nextval_returns_integer
RETURN nextval('seq_h') > 0 OR nextval('seq_h') = 1 AS ok;
