-- corpus-functions-json-vector.cypher
-- Deterministic vector-function and JSON table-function coverage.

-- QUERY: vector_inner_product
RETURN array_inner_product(array_value(1.0, 2.0, 3.0), array_value(4.0, 5.0, 6.0)) AS dot;

-- QUERY: vector_cosine_similarity
RETURN array_cosine_similarity(array_value(1.0, 0.0, 0.0), array_value(1.0, 0.0, 0.0)) AS cosine;

-- QUERY: vector_distance
RETURN array_distance(array_value(1.0, 0.0), array_value(0.0, 1.0)) AS distance;

-- QUERY: vector_cross_product_z
RETURN list_element(array_cross_product(array_value(1.0, 0.0, 0.0), array_value(0.0, 1.0, 0.0)), 3) AS z;

-- QUERY: load_json_prim_rows
LOAD FROM 'dataset/json-misc/prim-test.json' RETURN *;

-- QUERY: load_json_array_rows
LOAD FROM 'dataset/json-misc/array-test.json' RETURN *;

-- QUERY: load_json_object_rows
LOAD FROM 'dataset/json-misc/obj-test.json' RETURN *;

-- QUERY: load_json_ndjson_rows
LOAD FROM 'dataset/json-misc/newline-delimited.json' RETURN *;
