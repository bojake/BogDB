-- corpus-filter.cypher
-- Predicate expressiveness: arithmetic, string ops, boolean logic.

-- SCHEMA
NODE Item  id:INT64  name:STRING  price:INT64  qty:INT64

-- SETUP
CREATE (:Item {id:1,  name:'Apple',    price:3,  qty:100});
CREATE (:Item {id:2,  name:'Banana',   price:1,  qty:200});
CREATE (:Item {id:3,  name:'Cherry',   price:8,  qty:50});
CREATE (:Item {id:4,  name:'Apricot',  price:5,  qty:75});
CREATE (:Item {id:5,  name:'Blueberry',price:12, qty:30});
CREATE (:Item {id:6,  name:'Citrus',   price:2,  qty:150});

-- QUERY: arithmetic_gt
MATCH (i:Item) WHERE i.price > 5 RETURN i.name ORDER BY i.name;

-- QUERY: arithmetic_between
MATCH (i:Item) WHERE i.price >= 2 AND i.price <= 5 RETURN i.name ORDER BY i.name;

-- QUERY: arithmetic_computed
MATCH (i:Item) RETURN i.name, i.price * i.qty AS total ORDER BY total DESC;

-- QUERY: string_starts_with
MATCH (i:Item) WHERE i.name STARTS WITH 'A' RETURN i.name ORDER BY i.name;

-- QUERY: string_ends_with
MATCH (i:Item) WHERE i.name ENDS WITH 'y' RETURN i.name ORDER BY i.name;

-- QUERY: string_contains
MATCH (i:Item) WHERE i.name CONTAINS 'rr' RETURN i.name ORDER BY i.name;

-- QUERY: boolean_or
MATCH (i:Item) WHERE i.price < 2 OR i.price > 10 RETURN i.name ORDER BY i.name;

-- QUERY: boolean_not
MATCH (i:Item) WHERE NOT i.price > 5 RETURN i.name ORDER BY i.name;
