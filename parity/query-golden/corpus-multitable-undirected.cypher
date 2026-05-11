-- corpus-multitable-undirected.cypher
-- Undirected multi-table graph coverage: BOTH-direction expansion and denser mixed-shape chains.

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
CREATE (:Person {id:4, name:'Dave', level:4});
CREATE (:Company {id:10, name:'Acme', revenue:1000});
CREATE (:Company {id:20, name:'Beta', revenue:2000});
CREATE (:Company {id:30, name:'Core', revenue:1500});
CREATE (:City {id:100, name:'Seattle', region:'west'});
CREATE (:City {id:200, name:'Austin', region:'south'});
MATCH (p:Person {id:1}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2020}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2021}]->(c);
MATCH (p:Person {id:3}), (c:Company {id:20}) CREATE (p)-[:WORKS_AT {since:2022}]->(c);
MATCH (p:Person {id:4}), (c:Company {id:30}) CREATE (p)-[:WORKS_AT {since:2023}]->(c);
MATCH (p:Person {id:1}), (c:Company {id:20}) CREATE (p)-[:ADVISES {hours:10}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:20}) CREATE (p)-[:ADVISES {hours:5}]->(c);
MATCH (p:Person {id:3}), (c:Company {id:30}) CREATE (p)-[:ADVISES {hours:7}]->(c);
MATCH (c:Company {id:10}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2010}]->(city);
MATCH (c:Company {id:20}), (city:City {id:200}) CREATE (c)-[:LOCATED_IN {opened:2015}]->(city);
MATCH (c:Company {id:30}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2018}]->(city);
MATCH (c1:Company {id:10}), (c2:Company {id:20}) CREATE (c1)-[:PARTNERS_WITH {score:7}]->(c2);
MATCH (c1:Company {id:20}), (c2:Company {id:30}) CREATE (c1)-[:PARTNERS_WITH {score:5}]->(c2);

-- QUERY: undirected_works_at
MATCH (p:Person)-[r:WORKS_AT]-(c:Company) RETURN p.id, c.id, r.since ORDER BY p.id, c.id;

-- QUERY: undirected_located_in
MATCH (c:Company)-[r:LOCATED_IN]-(city:City) RETURN c.id, city.id, r.opened ORDER BY c.id, city.id;

-- QUERY: undirected_person_company_city
MATCH (p:Person)-[:WORKS_AT]-(c:Company)-[:LOCATED_IN]-(city:City)
RETURN p.id, c.id, city.id ORDER BY p.id, c.id, city.id;

-- QUERY: undirected_mixed_rel_counts
MATCH (p:Person)-[:WORKS_AT|ADVISES]-(c:Company) RETURN c.id, COUNT(*) AS cnt ORDER BY c.id;

-- QUERY: undirected_partner_pairs
MATCH (c1:Company)-[r:PARTNERS_WITH]-(c2:Company)
WHERE c1.id < c2.id
RETURN c1.id, c2.id, r.score ORDER BY c1.id, c2.id;

-- QUERY: undirected_partner_city_chain
MATCH (p:Person)-[:ADVISES]-(c1:Company)-[:PARTNERS_WITH]-(c2:Company)-[:LOCATED_IN]-(city:City)
RETURN p.name, c1.name, c2.name, city.name ORDER BY p.name, c1.name, c2.name, city.name;

-- QUERY: undirected_west_region_people
MATCH (p:Person)-[:WORKS_AT|ADVISES]-(c:Company)-[:LOCATED_IN]-(city:City)
WHERE city.region = 'west'
RETURN p.name, c.name, city.name ORDER BY p.name, c.name, city.name;

-- QUERY: undirected_dense_company_counts
MATCH (c1:Company)-[:PARTNERS_WITH]-(c2:Company)-[:LOCATED_IN]-(city:City)
RETURN city.name, COUNT(*) AS cnt ORDER BY city.name;
