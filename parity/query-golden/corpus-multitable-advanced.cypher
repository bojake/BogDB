-- corpus-multitable-advanced.cypher
-- Advanced multi-table graph coverage: incoming patterns, mixed rel types, denser endpoint filtering.

-- SCHEMA
NODE Person   id:INT64  name:STRING  level:INT64
NODE Company  id:INT64  name:STRING  revenue:INT64
NODE City     id:INT64  name:STRING  region:STRING
REL  WORKS_AT    FROM:Person   TO:Company  since:INT64
REL  ADVISES     FROM:Person   TO:Company  hours:INT64
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
MATCH (p:Person {id:1}), (c:Company {id:20}) CREATE (p)-[:ADVISES {hours:10}]->(c);
MATCH (p:Person {id:2}), (c:Company {id:20}) CREATE (p)-[:ADVISES {hours:5}]->(c);
MATCH (c:Company {id:10}), (city:City {id:100}) CREATE (c)-[:LOCATED_IN {opened:2010}]->(city);
MATCH (c:Company {id:20}), (city:City {id:200}) CREATE (c)-[:LOCATED_IN {opened:2015}]->(city);

-- QUERY: incoming_works_at
MATCH (c:Company)<-[r:WORKS_AT]-(p:Person) RETURN c.id, p.id, r.since ORDER BY c.id, p.id;

-- QUERY: incoming_located_in
MATCH (city:City)<-[r:LOCATED_IN]-(c:Company) RETURN city.id, c.id, r.opened ORDER BY city.id, c.id;

-- QUERY: incoming_company_employee_counts
MATCH (c:Company)<-[:WORKS_AT]-(p:Person) RETURN c.id, COUNT(*) AS cnt ORDER BY c.id;

-- QUERY: incoming_city_chain
MATCH (city:City)<-[:LOCATED_IN]-(c:Company)<-[:WORKS_AT]-(p:Person)
RETURN city.id, c.id, p.id ORDER BY city.id, c.id, p.id;

-- QUERY: multi_rel_company_counts
MATCH (p:Person)-[:WORKS_AT|ADVISES]->(c:Company) RETURN c.id, COUNT(*) AS cnt ORDER BY c.id;

-- QUERY: advised_company_people
MATCH (c:Company)<-[r:ADVISES]-(p:Person) RETURN c.id, p.name, r.hours ORDER BY c.id, p.name;

-- QUERY: incoming_with_endpoint_filter
MATCH (c:Company)<-[:WORKS_AT]-(p:Person)
WHERE c.revenue >= 1500
RETURN p.name, c.name ORDER BY p.name, c.name;

-- QUERY: city_region_incoming_union
MATCH (city:City)<-[:LOCATED_IN]-(c:Company)<-[:WORKS_AT|ADVISES]-(p:Person)
WHERE city.region = 'south'
RETURN p.name, c.name, city.name ORDER BY p.name, c.name;
