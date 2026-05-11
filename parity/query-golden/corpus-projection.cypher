-- corpus-projection.cypher
-- WITH, aliases, ORDER BY multi-key, SKIP, LIMIT.

-- SCHEMA
NODE Record  id:INT64  category:STRING  value:INT64

-- SETUP
CREATE (:Record {id:1,  category:'X', value:30});
CREATE (:Record {id:2,  category:'Y', value:10});
CREATE (:Record {id:3,  category:'X', value:50});
CREATE (:Record {id:4,  category:'Z', value:20});
CREATE (:Record {id:5,  category:'Y', value:40});
CREATE (:Record {id:6,  category:'Z', value:15});
CREATE (:Record {id:7,  category:'X', value:5});
CREATE (:Record {id:8,  category:'Y', value:25});
CREATE (:Record {id:9,  category:'Z', value:60});
CREATE (:Record {id:10, category:'X', value:35});

-- QUERY: order_by_value_asc
MATCH (r:Record) RETURN r.id, r.value ORDER BY r.value ASC;

-- QUERY: order_by_category_asc_value_desc
MATCH (r:Record) RETURN r.category, r.value ORDER BY r.category ASC, r.value DESC;

-- QUERY: limit_3
MATCH (r:Record) RETURN r.id ORDER BY r.id LIMIT 3;

-- QUERY: skip_7
MATCH (r:Record) RETURN r.id ORDER BY r.id SKIP 7;

-- QUERY: skip_3_limit_4
MATCH (r:Record) RETURN r.id ORDER BY r.id SKIP 3 LIMIT 4;

-- QUERY: with_alias_filter
MATCH (r:Record) WITH r.category AS cat, r.value AS v WHERE v > 25 RETURN cat, v ORDER BY v ASC;

-- QUERY: with_aggregate_then_filter
MATCH (r:Record) WITH r.category AS cat, SUM(r.value) AS total WHERE total > 80 RETURN cat, total ORDER BY cat;

-- QUERY: aliased_expression
MATCH (r:Record) RETURN r.id, r.value * 2 AS doubled ORDER BY r.id LIMIT 5;

-- QUERY: union_all_preserves_duplicates
UNWIND [1, 2] AS x RETURN x AS v
UNION ALL
UNWIND [2, 3] AS x RETURN x AS v
ORDER BY v;

-- QUERY: union_removes_duplicates
UNWIND [1, 2] AS x RETURN x AS v
UNION
UNWIND [2, 3] AS x RETURN x AS v
ORDER BY v;
