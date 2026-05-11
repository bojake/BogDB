-- corpus-merge.cypher
-- Supported MERGE shapes: single-node, single-relationship, connected graph, disconnected graph, typed NULL properties, duplicate-PK failure snapshots, storage-backed same-endpoint parallel edges, and post-MATCH relationship SET/DELETE targeting via row refs.

-- SCHEMA
NODE Person   id:INT64  name:STRING  visits:INT64  nickname:STRING  age:INT64  active:BOOL
NODE Company  id:INT64  name:STRING
NODE City     id:INT64  name:STRING
REL  WORKS_AT FROM:Person TO:Company score:INT64 active:BOOL note:STRING
REL  LOCATED_IN FROM:Company TO:City hq:BOOL note:STRING

-- SETUP
CREATE (:Company {id:10, name:'Acme'});

-- QUERY: merge_node_create
MERGE (p:Person {id:1}) ON CREATE SET p.name = 'Alice', p.visits = 1 ON MATCH SET p.visits = 99 RETURN p.name, p.visits;

-- QUERY: merge_node_match
MERGE (p:Person {id:1}) ON CREATE SET p.name = 'Other', p.visits = 2 ON MATCH SET p.visits = 3 RETURN p.name, p.visits;

-- QUERY: merge_node_duplicate_pk_error
MERGE (:Person {id:1, name:'Bob'}) RETURN 1;

-- QUERY: merge_node_null_create
MERGE (:Person {id:2, nickname:NULL, age:NULL, active:false}) RETURN 1;

-- QUERY: merge_node_null_verify
MATCH (p:Person {id:2}) RETURN p.nickname IS NULL, p.age IS NULL, p.active;

-- QUERY: merge_rel_create
MATCH (p:Person {id:1}), (c:Company {id:10}) MERGE (p)-[r:WORKS_AT {score:1, active:true}]->(c) ON CREATE SET r.note = 'created' ON MATCH SET r.note = 'matched' RETURN r.score, r.active, r.note;

-- QUERY: merge_rel_match
MATCH (p:Person {id:1}), (c:Company {id:10}) MERGE (p)-[r:WORKS_AT {score:1, active:true}]->(c) ON CREATE SET r.note = 'created' ON MATCH SET r.note = 'matched' RETURN r.score, r.active, r.note;

-- QUERY: merge_rel_parallel_edge_create
MATCH (p:Person {id:1}), (c:Company {id:10}) MERGE (p)-[r:WORKS_AT {score:2, active:true}]->(c) RETURN r.score;

-- QUERY: merge_rel_parallel_edge_verify
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) WHERE p.id = 1 AND c.id = 10 RETURN r.score ORDER BY r.score;

-- QUERY: merge_rel_parallel_edge_set_after_match
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) WHERE p.id = 1 AND c.id = 10 AND r.score = 2 SET r.note = 'parallel' RETURN r.score, r.note;

-- QUERY: merge_rel_parallel_edge_set_verify
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) WHERE p.id = 1 AND c.id = 10 RETURN r.score, coalesce(r.note, '<null>') ORDER BY r.score;

-- QUERY: merge_rel_parallel_edge_delete_after_match
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) WHERE p.id = 1 AND c.id = 10 AND r.score = 2 DELETE r;

-- QUERY: merge_rel_parallel_edge_delete_verify
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) WHERE p.id = 1 AND c.id = 10 RETURN r.score, coalesce(r.note, '<null>') ORDER BY r.score;

-- QUERY: merge_rel_null_create
MATCH (p:Person {id:2}), (c:Company {id:10}) MERGE (p)-[r:WORKS_AT {score:NULL, active:false}]->(c) RETURN r.score IS NULL, r.active;

-- QUERY: merge_rel_null_verify
MATCH (:Person {id:2})-[r:WORKS_AT]->(:Company {id:10}) RETURN r.score IS NULL, r.active;

-- QUERY: merge_graph_create
MERGE (p:Person {id:3})-[w:WORKS_AT {score:7, active:true}]->(c:Company {id:11})-[l:LOCATED_IN {hq:true}]->(city:City {id:100}) ON CREATE SET p.name = 'Cara', c.name = 'Beta', city.name = 'Seattle', w.note = 'created-work', l.note = 'created-city' ON MATCH SET w.note = 'matched-work', l.note = 'matched-city' RETURN p.name, c.name, city.name, w.note, l.note;

-- QUERY: merge_graph_match
MERGE (p:Person {id:3})-[w:WORKS_AT {score:7, active:true}]->(c:Company {id:11})-[l:LOCATED_IN {hq:true}]->(city:City {id:100}) ON CREATE SET p.name = 'Other', c.name = 'OtherCo', city.name = 'Elsewhere', w.note = 'created-work', l.note = 'created-city' ON MATCH SET w.note = 'matched-work', l.note = 'matched-city' RETURN p.name, c.name, city.name, w.note, l.note;

-- QUERY: merge_disconnected_create
MERGE (p:Person {id:4})-[w:WORKS_AT {score:8, active:true}]->(c:Company {id:12}), (city:City {id:101}) ON CREATE SET p.name = 'Dana', c.name = 'Delta', city.name = 'Portland', city.tag = 'created', w.note = 'created-work' ON MATCH SET city.tag = 'matched', w.note = 'matched-work' RETURN p.name, c.name, city.name, city.tag, w.note;

-- QUERY: merge_disconnected_match
MERGE (p:Person {id:4})-[w:WORKS_AT {score:8, active:true}]->(c:Company {id:12}), (city:City {id:101}) ON CREATE SET p.name = 'Other', c.name = 'OtherCo', city.name = 'Elsewhere', city.tag = 'created', w.note = 'created-work' ON MATCH SET city.tag = 'matched', w.note = 'matched-work' RETURN p.name, c.name, city.name, city.tag, w.note;
