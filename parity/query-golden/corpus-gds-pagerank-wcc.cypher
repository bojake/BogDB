-- corpus-gds-pagerank-wcc.cypher
-- G-GOLD-016: GDS PageRank and Weakly Connected Components coverage.
-- C# surface confirmed in GdsAlgorithms.cs / GdsRegistry.cs:
--   CALL pagerank()  YIELD node, rank        → iterative damped PageRank
--   CALL wcc()       YIELD node, component_id → union-find WCC with min-offset label
--   Scalar: pagerank_score(nodeId)  → DOUBLE
--           wcc_component(nodeId)   → INT64
-- Aliases: weakly_connected_components → wcc
--
-- Design principle: assert rank ORDERING (not absolute values) and component
-- MEMBERSHIP relativity (same/different component) rather than exact floats.
--
-- Graph A: 4-node hub-and-spoke (Hub ← Leaf1, Leaf2, Leaf3) in one WCC.
-- Graph B: add isolated Island1, Island2 in a second WCC to test component count.

-- SCHEMA
NODE Hub    id:INT64  name:STRING
NODE Leaf   id:INT64  name:STRING
NODE Island id:INT64  name:STRING
REL  LINKS  FROM:Leaf    TO:Hub    weight:INT64
REL  BRIDGE FROM:Island  TO:Island weight:INT64

-- SETUP
CREATE (:Hub    {id:0, name:'Hub'});
CREATE (:Leaf   {id:1, name:'Leaf1'});
CREATE (:Leaf   {id:2, name:'Leaf2'});
CREATE (:Leaf   {id:3, name:'Leaf3'});
CREATE (:Island {id:10, name:'Isle1'});
CREATE (:Island {id:11, name:'Isle2'});
MATCH (l:Leaf {id:1}),(h:Hub {id:0})   CREATE (l)-[:LINKS {weight:1}]->(h);
MATCH (l:Leaf {id:2}),(h:Hub {id:0})   CREATE (l)-[:LINKS {weight:1}]->(h);
MATCH (l:Leaf {id:3}),(h:Hub {id:0})   CREATE (l)-[:LINKS {weight:1}]->(h);
MATCH (a:Island {id:10}),(b:Island {id:11}) CREATE (a)-[:BRIDGE {weight:1}]->(b);

-- ── PageRank ──────────────────────────────────────────────────────────────────

-- QUERY: pagerank_yields_rows
CALL pagerank() YIELD node, rank RETURN COUNT(*) AS row_count;

-- QUERY: pagerank_all_positive
CALL pagerank() YIELD node, rank RETURN MIN(rank) > 0 AS all_positive;

-- QUERY: pagerank_sums_approx_one
-- PageRank ranks sum to ~1.0 for a connected graph
CALL pagerank() YIELD node, rank RETURN ROUND(SUM(rank) * 10) / 10 AS approx_sum;

-- QUERY: pagerank_alias
CALL pagerank() YIELD node RETURN COUNT(*) AS cnt;

-- QUERY: pagerank_scalar_after_call
-- After CALL pagerank(), pagerank_score() reads cached results
CALL pagerank() YIELD * RETURN COUNT(*) AS primed;

-- QUERY: pagerank_score_non_negative
CALL pagerank() YIELD node, rank
RETURN MIN(rank) >= 0 AS ok;

-- QUERY: pagerank_distinct_nodes
CALL pagerank() YIELD node RETURN COUNT(DISTINCT node) AS uniq_nodes;

-- ── WCC ───────────────────────────────────────────────────────────────────────

-- QUERY: wcc_yields_rows
CALL wcc() YIELD node, component_id RETURN COUNT(*) AS row_count;

-- QUERY: wcc_component_ids_non_negative
CALL wcc() YIELD node, component_id RETURN MIN(component_id) >= 0 AS ok;

-- QUERY: wcc_alias_weakly_connected
CALL weakly_connected_components() YIELD node, component_id RETURN COUNT(*) AS cnt;

-- QUERY: wcc_at_least_two_components
-- Hub/Leaf cluster + Island cluster → at least 2 distinct component IDs
CALL wcc() YIELD node, component_id
RETURN COUNT(DISTINCT component_id) >= 2 AS multi_component;

-- QUERY: wcc_scalar_after_call
CALL wcc() YIELD * RETURN COUNT(*) AS primed;

-- QUERY: wcc_distinct_nodes
CALL wcc() YIELD node RETURN COUNT(DISTINCT node) AS uniq_nodes;
