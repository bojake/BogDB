-- SCHEMA

-- SETUP

-- QUERY: load_csv_missing
LOAD FROM '{fixture:missing.csv}' RETURN *;

-- QUERY: load_csv_typed_missing
LOAD WITH HEADERS (id INT64) FROM '{fixture:missing.csv}' RETURN *;
