-- SCHEMA
NODE Dummy id:INT64

-- SETUP

-- QUERY: load_from_parquet
LOAD FROM '{fixture:dummy.parquet}' RETURN *;

-- QUERY: copy_to_parquet
COPY (MATCH (d:Dummy) RETURN d.*) TO '{fixture:dummy.parquet}';
