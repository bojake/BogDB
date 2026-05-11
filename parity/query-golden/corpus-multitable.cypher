-- corpus-multitable.cypher
-- Multi-table graph: Person, Company, City with typed cross-table relationships.

-- SCHEMA
NODE Person   id:INT64  name:STRING  level:INT64
NODE Company  id:INT64  name:STRING  revenue:INT64
NODE City     id:INT64  name:STRING  region:STRING
REL  WORKS_AT    FROM:Person   TO:Company  since:INT64
REL  LOCATED_IN  FROM:Company  TO:City     opened:INT64

-- SETUP
CREATE (:Person {id:1, name:'Alice', level:3});
CREATE (:Person {id:2, name:'Bob', level:2});
CREATE (:Person {id:3, name:'Carol', level:5});
CREATE (:Company {id:10, name:'Acme', revenue:1000});
CREATE (:Company {id:20, name:'Beta', revenue:2000});
CREATE (:City {id:100, name:'Seattle', region:'west'});
CREATE (:City {id:200, name:'Austin', region:'south'});
MATCH (p:Person {id:1}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2020}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:10}) CREATE (p)-[:WORKS_AT {since:2021}]->(c);
MATCH (p:Person {id:3}), (c:Company {id:20}) CREATE (p)-[:WORKS_AT {since:2022}]->(c);
MATCH (c:Company {id:10}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2010}]->(city);
MATCH (c:Company {id:20}), (city:City {id:200}) CREATE (c)-[:LOCATED_IN {opened:2015}]->(city);

-- QUERY: person_scan
MATCH (p:Person) RETURN p.id, p.name, p.level ORDER BY p.id;

-- QUERY: company_scan
MATCH (c:Company) RETURN c.id, c.name, c.revenue ORDER BY c.id;

-- QUERY: city_scan
MATCH (city:City) RETURN city.id, city.name, city.region ORDER BY city.id;

-- QUERY: works_at_edges
MATCH (p:Person)-[r:WORKS_AT]->(c:Company) RETURN p.id, c.id, r.since ORDER BY p.id, c.id;

-- QUERY: located_in_edges
MATCH (c:Company)-[r:LOCATED_IN]->(city:City) RETURN c.id, city.id, r.opened ORDER BY c.id, city.id;

-- QUERY: company_employee_counts
MATCH (p:Person)-[:WORKS_AT]->(c:Company) RETURN c.id, COUNT(*) AS cnt ORDER BY c.id;

-- QUERY: person_company_city_chain
MATCH (p:Person)-[:WORKS_AT]->(c:Company)-[:LOCATED_IN]->(city:City)
RETURN p.id, c.id, city.id ORDER BY p.id, c.id, city.id;

-- QUERY: west_region_chain
MATCH (p:Person)-[:WORKS_AT]->(c:Company)-[:LOCATED_IN]->(city:City)
WHERE city.region = 'west'
RETURN p.name, c.name, city.name ORDER BY p.name, c.name;
