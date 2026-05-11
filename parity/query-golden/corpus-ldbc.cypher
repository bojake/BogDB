-- SCHEMA
NODE Person id:INT64 firstName:STRING lastName:STRING
NODE Message id:INT64 content:STRING length:INT64 creationDate:TIMESTAMP
NODE Forum id:INT64 title:STRING
NODE Tag id:INT64 name:STRING
REL KNOWS FROM:Person TO:Person creationDate:DATE
REL HAS_CREATOR FROM:Message TO:Person
REL REPLY_OF FROM:Message TO:Message
REL CONTAINER_OF FROM:Forum TO:Message
REL HAS_TAG FROM:Message TO:Tag

-- SETUP
CREATE (:Person {id: 1, firstName: 'Alice', lastName: 'A'});
CREATE (:Person {id: 2, firstName: 'Bob', lastName: 'B'});
CREATE (:Person {id: 3, firstName: 'Charlie', lastName: 'C'});
CREATE (:Person {id: 4, firstName: 'David', lastName: 'D'});

CREATE (:Message {id: 101, content: 'Hello', length: 5, creationDate: timestamp('2026-01-01 10:00:00')});
CREATE (:Message {id: 102, content: 'World', length: 5, creationDate: timestamp('2026-01-02 11:00:00')});
CREATE (:Message {id: 103, content: 'Reply 1', length: 7, creationDate: timestamp('2026-01-03 12:00:00')});

CREATE (:Forum {id: 1001, title: 'Main Forum'});

CREATE (:Tag {id: 10001, name: 'Greeting'});
CREATE (:Tag {id: 10002, name: 'Discussion'});

MATCH (a:Person {id: 1}), (b:Person {id: 2}) CREATE (a)-[:KNOWS {creationDate: date('2026-01-01')}]->(b), (b)-[:KNOWS {creationDate: date('2026-01-01')}]->(a);
MATCH (b:Person {id: 2}), (c:Person {id: 3}) CREATE (b)-[:KNOWS {creationDate: date('2026-01-02')}]->(c), (c)-[:KNOWS {creationDate: date('2026-01-02')}]->(b);
MATCH (a:Person {id: 1}), (d:Person {id: 4}) CREATE (a)-[:KNOWS {creationDate: date('2026-01-03')}]->(d), (d)-[:KNOWS {creationDate: date('2026-01-03')}]->(a);

MATCH (m:Message {id: 101}), (p:Person {id: 1}) CREATE (m)-[:HAS_CREATOR]->(p);
MATCH (m:Message {id: 102}), (p:Person {id: 2}) CREATE (m)-[:HAS_CREATOR]->(p);
MATCH (m:Message {id: 103}), (p:Person {id: 3}) CREATE (m)-[:HAS_CREATOR]->(p);

MATCH (m1:Message {id: 103}), (m2:Message {id: 101}) CREATE (m1)-[:REPLY_OF]->(m2);

MATCH (f:Forum {id: 1001}), (m:Message {id: 101}) CREATE (f)-[:CONTAINER_OF]->(m);
MATCH (f:Forum {id: 1001}), (m:Message {id: 102}) CREATE (f)-[:CONTAINER_OF]->(m);

MATCH (m:Message {id: 101}), (t:Tag {id: 10001}) CREATE (m)-[:HAS_TAG]->(t);
MATCH (m:Message {id: 102}), (t:Tag {id: 10002}) CREATE (m)-[:HAS_TAG]->(t);
MATCH (m:Message {id: 103}), (t:Tag {id: 10001}) CREATE (m)-[:HAS_TAG]->(t);

-- QUERY: ldbc_interactive_1_friends_of_friends
MATCH (p:Person {id: 1})-[:KNOWS*1..2]->(friend:Person)
WHERE friend.firstName STARTS WITH 'C' AND friend.id <> 1
RETURN friend.id, friend.firstName, friend.lastName
ORDER BY friend.id;

-- QUERY: ldbc_interactive_2_latest_messages
MATCH (p:Person {id: 1})-[:KNOWS]->(friend:Person)<-[:HAS_CREATOR]-(m:Message)
WHERE m.creationDate < timestamp('2026-12-31 00:00:00')
RETURN friend.id, friend.firstName, m.id, m.content, m.creationDate
ORDER BY m.creationDate DESC, m.id ASC
LIMIT 10;

-- QUERY: ldbc_interactive_3_stub
MATCH (p:Person {id: 1})-[:KNOWS*1..2]->(friend:Person)<-[:HAS_CREATOR]-(m:Message)-[:HAS_TAG]->(t:Tag {name: 'Greeting'})
WHERE p.id <> friend.id
RETURN friend.id, count(m) AS msgCount
ORDER BY msgCount DESC, friend.id ASC;

-- QUERY: ldbc_complex_joins
MATCH (p:Person)-[:KNOWS]->(:Person)<-[:HAS_CREATOR]-(m:Message)-[:REPLY_OF]->(m2:Message)-[:HAS_TAG]->(t:Tag)
RETURN p.firstName, m.id, m2.id, t.name
ORDER BY p.id;
