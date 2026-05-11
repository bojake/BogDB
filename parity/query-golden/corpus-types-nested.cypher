-- corpus-types-nested.cypher
-- Deterministic nested LIST/STRUCT/MAP query coverage using literals and functions.

-- QUERY: list_literal_access
RETURN list_element([10, 20, 30], 1) AS second,
       list_len([10, 20, 30]) AS len;

-- QUERY: list_concat_and_contains
RETURN list_element(list_concat([1, 2], [3, 4]), 3) AS fourth,
       list_contains([1, 2, 3, 4], 2) AS has_two;

-- QUERY: map_extract_scalar
RETURN list_element(map_extract(map(['name', 'age'], ['alice', 30]), 'age'), 0) AS age;

-- QUERY: struct_extract_scalar
RETURN struct_extract(struct_pack('name', 'alice', 'age', 30), 'name') AS name,
       struct_extract(struct_pack('name', 'alice', 'age', 30), 'age') AS age;

-- QUERY: struct_keys_sorted
UNWIND keys(struct_pack('name', 'alice', 'age', 30, 'city', 'la')) AS k
RETURN k ORDER BY k;

-- QUERY: unwind_list_of_structs
UNWIND [struct_pack('name', 'alice', 'score', 10), struct_pack('name', 'bob', 'score', 20)] AS s
RETURN struct_extract(s, 'name') AS name, struct_extract(s, 'score') AS score ORDER BY score;

-- QUERY: unwind_list_of_maps
UNWIND [map(['id', 'name'], [1, 'a']), map(['id', 'name'], [2, 'b'])] AS m
RETURN list_element(map_extract(m, 'id'), 0) AS id,
       list_element(map_extract(m, 'name'), 0) AS name
ORDER BY id;

-- QUERY: nested_map_list_access
WITH map(['tags'], [[ 'red', 'blue' ]]) AS m
RETURN list_element(list_element(map_extract(m, 'tags'), 0), 1) AS second_tag;

-- QUERY: struct_literal_extract
RETURN struct_extract({name:'alice', age:30}, 'name') AS name,
       struct_extract({name:'alice', age:30}, 'age') AS age;

-- QUERY: struct_literal_keys
UNWIND keys({name:'alice', age:30, city:'la'}) AS k
RETURN k ORDER BY k;

-- QUERY: quantifier_truth_table
RETURN ALL(x IN [1, 2, 3] WHERE x > 0) AS all_positive,
       ANY(x IN [1, 2, 3] WHERE x = 2) AS any_two,
       NONE(x IN [1, 2, 3] WHERE x < 0) AS none_negative,
       SINGLE(x IN [1, 2, 3] WHERE x = 2) AS single_two,
       ALL(x IN [] WHERE x > 0) AS all_empty,
       ANY(x IN [] WHERE x > 0) AS any_empty,
       NONE(x IN [] WHERE x > 0) AS none_empty,
       SINGLE(x IN [] WHERE x > 0) AS single_empty;
