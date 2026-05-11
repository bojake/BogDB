-- corpus-transactional.cypher
-- Transaction lifecycle snapshots over a shared in-memory connection.

-- SCHEMA
NODE Person  id:INT64  name:STRING
REL  KNOWS   FROM:Person  TO:Person  since:INT64

-- SETUP
CREATE (:Person {id:1, name:'Alice'});

-- QUERY: begin_insert_rollback
BEGIN TRANSACTION;

-- QUERY: insert_bob_in_tx
CREATE (:Person {id:2, name:'Bob'});

-- QUERY: visible_inside_insert_tx
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: rollback_insert
ROLLBACK;

-- QUERY: hidden_after_insert_rollback
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: begin_insert_commit
BEGIN TRANSACTION;

-- QUERY: insert_bob_commit_tx
CREATE (:Person {id:2, name:'Bob'});

-- QUERY: commit_insert
COMMIT;

-- QUERY: visible_after_commit
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: begin_delete_rollback
BEGIN TRANSACTION;

-- QUERY: delete_bob_in_tx
MATCH (p:Person) WHERE p.id = 2 DELETE p;

-- QUERY: hidden_inside_delete_tx
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: rollback_delete
ROLLBACK;

-- QUERY: restored_after_delete_rollback
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: begin_update_rollback
BEGIN TRANSACTION;

-- QUERY: update_bob_name_in_tx
MATCH (p:Person) WHERE p.id = 2 SET p.name = 'Bobby' RETURN p.name;

-- QUERY: visible_updated_name_inside_tx
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: rollback_update
ROLLBACK;

-- QUERY: restored_name_after_update_rollback
MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id;

-- QUERY: begin_rel_insert_rollback
BEGIN TRANSACTION;

-- QUERY: create_knows_in_tx
MATCH (a:Person {id:1}), (b:Person {id:2}) CREATE (a)-[:KNOWS {since:2026}]->(b);

-- QUERY: visible_rel_inside_tx
MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since ORDER BY a.id, b.id;

-- QUERY: rollback_rel_insert
ROLLBACK;

-- QUERY: hidden_rel_after_rollback
MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since ORDER BY a.id, b.id;

-- QUERY: begin_rel_insert_commit
BEGIN TRANSACTION;

-- QUERY: create_knows_commit_tx
MATCH (a:Person {id:1}), (b:Person {id:2}) CREATE (a)-[:KNOWS {since:2026}]->(b);

-- QUERY: commit_rel_insert
COMMIT;

-- QUERY: visible_rel_after_commit
MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since ORDER BY a.id, b.id;
