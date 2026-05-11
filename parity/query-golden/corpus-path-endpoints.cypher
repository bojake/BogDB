-- corpus-path-endpoints.cypher
-- Path endpoint extraction: start/end node IDs, per-hop property access, endpoint-filter chains.

-- SCHEMA
NODE City      id:INT64  name:STRING  pop:INT64
REL  ROUTE     FROM:City  TO:City     dist:INT64  mode:STRING

-- SETUP
CREATE (:City {id:1, name:'Alpha', pop:100});
CREATE (:City {id:2, name:'Beta',  pop:200});
CREATE (:City {id:3, name:'Gamma', pop:150});
CREATE (:City {id:4, name:'Delta', pop:300});
CREATE (:City {id:5, name:'Eta',   pop:250});
MATCH (a:City {id:1}), (b:City {id:2}) CREATE (a)-[:ROUTE {dist:10, mode:'rail'}]->(b);
MATCH (a:City {id:2}), (b:City {id:3}) CREATE (a)-[:ROUTE {dist:20, mode:'road'}]->(b);
MATCH (a:City {id:3}), (b:City {id:4}) CREATE (a)-[:ROUTE {dist:15, mode:'rail'}]->(b);
MATCH (a:City {id:1}), (b:City {id:3}) CREATE (a)-[:ROUTE {dist:25, mode:'road'}]->(b);
MATCH (a:City {id:4}), (b:City {id:5}) CREATE (a)-[:ROUTE {dist:5,  mode:'air'}]->(b);

-- QUERY: single_hop_start_end_ids
MATCH (a:City)-[r:ROUTE]->(b:City)
RETURN a.id AS from_id, b.id AS to_id, r.dist ORDER BY a.id, b.id;

-- QUERY: two_hop_endpoint_ids
MATCH (a:City)-[:ROUTE*2..2]->(b:City)
WHERE a.id = 1
RETURN a.id AS start_id, b.id AS end_id ORDER BY end_id;

-- QUERY: path_depth_range
MATCH (a:City {id:1})-[p:ROUTE*1..2]->(b:City)
RETURN b.id AS dest, length(p) AS hops ORDER BY b.id, hops;

-- QUERY: path_first_rel
MATCH (a:City {id:1})-[p:ROUTE*2..2]->(b:City {id:3})
RETURN list_element(rels(p), 1) AS first_rel;

-- QUERY: endpoint_filter_high_pop
MATCH (a:City)-[r:ROUTE]->(b:City)
WHERE b.pop >= 200
RETURN a.id, b.id, b.pop ORDER BY a.id, b.id;

-- QUERY: two_hop_end_pop_filter
MATCH (a:City)-[:ROUTE*2..2]->(b:City)
WHERE b.pop > 200
RETURN a.id, b.id, b.pop ORDER BY a.id, b.id;

-- QUERY: chained_mode_filter
MATCH (a:City)-[r:ROUTE]->(b:City)
WHERE r.mode = 'rail'
RETURN a.id, b.id, r.dist ORDER BY a.id, b.id;

-- QUERY: path_node_count_range
MATCH (a:City {id:1})-[p:ROUTE*1..3]->(b:City)
RETURN b.id AS dest, array_length(nodes(p)) AS nc ORDER BY dest, nc;
