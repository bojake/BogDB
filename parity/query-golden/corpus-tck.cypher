-- SCHEMA
NODE Node n:INT64 name:STRING
NODE A n:INT64 name:STRING
NODE B n:INT64 name:STRING
NODE C n:INT64 name:STRING
REL REL FROM:Node TO:Node
REL T FROM:Node TO:Node
REL R_A_B FROM:A TO:B
REL R_B_C FROM:B TO:C

-- SETUP
CREATE (:Node {n: 1, name: 'a'});
CREATE (:Node {n: 2, name: 'b'});
CREATE (:Node {n: 3, name: 'c'});
CREATE (:A {n: 10, name: 'A1'});
CREATE (:B {n: 20, name: 'B1'});
CREATE (:C {n: 30, name: 'C1'});

MATCH (n1:Node {n: 1}), (n2:Node {n: 2}) CREATE (n1)-[:REL]->(n2);
MATCH (n2:Node {n: 2}), (n3:Node {n: 3}) CREATE (n2)-[:REL]->(n3);
MATCH (n1:Node {n: 1}), (n3:Node {n: 3}) CREATE (n1)-[:T]->(n3);

MATCH (a:A {n: 10}), (b:B {n: 20}) CREATE (a)-[:R_A_B]->(b);
MATCH (b:B {n: 20}), (c:C {n: 30}) CREATE (b)-[:R_B_C]->(c);

-- QUERY: tck_match_all_nodes
MATCH (n:Node) RETURN n.n ORDER BY n.n;

-- QUERY: tck_path_bindings
MATCH p=(n1:Node)-[r:REL*1..2]->(n2:Node)
RETURN length(p) AS len
ORDER BY len, n1.n, n2.n;

-- QUERY: tck_optional_match_fallback
MATCH (n1:Node {n: 1})
OPTIONAL MATCH (n1)-[:REL]->(n2:Node {n: 999})
RETURN n1.n, n2.n;

-- QUERY: tck_multiple_with
MATCH (n:Node)
WITH n.n AS num, n.name AS str
WHERE num > 1
WITH num * 2 AS doubleNum, str
RETURN doubleNum, str
ORDER BY doubleNum;

-- QUERY: tck_unwind_chain
UNWIND [1, 2, 3] AS i
UNWIND [i, i+1] AS j
RETURN i, j
ORDER BY i, j;

-- QUERY: tck_boolean_bridge
MATCH (n:Node)
WHERE (n.n > 1 AND n.name = 'b') OR n.name = 'c'
RETURN n.n ORDER BY n.n;
