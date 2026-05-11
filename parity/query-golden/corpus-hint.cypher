-- SCHEMA
NODE Person id:INT64

-- SETUP
CREATE (:Person {id: 1});

-- QUERY: join_order_hint
MATCH (a:Person), (b:Person) HINT (a JOIN b) RETURN a.id, b.id;
