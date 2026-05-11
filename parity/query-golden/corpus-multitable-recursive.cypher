-- corpus-multitable-recursive.cypher
-- Variable-length partner-chain traversal inside a multi-table graph.

-- SCHEMA
NODE Person         id:INT64  name:STRING  level:INT64
NODE Company        id:INT64  name:STRING  revenue:INT64
NODE City           id:INT64  name:STRING  region:STRING
REL  WORKS_AT       FROM:Person   TO:Company  since:INT64
REL  ADVISES        FROM:Person   TO:Company  hours:INT64
REL  LOCATED_IN     FROM:Company  TO:City     opened:INT64
REL  PARTNERS_WITH  FROM:Company  TO:Company  score:INT64

-- SETUP
CREATE (:Person {id:1, name:'Alice', level:3});
CREATE (:Person {id:2, name:'Bob', level:2});
CREATE (:Person {id:3, name:'Carol', level:5});
CREATE (:Company {id:10, name:'Acme', revenue:1000});
CREATE (:Company {id:20, name:'Beta', revenue:2000});
CREATE (:Company {id:30, name:'Core', revenue:1500});
CREATE (:Company {id:40, name:'Delta', revenue:2500});
CREATE (:City {id:100, name:'Seattle', region:'west'});
CREATE (:City {id:200, name:'Austin', region:'south'});
CREATE (:City {id:300, name:'Denver', region:'mountain'});
MATCH (p:Person {id:1}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2020}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:20}) CREATE (p)-[:WORKS_AT {since:2021}]->(c);
MATCH (p:Person {id:3}), (c:Company {id:30}) CREATE (p)-[:WORKS_AT {since:2022}]->(c);
MATCH (p:Person {id:1}), (c:Company {id:20}) CREATE (p)-[:ADVISES {hours:10}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:30}) CREATE (p)-[:ADVISES {hours:7}]->(c);
MATCH (c:Company {id:10}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2010}]->(city);
MATCH (c:Company {id:20}), (city:City {id:200}) CREATE (c)-[:LOCATED_IN {opened:2015}]->(city);
MATCH (c:Company {id:30}), (city:City {id:300}) CREATE (c)-[:LOCATED_IN {opened:2018}]->(city);
MATCH (c:Company {id:40}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2020}]->(city);
MATCH (c1:Company {id:10}), (c2:Company {id:20}) CREATE (c1)-[:PARTNERS_WITH {score:7}]->(c2);
MATCH (c1:Company {id:20}), (c2:Company {id:30}) CREATE (c1)-[:PARTNERS_WITH {score:5}]->(c2);
MATCH (c1:Company {id:30}), (c2:Company {id:40}) CREATE (c1)-[:PARTNERS_WITH {score:9}]->(c2);
MATCH (c1:Company {id:10}), (c2:Company {id:30}) CREATE (c1)-[:PARTNERS_WITH {score:4}]->(c2);

-- QUERY: partner_one_hop_from_acme
MATCH (c1:Company)-[:PARTNERS_WITH*1..1]->(c2:Company)
WHERE c1.id = 10
RETURN DISTINCT c2.id ORDER BY c2.id;

-- QUERY: partner_two_hop_from_acme
MATCH (c1:Company)-[:PARTNERS_WITH*1..2]->(c2:Company)
WHERE c1.id = 10
RETURN DISTINCT c2.id ORDER BY c2.id;

-- QUERY: incoming_partner_chain_to_delta
MATCH (c1:Company)<-[:PARTNERS_WITH*1..2]-(c2:Company)
WHERE c1.id = 40
RETURN DISTINCT c2.id ORDER BY c2.id;

-- QUERY: undirected_partner_chain_from_beta
MATCH (c1:Company)-[:PARTNERS_WITH*1..2]-(c2:Company)
WHERE c1.id = 20
RETURN DISTINCT c2.id ORDER BY c2.id;

-- QUERY: employee_to_city_via_partner_chain
MATCH (p:Person)-[:WORKS_AT]->(c1:Company)-[:PARTNERS_WITH*1..2]->(c2:Company)-[:LOCATED_IN]->(city:City)
WHERE p.id = 1
RETURN DISTINCT c2.id, city.id ORDER BY c2.id, city.id;

-- QUERY: advisor_to_city_via_partner_chain
MATCH (p:Person)-[:ADVISES]->(c1:Company)-[:PARTNERS_WITH*1..2]->(c2:Company)-[:LOCATED_IN]->(city:City)
WHERE p.id = 1
RETURN DISTINCT c2.id, city.name ORDER BY c2.id, city.name;

-- QUERY: reachable_city_count_from_alice
MATCH (p:Person)-[:WORKS_AT]->(c1:Company)-[:PARTNERS_WITH*1..3]->(c2:Company)-[:LOCATED_IN]->(city:City)
WHERE p.id = 1
RETURN COUNT(DISTINCT city.id) AS cnt;

-- QUERY: partner_chain_to_west_region
MATCH (p:Person)-[:WORKS_AT]->(c1:Company)-[:PARTNERS_WITH*1..3]->(c2:Company)-[:LOCATED_IN]->(city:City)
WHERE city.region = 'west'
RETURN DISTINCT p.name, c2.name, city.name ORDER BY p.name, c2.name, city.name;
