-- corpus-nested-types-depth.cypher
-- G-GOLD-018: Deeper nested type coverage extending corpus-types-nested.cypher.
-- C++ parity: nested_types/ test directory, user_defined_types/ test directory.
-- Targets: LIST of STRUCT, STRUCT-in-STRUCT, MAP of LIST, deep chaining,
--          null propagation in nested positions, map_keys/map_values functions,
--          list_transform over nested elements, struct field counting.

-- SCHEMA
-- (none — all queries use literal construction)

-- SETUP
RETURN 1 AS ready;

-- ── LIST of STRUCT ────────────────────────────────────────────────────────────

-- QUERY: list_of_struct_unwind_sort
UNWIND [
  struct_pack('name', 'Charlie', 'score', 70),
  struct_pack('name', 'Alice',   'score', 95),
  struct_pack('name', 'Bob',     'score', 82)
] AS s
RETURN struct_extract(s, 'name') AS name,
       struct_extract(s, 'score') AS score
ORDER BY score DESC;

-- QUERY: list_of_struct_count
RETURN list_len([
  struct_pack('x', 1),
  struct_pack('x', 2),
  struct_pack('x', 3)
]) AS n;

-- QUERY: list_of_struct_first_element
RETURN struct_extract(
  list_element([struct_pack('v', 10), struct_pack('v', 20)], 0),
  'v'
) AS first_v;

-- ── STRUCT-in-STRUCT ──────────────────────────────────────────────────────────

-- QUERY: nested_struct_two_deep
WITH struct_pack('inner', struct_pack('val', 42)) AS outer
RETURN struct_extract(struct_extract(outer, 'inner'), 'val') AS val;

-- QUERY: nested_struct_mixed_types
WITH struct_pack('meta', struct_pack('count', 3, 'label', 'foo'), 'flag', TRUE) AS s
RETURN struct_extract(struct_extract(s, 'meta'), 'label') AS label,
       struct_extract(s, 'flag') AS flag;

-- ── MAP of LIST ───────────────────────────────────────────────────────────────

-- QUERY: map_of_list_access
WITH map(['nums', 'letters'], [[1, 2, 3], ['a', 'b']]) AS m
RETURN list_element(list_element(map_extract(m, 'nums'), 0), 2) AS third_num;

-- QUERY: map_of_list_length
WITH map(['nums', 'letters'], [[1, 2, 3], ['a', 'b']]) AS m
RETURN list_len(list_element(map_extract(m, 'nums'), 0))   AS nums_len,
       list_len(list_element(map_extract(m, 'letters'), 0)) AS letters_len;

-- ── MAP utility functions ─────────────────────────────────────────────────────

-- QUERY: map_keys_sorted
UNWIND map_keys(map(['c', 'a', 'b'], [3, 1, 2])) AS k
RETURN k ORDER BY k;

-- QUERY: map_values_sum
RETURN list_sum(map_values(map(['x', 'y', 'z'], [10, 20, 30]))) AS total;

-- QUERY: map_size
RETURN map_len(map(['a', 'b', 'c'], [1, 2, 3])) AS sz;

-- ── Null within nested positions ──────────────────────────────────────────────

-- QUERY: list_with_null_element
RETURN list_element([1, NULL, 3], 1) IS NULL AS slot_is_null;

-- QUERY: struct_with_null_field
WITH struct_pack('a', 1, 'b', NULL) AS s
RETURN struct_extract(s, 'b') IS NULL AS b_null,
       struct_extract(s, 'a') AS a_val;

-- ── map_extract with missing key returns empty list ───────────────────────────

-- QUERY: map_extract_missing_key_empty
RETURN list_len(map_extract(map(['x'], [1]), 'y')) AS missing_len;

-- ── list_transform (lambda) over nested lists ─────────────────────────────────

-- QUERY: list_transform_double_elements
RETURN list_transform([1, 2, 3, 4], x -> x * 2) AS doubled;

-- QUERY: list_transform_extract_struct_field
UNWIND list_transform(
  [struct_pack('v', 1), struct_pack('v', 2), struct_pack('v', 3)],
  s -> struct_extract(s, 'v')
) AS v
RETURN v ORDER BY v;
