-- corpus-paths.cypher
-- Path-value semantics over recursive relationship bindings.

-- SCHEMA
NODE Company        id:INT64  name:STRING  revenue:INT64
REL  PARTNERS_WITH  FROM:Company  TO:Company  score:INT64

-- SETUP
CREATE (:Company {id:10, name:'Acme', revenue:1000});
CREATE (:Company {id:20, name:'Beta', revenue:2000});
CREATE (:Company {id:30, name:'Core', revenue:1500});
CREATE (:Company {id:40, name:'Delta', revenue:2500});
MATCH (c1:Company {id:10}), (c2:Company {id:20}) CREATE (c1)-[:PARTNERS_WITH {score:7}]->(c2);
MATCH (c1:Company {id:20}), (c2:Company {id:30}) CREATE (c1)-[:PARTNERS_WITH {score:5}]->(c2);
MATCH (c1:Company {id:30}), (c2:Company {id:40}) CREATE (c1)-[:PARTNERS_WITH {score:9}]->(c2);
MATCH (c1:Company {id:10}), (c2:Company {id:30}) CREATE (c1)-[:PARTNERS_WITH {score:4}]->(c2);

-- QUERY: path_length_two_hop
MATCH (c1:Company)-[p:PARTNERS_WITH*2..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 40
RETURN length(p) AS len;

-- QUERY: path_nodes_count_two_hop
MATCH (c1:Company)-[p:PARTNERS_WITH*2..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 40
RETURN array_length(nodes(p)) AS node_count;

-- QUERY: path_rels_count_two_hop
MATCH (c1:Company)-[p:PARTNERS_WITH*2..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 40
RETURN array_length(rels(p)) AS rel_count;

-- QUERY: path_first_last_nodes
MATCH (c1:Company)-[p:PARTNERS_WITH*2..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 40
RETURN list_element(nodes(p), 1) AS first_node, list_element(nodes(p), 3) AS last_node;

-- QUERY: path_is_acyclic
MATCH (c1:Company)-[p:PARTNERS_WITH*1..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 30
RETURN is_acyclic(p) AS acyclic_flag ORDER BY acyclic_flag;

-- QUERY: path_is_trail
MATCH (c1:Company)-[p:PARTNERS_WITH*1..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 30
RETURN is_trail(p) AS trail_flag ORDER BY trail_flag;

-- QUERY: undirected_path_lengths_from_beta
MATCH (c1:Company)-[p:PARTNERS_WITH*1..2]-(c2:Company)
WHERE c1.id = 20
RETURN c2.id AS target_id, length(p) AS len ORDER BY target_id, len;

-- QUERY: path_nodes_are_company_ids
MATCH (c1:Company)-[p:PARTNERS_WITH*1..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 20
RETURN list_element(nodes(p), 1) AS start_id, list_element(nodes(p), 2) AS end_id;

-- QUERY: path_assignment_alias_length
MATCH p = (c1:Company)-[:PARTNERS_WITH*2..2]->(c2:Company)
WHERE c1.id = 10 AND c2.id = 40
RETURN length(p) AS len;
