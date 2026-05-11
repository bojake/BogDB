-- SCHEMA
NODE Person id:INT64 name:STRING

-- SETUP
CREATE (p:Person {id:1, name:'Alice'});
CREATE (p:Person {id:2, name:'Bob'});

-- QUERY: explain_match
EXPLAIN MATCH (p:Person) RETURN p.name;

-- QUERY: explain_logical_match
EXPLAIN LOGICAL MATCH (p:Person) RETURN p.name;
