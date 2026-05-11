-- corpus-errors-extended.cypher
-- Extended deterministic binder/planner/runtime snapshots.

-- SCHEMA
NODE Item  id:INT64  name:STRING  price:DOUBLE
REL  PAIRS  FROM:Item  TO:Item  tag:STRING
REL  CONTRACTS_WITH  FROM:Item  TO:Item  date_range:STRING
REL  RESPONSIBLE_FOR  FROM:Item  TO:Item  contract_dates:STRING

-- SETUP
CREATE (:Item {id:1, name:'Widget', price:9.99});
CREATE (:Item {id:2, name:'Gadget', price:19.99});
MATCH (a:Item {id:1}), (b:Item {id:2}) CREATE (a)-[:PAIRS {tag:'2025'}]->(b);
MATCH (a:Item {id:1}), (b:Item {id:2}) CREATE (a)-[:CONTRACTS_WITH {date_range:'2025'}]->(b);

-- QUERY: unknown_function
RETURN does_not_exist(42);

-- QUERY: invalid_regex
RETURN regexp_matches('hello', '[invalid[');

-- QUERY: divide_by_zero_int
RETURN 10 / 0;

-- QUERY: modulo_by_zero
RETURN 10 % 0;

-- QUERY: log_of_zero
RETURN log(0.0);

-- QUERY: log_of_negative
RETURN log(-1.0);

-- QUERY: sqrt_of_negative
RETURN sqrt(-4.0);

-- QUERY: unknown_rel_in_match
MATCH (a:Item)-[r:SELLS]->(b:Item) RETURN r;

-- QUERY: unknown_node_label_in_create
MATCH (a:Item) CREATE (a)-[:PAIRS]->(b:Ghost {id:99});

-- QUERY: property_on_unknown_label
MATCH (x:Unknown) RETURN x.id;

-- QUERY: aggregate_in_where
MATCH (i:Item) WHERE COUNT(i) > 0 RETURN i.id;

-- QUERY: nested_aggregate
RETURN COUNT(COUNT(1));

-- QUERY: where_not_pattern_atom
MATCH (a:Item), (b:Item)
WHERE NOT (a)-[:PAIRS]->(b)
RETURN a.id, b.id
ORDER BY a.id, b.id;

-- QUERY: where_not_pattern_atom_with_correlated_rel_property
MATCH (hp:Item)-[c:CONTRACTS_WITH]->(mg:Item)
MATCH (sc:Item)
WHERE NOT (mg)-[:RESPONSIBLE_FOR {contract_dates: c.date_range}]->(sc)
RETURN hp.id, mg.id, sc.id
ORDER BY hp.id, mg.id, sc.id;
