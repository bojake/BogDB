-- SCHEMA
NODE Message id:INT64 content:STRING
NODE Tag id:INT64 name:STRING
NODE Person id:INT64 name:STRING
NODE Company id:INT64 name:STRING
NODE University id:INT64 name:STRING
NODE City id:INT64 name:STRING
NODE Country id:INT64 name:STRING
NODE Continent id:INT64 name:STRING

REL HAS_TAG FROM:Message TO:Tag
REL HAS_CREATOR FROM:Message TO:Person
REL WORKS_AT FROM:Person TO:Company
REL STUDIES_AT FROM:Person TO:University
REL IS_LOCATED_IN FROM:Company TO:Country
REL IS_LOCATED_IN FROM:University TO:City
REL IS_LOCATED_IN FROM:Message TO:Country
REL IS_LOCATED_IN FROM:City TO:Country
REL IS_PART_OF FROM:Country TO:Continent
REL KNOWS FROM:Person TO:Person
REL REPLY_OF FROM:Message TO:Message

-- SETUP
CREATE (:Continent {id: 1, name: 'North America'});
CREATE (:Country {id: 2, name: 'USA'});
CREATE (:City {id: 3, name: 'New York'});
CREATE (:City {id: 4, name: 'San Francisco'});
CREATE (:Company {id: 10, name: 'TechCorp'});
CREATE (:University {id: 20, name: 'State Univ'});
CREATE (:Person {id: 100, name: 'Alice'});
CREATE (:Person {id: 101, name: 'Bob'});
CREATE (:Person {id: 102, name: 'Charlie'});
CREATE (:Message {id: 1000, content: 'Data Graph'});
CREATE (:Message {id: 1001, content: 'Analysis Mode'});
CREATE (:Message {id: 1002, content: 'Graphs'});
CREATE (:Tag {id: 5000, name: 'Database'});
CREATE (:Tag {id: 5001, name: 'Analytics'});

MATCH (c:Country {id: 2}), (cont:Continent {id: 1}) CREATE (c)-[:IS_PART_OF]->(cont);
MATCH (city:City {id: 3}), (c:Country {id: 2}) CREATE (city)-[:IS_LOCATED_IN]->(c);
MATCH (city:City {id: 4}), (c:Country {id: 2}) CREATE (city)-[:IS_LOCATED_IN]->(c);
MATCH (comp:Company {id: 10}), (c:Country {id: 2}) CREATE (comp)-[:IS_LOCATED_IN]->(c);
MATCH (u:University {id: 20}), (city:City {id: 3}) CREATE (u)-[:IS_LOCATED_IN]->(city);

MATCH (p:Person {id: 100}), (comp:Company {id: 10}) CREATE (p)-[:WORKS_AT]->(comp);
MATCH (p:Person {id: 101}), (comp:Company {id: 10}) CREATE (p)-[:WORKS_AT]->(comp);
MATCH (p:Person {id: 102}), (u:University {id: 20}) CREATE (p)-[:STUDIES_AT]->(u);

MATCH (m:Message {id: 1000}), (p:Person {id: 100}) CREATE (m)-[:HAS_CREATOR]->(p);
MATCH (m:Message {id: 1001}), (p:Person {id: 101}) CREATE (m)-[:HAS_CREATOR]->(p);
MATCH (m:Message {id: 1002}), (p:Person {id: 102}) CREATE (m)-[:HAS_CREATOR]->(p);

MATCH (m:Message {id: 1000}), (c:Country {id: 2}) CREATE (m)-[:IS_LOCATED_IN]->(c);
MATCH (m:Message {id: 1001}), (c:Country {id: 2}) CREATE (m)-[:IS_LOCATED_IN]->(c);
MATCH (m:Message {id: 1002}), (c:Country {id: 2}) CREATE (m)-[:IS_LOCATED_IN]->(c);

MATCH (m:Message {id: 1000}), (t:Tag {id: 5000}) CREATE (m)-[:HAS_TAG]->(t);
MATCH (m:Message {id: 1001}), (t:Tag {id: 5001}) CREATE (m)-[:HAS_TAG]->(t);
MATCH (m:Message {id: 1002}), (t:Tag {id: 5000}) CREATE (m)-[:HAS_TAG]->(t);

MATCH (m1:Message {id: 1001}), (m2:Message {id: 1000}) CREATE (m1)-[:REPLY_OF]->(m2);

MATCH (a:Person {id: 100}), (b:Person {id: 101}) CREATE (a)-[:KNOWS]->(b), (b)-[:KNOWS]->(a);
MATCH (b:Person {id: 101}), (c:Person {id: 102}) CREATE (b)-[:KNOWS]->(c), (c)-[:KNOWS]->(b);

-- QUERY: lsqb_q1
MATCH (city:City)<-[:IS_LOCATED_IN]-(u:University)<-[:STUDIES_AT]-(p:Person)<-[:HAS_CREATOR]-(m1:Message)-[:REPLY_OF]->(m2:Message)-[:HAS_TAG]->(t:Tag)
RETURN count(*) AS cnt;

-- QUERY: lsqb_q2_triangle
MATCH (p1:Person)-[:KNOWS]->(p2:Person)<-[:HAS_CREATOR]-(m:Message)-[:HAS_CREATOR]->(p1)
RETURN count(*) AS cnt;

-- QUERY: lsqb_q3
MATCH (city1:City)<-[:IS_LOCATED_IN]-(u:University)<-[:STUDIES_AT]-(p1:Person)-[:KNOWS]->(p2:Person)-[:KNOWS]->(p3:Person)-[:WORKS_AT]->(c:Company)-[:IS_LOCATED_IN]->(country:Country)
RETURN count(*) AS cnt;

-- QUERY: lsqb_q4
MATCH (t:Tag)<-[:HAS_TAG]-(m:Message)-[:HAS_CREATOR]->(p:Person)-[:KNOWS]->(p2:Person)<-[:HAS_CREATOR]-(m2:Message)-[:REPLY_OF]->(m3:Message)-[:HAS_TAG]->(t2:Tag)
RETURN count(*) AS cnt;
