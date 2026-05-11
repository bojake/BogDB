-- corpus-gds-shortest-path.cypher
-- G-GOLD-015: GDS kernel coverage — the highest-value missing corpus.
-- C# surface confirmed in GdsAlgorithms.cs, GdsFunctions.cs, GdsRegistry.cs:
--   CALL sssp()                 → node, distance (BFS unweighted OR Dijkstra weighted)
--   CALL k_hop()                → node, source, hops
--   CALL variable_length_path() → source, target, length, path
--   Scalar functions: gds_version(), graph_density(), node_degree(),
--                     has_path(), sssp_distance(), k_hop_count()
-- Aliases: shortest_path / all_shortest_paths → sssp
--          k_hop_neighbors → k_hop,  var_length_path → variable_length_path

-- SCHEMA
NODE City   id:INT64  name:STRING
REL  ROUTE  FROM:City  TO:City  dist:INT64

-- SETUP
CREATE (:City {id:0, name:'A'});
CREATE (:City {id:1, name:'B'});
CREATE (:City {id:2, name:'C'});
CREATE (:City {id:3, name:'D'});
CREATE (:City {id:4, name:'E'});
MATCH (a:City {id:0}),(b:City {id:1}) CREATE (a)-[:ROUTE {dist:1}]->(b);
MATCH (a:City {id:1}),(b:City {id:2}) CREATE (a)-[:ROUTE {dist:2}]->(b);
MATCH (a:City {id:0}),(b:City {id:2}) CREATE (a)-[:ROUTE {dist:4}]->(b);
MATCH (a:City {id:2}),(b:City {id:3}) CREATE (a)-[:ROUTE {dist:1}]->(b);
MATCH (a:City {id:3}),(b:City {id:4}) CREATE (a)-[:ROUTE {dist:3}]->(b);
MATCH (a:City {id:1}),(b:City {id:4}) CREATE (a)-[:ROUTE {dist:10}]->(b);

-- ── GDS utility ───────────────────────────────────────────────────────────────

-- QUERY: gds_version
RETURN gds_version() IS NOT NULL AS has_version;

-- QUERY: graph_density
RETURN graph_density() >= 0.0 AS non_negative;

-- ── Node degree ───────────────────────────────────────────────────────────────

-- QUERY: out_degree_a
MATCH (c:City {id:0}) RETURN node_degree(id(c), 'out') AS deg;

-- QUERY: in_degree_b
MATCH (c:City {id:1}) RETURN node_degree(id(c), 'in') AS deg;

-- QUERY: both_degree_b
MATCH (c:City {id:1}) RETURN node_degree(id(c), 'both') AS deg;

-- ── SSSP (unweighted BFS from node 0) ────────────────────────────────────────

-- QUERY: sssp_call_returns_rows
CALL sssp() YIELD node, distance RETURN COUNT(*) AS row_count;

-- QUERY: sssp_source_distance_zero
CALL sssp() YIELD node, distance WHERE distance = 0.0 RETURN COUNT(*) AS cnt;

-- QUERY: sssp_all_non_negative
CALL sssp() YIELD node, distance WHERE distance IS NOT NULL RETURN MIN(distance) >= 0 AS ok;

-- QUERY: sssp_alias_shortest_path
CALL shortest_path() YIELD node, distance RETURN COUNT(*) AS row_count;

-- ── After SSSP: scalar accessor functions ────────────────────────────────────

-- QUERY: sssp_distance_scalar
CALL sssp() YIELD * RETURN COUNT(*) AS primed;

-- QUERY: has_path_a_to_b
MATCH (a:City {id:0}), (b:City {id:1}) RETURN has_path(id(a), id(b), 3) AS reachable;

-- QUERY: has_path_a_to_e
MATCH (a:City {id:0}), (e:City {id:4}) RETURN has_path(id(a), id(e), 5) AS reachable;

-- ── K-Hop ─────────────────────────────────────────────────────────────────────

-- QUERY: k_hop_call_yields_rows
CALL k_hop(maxHops := 2) YIELD node, source, hops RETURN COUNT(*) > 0 AS has_rows;

-- QUERY: k_hop_max_hops_bound
CALL k_hop(maxHops := 1) YIELD node, source, hops RETURN MAX(hops) <= 1 AS bounded;

-- ── Variable-length path ──────────────────────────────────────────────────────

-- QUERY: var_length_path_call
CALL variable_length_path(maxHops := 2) YIELD source, target, length, path
RETURN COUNT(*) > 0 AS has_rows;

-- QUERY: var_length_path_lengths_bounded
CALL variable_length_path(maxHops := 2) YIELD source, target, length, path
RETURN MAX(length) <= 2 AS bounded;

-- QUERY: var_len_alias
CALL var_length_path(maxHops := 1) YIELD source, target, length
RETURN MIN(length) >= 1 AS ok;
