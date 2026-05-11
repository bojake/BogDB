-- corpus-errors.cypher
-- Intentionally failing queries with deterministic binder/runtime errors.

-- SCHEMA
NODE Person  id:INT64  name:STRING

-- SETUP
CREATE (:Person {id:1, name:'Alice'});

-- QUERY: commit_without_begin
COMMIT;

-- QUERY: rollback_without_begin
ROLLBACK;

-- QUERY: unknown_label
MATCH (p:Ghost) RETURN p.id;

-- QUERY: unknown_property
MATCH (p:Person) RETURN p.age;

-- QUERY: unknown_variable
MATCH (p:Person) RETURN x;

-- QUERY: unknown_relationship
MATCH (p:Person)-[r:KNOWS]->(q:Person) RETURN r;

-- QUERY: invalid_syntax
THIS IS NOT CYPHER;

-- QUERY: non_constant_skip
MATCH (p:Person) RETURN p.id SKIP p.id;

-- QUERY: non_constant_limit
MATCH (p:Person) RETURN p.id LIMIT p.id;

-- QUERY: distinct_order_by_not_projected
MATCH (p:Person) RETURN DISTINCT p.name ORDER BY p.id;

-- QUERY: compare_string_to_int
MATCH (p:Person) WHERE p.name > 1 RETURN p.id;

-- QUERY: factorial_negative
RETURN factorial(-1) AS bad_factorial;
