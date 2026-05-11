-- corpus-recursive.cypher
-- Variable-length path patterns over a chain + shortcut graph.

-- SCHEMA
NODE Node   id:INT64  label:STRING
REL  EDGE   FROM:Node  TO:Node  weight:INT64

-- SETUP
CREATE (:Node {id:1, label:'A'});
CREATE (:Node {id:2, label:'B'});
CREATE (:Node {id:3, label:'C'});
CREATE (:Node {id:4, label:'D'});
CREATE (:Node {id:5, label:'E'});
MATCH (a:Node {id:1}), (b:Node {id:2}) CREATE (a)-[:EDGE {weight:1}]->(b);
MATCH (a:Node {id:2}), (b:Node {id:3}) CREATE (a)-[:EDGE {weight:2}]->(b);
MATCH (a:Node {id:3}), (b:Node {id:4}) CREATE (a)-[:EDGE {weight:3}]->(b);
MATCH (a:Node {id:4}), (b:Node {id:5}) CREATE (a)-[:EDGE {weight:4}]->(b);
MATCH (a:Node {id:1}), (b:Node {id:3}) CREATE (a)-[:EDGE {weight:5}]->(b);

-- QUERY: one_hop
MATCH (a:Node)-[:EDGE*1..1]->(b:Node) WHERE a.id = 1 RETURN b.id ORDER BY b.id;

-- QUERY: two_hop
MATCH (a:Node)-[:EDGE*1..2]->(b:Node) WHERE a.id = 1 RETURN DISTINCT b.id ORDER BY b.id;

-- QUERY: three_hop
MATCH (a:Node)-[:EDGE*1..3]->(b:Node) WHERE a.id = 1 RETURN DISTINCT b.id ORDER BY b.id;

-- QUERY: exact_two_hop
MATCH (a:Node)-[:EDGE*2..2]->(b:Node) WHERE a.id = 1 RETURN DISTINCT b.id ORDER BY b.id;

-- QUERY: reachable_from_2
MATCH (a:Node)-[:EDGE*1..4]->(b:Node) WHERE a.id = 2 RETURN DISTINCT b.id ORDER BY b.id;

-- QUERY: count_reachable_from_1
MATCH (a:Node)-[:EDGE*1..4]->(b:Node) WHERE a.id = 1 RETURN COUNT(DISTINCT b.id) AS cnt;
