-- corpus-array-vector-functions.cypher
-- G-GOLD-003: Fixed-size array / vector scalar function coverage.
-- Targets the confirmed-supported C# surface in ArrayFunctions.cs:
--   array_value, array_extract/element, array_contains/has,
--   array_length, array_min, array_max, array_sum, array_avg,
--   array_inner_product / array_dot_product,
--   array_cosine_similarity, array_cosine_distance,
--   array_squared_distance, array_distance / array_l2_distance,
--   array_l1_distance, array_cross_product,
--   array_normalize, array_concat / array_cat,
--   array_push_front, array_push_back / array_append,
--   array_pop_front, array_pop_back,
--   array_reverse, array_unique

-- QUERY: array_value_ints
RETURN array_value(1, 2, 3, 4) AS arr;

-- QUERY: array_value_floats
RETURN array_value(1.5, 2.5, 3.5) AS arr;

-- QUERY: array_value_empty
RETURN array_value() AS arr;

-- QUERY: array_extract_first
RETURN array_extract(array_value(10, 20, 30), 1) AS v;

-- QUERY: array_extract_last
RETURN array_extract(array_value(10, 20, 30), 3) AS v;

-- QUERY: array_element_alias
RETURN array_element(array_value(100, 200, 300), 2) AS v;

-- QUERY: array_contains_true
RETURN array_contains(array_value(1, 2, 3, 4), 3) AS found;

-- QUERY: array_contains_false
RETURN array_contains(array_value(1, 2, 3, 4), 9) AS found;

-- QUERY: array_has_alias
RETURN array_has(array_value('a', 'b', 'c'), 'b') AS found;

-- QUERY: array_length
RETURN array_length(array_value(5, 10, 15, 20)) AS len;

-- QUERY: array_min
RETURN array_min(array_value(7.0, 2.0, 9.0, 1.0)) AS v;

-- QUERY: array_max
RETURN array_max(array_value(7.0, 2.0, 9.0, 1.0)) AS v;

-- QUERY: array_sum
RETURN array_sum(array_value(1.0, 2.0, 3.0, 4.0)) AS total;

-- QUERY: array_avg
RETURN array_avg(array_value(2.0, 4.0, 6.0, 8.0)) AS avg;

-- QUERY: array_inner_product
RETURN array_inner_product(array_value(1.0, 2.0, 3.0), array_value(4.0, 5.0, 6.0)) AS dot;

-- QUERY: array_dot_product_alias
RETURN array_dot_product(array_value(1.0, 0.0, 0.0), array_value(0.0, 1.0, 0.0)) AS dot;

-- QUERY: array_squared_distance
RETURN array_squared_distance(array_value(0.0, 0.0, 0.0), array_value(3.0, 4.0, 0.0)) AS sq_dist;

-- QUERY: array_distance
RETURN array_distance(array_value(0.0, 0.0), array_value(3.0, 4.0)) AS dist;

-- QUERY: array_l2_distance_alias
RETURN array_l2_distance(array_value(1.0, 1.0), array_value(4.0, 5.0)) AS dist;

-- QUERY: array_l1_distance
RETURN array_l1_distance(array_value(1.0, 2.0, 3.0), array_value(4.0, 5.0, 6.0)) AS dist;

-- QUERY: array_cosine_similarity_orthogonal
RETURN array_cosine_similarity(array_value(1.0, 0.0), array_value(0.0, 1.0)) AS sim;

-- QUERY: array_cosine_similarity_parallel
RETURN array_cosine_similarity(array_value(1.0, 0.0), array_value(2.0, 0.0)) AS sim;

-- QUERY: array_cosine_distance
RETURN array_cosine_distance(array_value(1.0, 0.0), array_value(1.0, 0.0)) AS dist;

-- QUERY: array_cross_product
RETURN array_cross_product(array_value(1.0, 0.0, 0.0), array_value(0.0, 1.0, 0.0)) AS cross;

-- QUERY: array_normalize
RETURN array_normalize(array_value(3.0, 4.0)) AS normed;

-- QUERY: array_concat
RETURN array_concat(array_value(1, 2), array_value(3, 4)) AS merged;

-- QUERY: array_cat_alias
RETURN array_cat(array_value('x', 'y'), array_value('z')) AS merged;

-- QUERY: array_push_front
RETURN array_push_front(array_value(2, 3, 4), 1) AS arr;

-- QUERY: array_push_back
RETURN array_push_back(array_value(1, 2, 3), 4) AS arr;

-- QUERY: array_append_alias
RETURN array_append(array_value(10, 20), 30) AS arr;

-- QUERY: array_pop_front
RETURN array_pop_front(array_value(1, 2, 3, 4)) AS arr;

-- QUERY: array_pop_back
RETURN array_pop_back(array_value(1, 2, 3, 4)) AS arr;

-- QUERY: array_reverse
RETURN array_reverse(array_value(1, 2, 3, 4, 5)) AS arr;

-- QUERY: array_unique
RETURN array_unique(array_value(1, 2, 2, 3, 3, 3, 4)) AS arr;
