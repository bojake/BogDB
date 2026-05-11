-- corpus-basic.cypher
-- Basic graph: Person nodes + KNOWS relationships.
-- Uses structured SCHEMA section for the runner to call EnsureNodeTable/EnsureRelTable.
-- DML runs through Query(). All QUERY statements include ORDER BY.

-- SCHEMA
NODE Person  id:INT64  age:INT64  name:STRING
REL  KNOWS   FROM:Person  TO:Person  since:INT64

-- SETUP
CREATE (:Person {id:1, age:25, name:'Alice'});
CREATE (:Person {id:2, age:40, name:'Bob'});
CREATE (:Person {id:3, age:55, name:'Carol'});
CREATE (:Person {id:4, age:30, name:'Dave'});
MATCH (a:Person {id:1}), (b:Person {id:2}) CREATE (a)-[:KNOWS {since:2020}]->(b);
MATCH (a:Person {id:2}), (b:Person {id:3}) CREATE (a)-[:KNOWS {since:2021}]->(b);
MATCH (a:Person {id:1}), (b:Person {id:4}) CREATE (a)-[:KNOWS {since:2022}]->(b);

-- QUERY: node_scan_all
MATCH (p:Person) RETURN p.id, p.age, p.name ORDER BY p.id;

-- QUERY: node_count
MATCH (p:Person) RETURN COUNT(*) AS cnt;

-- QUERY: one_hop_all_rels
MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since ORDER BY a.id, b.id;

-- QUERY: one_hop_property_filter
MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE r.since > 2020 RETURN a.name, b.name, r.since ORDER BY r.since, a.name;

-- QUERY: filter_age_gt_30
MATCH (p:Person) WHERE p.age > 30 RETURN p.name ORDER BY p.name;

-- QUERY: filter_name_starts_with_a
MATCH (p:Person) WHERE p.name STARTS WITH 'A' RETURN p.name ORDER BY p.name;

-- QUERY: rel_count
MATCH ()-[:KNOWS]->() RETURN COUNT(*) AS cnt;

-- QUERY: two_hop
MATCH (a:Person)-[:KNOWS]->(b:Person)-[:KNOWS]->(c:Person) RETURN a.id, b.id, c.id ORDER BY a.id;
