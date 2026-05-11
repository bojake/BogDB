-- corpus-functions-advanced.cypher
-- Deterministic collection/date-arithmetic scalar coverage.

-- QUERY: list_sort_edges
RETURN list_element(list_sort(list_creation(10, 2, 9)), 0) AS smallest,
       list_element(list_reverse_sort(list_creation(10, 2, 9)), 0) AS largest;

-- QUERY: list_unique_length
RETURN list_len(list_unique(list_creation(1, 2, 2, 3, 3, 3))) AS len;

-- QUERY: list_contains_and_position
RETURN list_contains(list_creation('red', 'green', 'blue'), 'green') AS has_green,
       list_position(list_creation('red', 'green', 'blue'), 'blue') AS blue_pos;

-- QUERY: list_concat_access
RETURN list_element(list_concat(list_creation('a', 'b'), list_creation('c', 'd')), 2) AS third;

-- QUERY: list_transform_upper
RETURN list_element(list_transform(list_creation('alice', 'bob'), 'upper'), 1) AS second_upper;

-- QUERY: map_lookup_and_contains
RETURN list_element(map_extract(map(list_creation('a', 'b'), list_creation(10, 20)), 'b'), 0) AS value_b,
       map_contains(map(list_creation('a', 'b'), list_creation(10, 20)), 'a') AS has_a;

-- QUERY: struct_extract_and_key_count
RETURN struct_extract(struct_pack('name', 'alice', 'age', 30), 'age') AS age,
       list_len(keys(struct_pack('name', 'alice', 'age', 30))) AS key_count;

-- QUERY: date_and_timestamp_arithmetic
RETURN date_add('day', date('2024-01-01'), 5) AS plus_days,
       date_diff('day', date('2024-01-01'), date('2024-01-10')) AS diff_days,
       timestamp_add('hour', timestamp('2024-01-01T00:00:00Z'), 6) AS plus_hours;
