-- corpus-nulls.cypher
-- Null literal behavior, omitted-property scans, coalesce, and aggregate null handling.

-- SCHEMA
NODE Person  id:INT64  nickname:STRING  age:INT64  active:BOOL

-- SETUP
CREATE (:Person {id:1, nickname:'Ace', age:30, active:true});
CREATE (:Person {id:2, nickname:NULL, age:NULL, active:false});
CREATE (:Person {id:3, age:22});
CREATE (:Person {id:4, nickname:'Zero', active:NULL});

-- QUERY: scan_all_rows
MATCH (p:Person) RETURN p.id, p.nickname, p.age, p.active ORDER BY p.id;

-- QUERY: nickname_is_null
MATCH (p:Person) WHERE p.nickname IS NULL RETURN p.id ORDER BY p.id;

-- QUERY: age_is_not_null
MATCH (p:Person) WHERE p.age IS NOT NULL RETURN p.id ORDER BY p.id;

-- QUERY: active_is_null
MATCH (p:Person) WHERE p.active IS NULL RETURN p.id ORDER BY p.id;

-- QUERY: null_literal_projection
RETURN NULL AS value;

-- QUERY: coalesce_nickname
MATCH (p:Person) RETURN p.id, coalesce(p.nickname, 'anon') AS nickname ORDER BY p.id;

-- QUERY: count_age_ignores_nulls
MATCH (p:Person) RETURN COUNT(p.age) AS cnt;

-- QUERY: count_null_literal
RETURN COUNT(NULL) AS cnt;
