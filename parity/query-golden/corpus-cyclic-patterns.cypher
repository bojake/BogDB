-- corpus-cyclic-patterns.cypher
-- G-GOLD-011: Cyclic query pattern coverage.
-- Models two-node cycles, triangles, squares, and self-referential patterns.
-- C++ parity: test/test_files/cyclic/single_label.test, multi_label.test
--
-- Graph: 5 persons (A-E) with directed KNOWS edges forming a dense
-- mutual-knows subgraph among A,B,C,D (all know each other bidirectionally),
-- plus person E knows A only. Orgs attached for cross-label triangle tests.

-- SCHEMA
NODE Person  id:INT64  name:STRING
NODE Org     id:INT64  name:STRING
REL  KNOWS   FROM:Person  TO:Person   weight:INT64
REL  MEMBER  FROM:Person  TO:Org      since:INT64

-- SETUP
CREATE (:Person {id:1, name:'Alice'});
CREATE (:Person {id:2, name:'Bob'});
CREATE (:Person {id:3, name:'Carol'});
CREATE (:Person {id:4, name:'Dan'});
CREATE (:Person {id:5, name:'Eve'});
CREATE (:Org {id:10, name:'AlphaClub'});
CREATE (:Org {id:20, name:'BetaClub'});
MATCH (a:Person {id:1}),(b:Person {id:2}) CREATE (a)-[:KNOWS {weight:1}]->(b);
MATCH (a:Person {id:2}),(b:Person {id:1}) CREATE (a)-[:KNOWS {weight:1}]->(b);
MATCH (a:Person {id:1}),(b:Person {id:3}) CREATE (a)-[:KNOWS {weight:2}]->(b);
MATCH (a:Person {id:3}),(b:Person {id:1}) CREATE (a)-[:KNOWS {weight:2}]->(b);
MATCH (a:Person {id:1}),(b:Person {id:4}) CREATE (a)-[:KNOWS {weight:3}]->(b);
MATCH (a:Person {id:4}),(b:Person {id:1}) CREATE (a)-[:KNOWS {weight:3}]->(b);
MATCH (a:Person {id:2}),(b:Person {id:3}) CREATE (a)-[:KNOWS {weight:4}]->(b);
MATCH (a:Person {id:3}),(b:Person {id:2}) CREATE (a)-[:KNOWS {weight:4}]->(b);
MATCH (a:Person {id:2}),(b:Person {id:4}) CREATE (a)-[:KNOWS {weight:5}]->(b);
MATCH (a:Person {id:4}),(b:Person {id:2}) CREATE (a)-[:KNOWS {weight:5}]->(b);
MATCH (a:Person {id:3}),(b:Person {id:4}) CREATE (a)-[:KNOWS {weight:6}]->(b);
MATCH (a:Person {id:4}),(b:Person {id:3}) CREATE (a)-[:KNOWS {weight:6}]->(b);
MATCH (a:Person {id:5}),(b:Person {id:1}) CREATE (a)-[:KNOWS {weight:7}]->(b);
MATCH (p:Person {id:1}),(o:Org {id:10}) CREATE (p)-[:MEMBER {since:2020}]->(o);
MATCH (p:Person {id:2}),(o:Org {id:10}) CREATE (p)-[:MEMBER {since:2021}]->(o);
MATCH (p:Person {id:3}),(o:Org {id:20}) CREATE (p)-[:MEMBER {since:2022}]->(o);

-- ── Two-node cycle ────────────────────────────────────────────────────────────

-- QUERY: two_node_cycle_count
MATCH (a:Person)-[:KNOWS]->(b:Person), (b)-[:KNOWS]->(a)
RETURN COUNT(*) AS cnt;

-- QUERY: two_node_cycle_pairs
MATCH (a:Person)-[:KNOWS]->(b:Person), (b)-[:KNOWS]->(a)
RETURN a.name, b.name ORDER BY a.name, b.name;

-- QUERY: two_node_cycle_no_eve
MATCH (a:Person)-[:KNOWS]->(b:Person), (b)-[:KNOWS]->(a)
RETURN a.name, b.name ORDER BY a.name, b.name
SKIP 0 LIMIT 100;

-- ── Triangle ─────────────────────────────────────────────────────────────────

-- QUERY: triangle_count
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:KNOWS]->(c:Person), (a)-[:KNOWS]->(c)
RETURN COUNT(*) AS cnt;

-- QUERY: triangle_distinct_ids
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:KNOWS]->(c:Person), (a)-[:KNOWS]->(c)
WHERE a.id < b.id AND b.id < c.id
RETURN a.name, b.name, c.name ORDER BY a.name, b.name, c.name;

-- QUERY: triangle_with_edge_weight_filter
MATCH (a:Person)-[e1:KNOWS]->(b:Person)-[e2:KNOWS]->(c:Person), (a)-[e3:KNOWS]->(c)
WHERE e1.weight = 1
RETURN a.name, b.name, c.name ORDER BY a.name, b.name, c.name;

-- QUERY: triangle_with_node_filter
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:KNOWS]->(c:Person), (a)-[:KNOWS]->(c)
WHERE a.name = 'Alice'
RETURN b.name, c.name ORDER BY b.name, c.name;

-- ── Cross-label triangle ──────────────────────────────────────────────────────

-- QUERY: cross_label_triangle_count
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:MEMBER]->(o:Org), (a)-[:MEMBER]->(o)
RETURN COUNT(*) AS cnt;

-- QUERY: cross_label_triangle_projection
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:MEMBER]->(o:Org), (a)-[:MEMBER]->(o)
RETURN a.name, b.name, o.name ORDER BY a.name, b.name, o.name;

-- ── Square (4-cycle) ──────────────────────────────────────────────────────────

-- QUERY: square_count
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:KNOWS]->(c:Person)-[:KNOWS]->(d:Person), (a)-[:KNOWS]->(d)
RETURN COUNT(*) AS cnt;

-- ── No cycle (Eve only knows Alice, not mutual) ───────────────────────────────

-- QUERY: eve_no_return_edge
MATCH (a:Person)-[:KNOWS]->(b:Person), (b)-[:KNOWS]->(a)
WHERE a.name = 'Eve'
RETURN COUNT(*) AS cnt;

-- QUERY: one_directional_only
MATCH (a:Person {name:'Eve'})-[:KNOWS]->(b:Person)
RETURN b.name;
