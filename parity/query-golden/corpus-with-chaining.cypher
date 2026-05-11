-- corpus-with-chaining.cypher
-- Multi-step WITH pipelines: intermediate projections, aggregation staging, ORDER/LIMIT chaining.

-- SCHEMA
NODE Sale  id:INT64  region:STRING  amount:INT64  year:INT64

-- SETUP
CREATE (:Sale {id:1, region:'west',  amount:100, year:2021});
CREATE (:Sale {id:2, region:'east',  amount:200, year:2021});
CREATE (:Sale {id:3, region:'west',  amount:150, year:2022});
CREATE (:Sale {id:4, region:'east',  amount:300, year:2022});
CREATE (:Sale {id:5, region:'west',  amount:250, year:2023});
CREATE (:Sale {id:6, region:'south', amount:400, year:2023});
CREATE (:Sale {id:7, region:'south', amount:350, year:2022});
CREATE (:Sale {id:8, region:'east',  amount:180, year:2023});

-- QUERY: with_computed_filter
MATCH (s:Sale)
WITH s.region AS region, s.amount AS amt
WHERE amt > 200
RETURN region, amt ORDER BY region, amt;

-- QUERY: with_aggregation_stage
MATCH (s:Sale)
WITH s.region AS region, SUM(s.amount) AS total
RETURN region, total ORDER BY region;

-- QUERY: with_having_equivalent
MATCH (s:Sale)
WITH s.region AS region, COUNT(*) AS cnt
WHERE cnt > 1
RETURN region, cnt ORDER BY region;

-- QUERY: with_double_aggregate
MATCH (s:Sale)
WITH s.year AS yr, SUM(s.amount) AS yr_total
WITH AVG(yr_total) AS avg_year_total
RETURN avg_year_total;

-- QUERY: with_limit_in_pipe
MATCH (s:Sale)
WITH s.id AS id, s.amount AS amt ORDER BY amt DESC LIMIT 3
RETURN id, amt;

-- QUERY: with_rename_and_reuse
MATCH (s:Sale)
WITH s.amount * 2 AS doubled, s.region AS r
RETURN r, doubled ORDER BY r, doubled;

-- QUERY: with_chained_filter_and_count
MATCH (s:Sale)
WITH s.year AS yr, s.amount AS amt
WHERE yr >= 2022
WITH yr, COUNT(*) AS cnt
RETURN yr, cnt ORDER BY yr;

-- QUERY: with_max_per_region
MATCH (s:Sale)
WITH s.region AS region, MAX(s.amount) AS peak
RETURN region, peak ORDER BY region;
