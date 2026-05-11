-- corpus-transactional-graph.cypher
-- Transactional graph snapshots over multi-table node/rel traversal and rel mutation.

-- SCHEMA
NODE Person   id:INT64  name:STRING
NODE Company  id:INT64  name:STRING
REL  WORKS_AT       FROM:Person   TO:Company  since:INT64
REL  PARTNERS_WITH  FROM:Company  TO:Company  strength:INT64

-- SETUP
CREATE (:Person {id:1, name:'Alice'});
CREATE (:Person {id:2, name:'Bob'});
CREATE (:Company {id:10, name:'Acme'});
CREATE (:Company {id:20, name:'Beta'});
MATCH (p:Person {id:1}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2020}]->(c);
MATCH (a:Company {id:10}), (b:Company {id:20}) CREATE (a)-[:PARTNERS_WITH {strength:9}]->(b);

-- QUERY: begin_graph_insert_rollback
BEGIN TRANSACTION;

-- QUERY: create_bob_worksat_in_tx
MATCH (p:Person {id:2}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2024}]->(c);

-- QUERY: visible_chain_inside_insert_tx
MATCH (p:Person)-[:WORKS_AT]->(:Company)-[:PARTNERS_WITH]->(c2:Company)
RETURN p.id, c2.id ORDER BY p.id, c2.id;

-- QUERY: rollback_graph_insert
ROLLBACK;

-- QUERY: hidden_chain_after_insert_rollback
MATCH (p:Person)-[:WORKS_AT]->(:Company)-[:PARTNERS_WITH]->(c2:Company)
RETURN p.id, c2.id ORDER BY p.id, c2.id;

-- QUERY: begin_graph_mutation_rollback
BEGIN TRANSACTION;

-- QUERY: update_worksat_in_tx
MATCH (p:Person {id:1})-[r:WORKS_AT]->(c:Company {id:10}) SET r.since = 2030 RETURN r.since;

-- QUERY: delete_partner_in_tx
MATCH (a:Company {id:10})-[r:PARTNERS_WITH]->(b:Company {id:20}) DELETE r;

-- QUERY: visible_updated_worksat_inside_tx
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) RETURN p.id, c.id, r.since ORDER BY p.id, c.id;

-- QUERY: hidden_partner_inside_delete_tx
MATCH (a:Company)-[r:PARTNERS_WITH]->(b:Company) RETURN a.id, b.id, r.strength ORDER BY a.id, b.id;

-- QUERY: rollback_graph_mutation
ROLLBACK;

-- QUERY: restored_graph_after_mutation_rollback
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) RETURN p.id, c.id, r.since ORDER BY p.id, c.id;

-- QUERY: restored_partner_after_mutation_rollback
MATCH (a:Company)-[r:PARTNERS_WITH]->(b:Company) RETURN a.id, b.id, r.strength ORDER BY a.id, b.id;

-- QUERY: begin_graph_insert_commit
BEGIN TRANSACTION;

-- QUERY: create_bob_worksat_commit_tx
MATCH (p:Person {id:2}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2024}]->(c);

-- QUERY: commit_graph_insert
COMMIT;

-- QUERY: visible_chain_after_commit
MATCH (p:Person)-[:WORKS_AT]->(:Company)-[:PARTNERS_WITH]->(c2:Company)
RETURN p.id, c2.id ORDER BY p.id, c2.id;
