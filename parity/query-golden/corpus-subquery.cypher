-- corpus-subquery.cypher
-- G-GOLD-010: EXISTS and COUNT subquery coverage.
-- Covers correlated EXISTS { MATCH ... } and COUNT { MATCH ... } evaluation.
-- C++ parity target: test/test_files/subquery/exists.test, count.test,
-- correlated.test, multi_label.test

-- SCHEMA
NODE Person  id:STRING  fName:STRING
REL  KNOWS   FROM:Person  TO:Person

-- SETUP
CREATE (:Person {id:'a', fName:'Alice'});
CREATE (:Person {id:'b', fName:'Bob'});
CREATE (:Person {id:'c', fName:'Cara'});
MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b);

-- QUERY: exists_subquery_in_where
MATCH (a:Person) WHERE EXISTS { MATCH (a)-[:KNOWS]->(b:Person) } RETURN COUNT(*);

-- QUERY: count_subquery_in_return
MATCH (a:Person) RETURN a.id, COUNT { MATCH (a)-[:KNOWS]->(b:Person) } AS degree ORDER BY a.id;

-- QUERY: not_exists_subquery
MATCH (a:Person) WHERE NOT EXISTS { MATCH (a)-[:KNOWS]->(b:Person) } RETURN COUNT(*);

-- QUERY: exists_with_filter
MATCH (a:Person) WHERE EXISTS { MATCH (a)-[:KNOWS]->(b:Person) WHERE b.fName = 'Bob' } RETURN a.fName;

-- QUERY: correlated_multi_hop_exists
MATCH (a:Person) WHERE EXISTS { MATCH (a)-[:KNOWS]->(:Person)-[:KNOWS]->(c:Person) } RETURN a.fName ORDER BY a.fName;

-- QUERY: nested_filter_on_outer_binding
MATCH (a:Person), (b:Person) WHERE a.id <> b.id AND EXISTS { MATCH (a)-[:KNOWS]->(c:Person) WHERE c.id = b.id } RETURN a.fName, b.fName ORDER BY a.fName, b.fName;

-- QUERY: count_with_grouped_aggregate
MATCH (a:Person) RETURN a.fName, COUNT { MATCH (a)-[:KNOWS]->(b:Person) WHERE b.fName STARTS WITH 'B' } AS b_friends ORDER BY a.fName;

-- QUERY: multiple_subqueries
MATCH (a:Person)
WHERE EXISTS { MATCH (a)-[:KNOWS]->(b:Person) }
  AND COUNT { MATCH (a)-[:KNOWS]->(c:Person) } > 0
RETURN a.fName ORDER BY a.fName;
