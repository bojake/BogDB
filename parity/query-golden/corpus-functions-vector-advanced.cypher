-- corpus-functions-vector-advanced.cypher
-- Broader deterministic vector/array function coverage and edge cases.

-- QUERY: vector_l1_distance
RETURN array_l1_distance(array_value(1.0, 2.0, 3.0), array_value(4.0, 0.0, 3.0)) AS l1;

-- QUERY: vector_squared_distance
RETURN array_squared_distance(array_value(1.0, 2.0), array_value(4.0, 6.0)) AS sq;

-- QUERY: vector_cosine_distance
RETURN array_cosine_distance(array_value(1.0, 0.0), array_value(0.0, 1.0)) AS cosine_dist;

-- QUERY: vector_normalize_zero
RETURN list_element(array_normalize(array_value(0.0, 0.0)), 1) AS first_zero,
       list_element(array_normalize(array_value(0.0, 0.0)), 2) AS second_zero;

-- QUERY: array_aggregate_stats
RETURN array_min(array_value(4.0, 1.0, 9.0)) AS min_v,
       array_max(array_value(4.0, 1.0, 9.0)) AS max_v,
       array_sum(array_value(4.0, 1.0, 9.0)) AS sum_v,
       array_avg(array_value(4.0, 1.0, 9.0)) AS avg_v;

-- QUERY: array_contains_length_extract
RETURN array_contains(array_value(10, 20, 30), 20) AS has_twenty,
       array_length(array_value(10, 20, 30)) AS len,
       array_extract(array_value(10, 20, 30), 3) AS third;

-- QUERY: array_push_and_pop
RETURN array_extract(array_pop_front(array_push_front(array_value(2, 3), 1)), 1) AS first_after_pop_front,
       array_extract(array_pop_back(array_push_back(array_value(1, 2), 3)), 2) AS second_after_pop_back;

-- QUERY: array_unique_and_reverse
RETURN array_length(array_unique(array_value(1, 2, 2, 3, 3))) AS unique_len,
       array_extract(array_reverse(array_value(1, 2, 3)), 1) AS reversed_first;
