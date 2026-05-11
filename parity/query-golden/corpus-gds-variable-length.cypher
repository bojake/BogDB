-- corpus-gds-variable-length.cypher
-- G-GOLD-017: GDS variable-length path algorithm coverage (deeper than G-GOLD-015).
-- C# surface confirmed in GdsAlgorithms.cs:
--   CALL variable_length_path(maxHops := N)
--     → source, target, length, path (DFS simple paths, minLen=1)
--   CALL k_hop(maxHops := N)
--     → node, source, hops (BFS reachability per-source)
--   CALL sssp_delta()  / CALL delta_stepping()
--     → node, distance (parallel Delta-Stepping SSSP)
-- Options available via GdsCallOptions:
--   maxHops, sourceNode (NodeId), direction ("OUT"|"IN"|"BOTH")
--
-- Graph: linear chain A→B→C→D→E plus shortcut A→C for interesting path counts.

-- SCHEMA
NODE Stop   id:INT64  name:STRING
REL  LINK   FROM:Stop  TO:Stop  cost:INT64

-- SETUP
CREATE (:Stop {id:0, name:'A'});
CREATE (:Stop {id:1, name:'B'});
CREATE (:Stop {id:2, name:'C'});
CREATE (:Stop {id:3, name:'D'});
CREATE (:Stop {id:4, name:'E'});
MATCH (a:Stop {id:0}),(b:Stop {id:1}) CREATE (a)-[:LINK {cost:1}]->(b);
MATCH (a:Stop {id:1}),(b:Stop {id:2}) CREATE (a)-[:LINK {cost:1}]->(b);
MATCH (a:Stop {id:2}),(b:Stop {id:3}) CREATE (a)-[:LINK {cost:1}]->(b);
MATCH (a:Stop {id:3}),(b:Stop {id:4}) CREATE (a)-[:LINK {cost:1}]->(b);
MATCH (a:Stop {id:0}),(b:Stop {id:2}) CREATE (a)-[:LINK {cost:2}]->(b);

-- ── Variable-length path ──────────────────────────────────────────────────────

-- QUERY: vlp_hop1_has_rows
CALL variable_length_path(maxHops := 1) YIELD source, target, length, path
RETURN COUNT(*) > 0 AS has_rows;

-- QUERY: vlp_hop1_all_length_one
CALL variable_length_path(maxHops := 1) YIELD source, target, length, path
RETURN MAX(length) = 1 AS exact;

-- QUERY: vlp_hop2_max_bound
CALL variable_length_path(maxHops := 2) YIELD source, target, length, path
RETURN MAX(length) <= 2 AS bounded;

-- QUERY: vlp_hop2_more_paths_than_hop1
-- maxHops=2 should yield strictly more rows than maxHops=1 (shortcut A→C adds paths)
CALL variable_length_path(maxHops := 2) YIELD source, target, length
RETURN COUNT(*) AS cnt_2;

-- QUERY: vlp_hop1_count_for_reference
CALL variable_length_path(maxHops := 1) YIELD source, target, length
RETURN COUNT(*) AS cnt_1;

-- QUERY: vlp_path_column_populated
CALL variable_length_path(maxHops := 1) YIELD source, target, length, path
RETURN MIN(LENGTH(path)) > 0 AS path_non_empty;

-- QUERY: vlp_source_target_non_equal
-- Simple path semantics: source ≠ target for all rows
CALL variable_length_path(maxHops := 2) YIELD source, target, length, path
RETURN COUNT(*) = SUM(CASE WHEN source <> target THEN 1 ELSE 0 END) AS no_self_paths;

-- ── K-hop (extended from G-GOLD-015) ─────────────────────────────────────────

-- QUERY: k_hop_2_more_than_1
-- k_hop with maxHops=2 reaches more nodes than maxHops=1
CALL k_hop(maxHops := 2) YIELD node, source, hops
RETURN COUNT(*) AS cnt_2;

-- QUERY: k_hop_1_for_reference
CALL k_hop(maxHops := 1) YIELD node, source, hops
RETURN COUNT(*) AS cnt_1;

-- QUERY: k_hop_hops_always_positive
CALL k_hop(maxHops := 3) YIELD node, source, hops
RETURN MIN(hops) >= 1 AS positive;

-- QUERY: k_hop_max_hops_3_bound
CALL k_hop(maxHops := 3) YIELD node, source, hops
RETURN MAX(hops) <= 3 AS bounded;

-- ── Delta-stepping SSSP ───────────────────────────────────────────────────────

-- QUERY: delta_stepping_yields_rows
CALL sssp_delta() YIELD node, distance
RETURN COUNT(*) AS row_count;

-- QUERY: delta_stepping_alias
CALL delta_stepping() YIELD node, distance
RETURN COUNT(*) AS row_count;

-- QUERY: delta_stepping_non_negative_distances
CALL sssp_delta() YIELD node, distance
WHERE distance IS NOT NULL
RETURN MIN(distance) >= 0 AS ok;

-- QUERY: delta_stepping_same_count_as_sssp
-- Delta-stepping and standard SSSP should reach the same number of reachable nodes
CALL sssp_delta() YIELD node, distance WHERE distance IS NOT NULL
RETURN COUNT(*) AS delta_reachable;
