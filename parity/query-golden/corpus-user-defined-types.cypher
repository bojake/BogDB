-- SCHEMA

-- SETUP

-- QUERY: create_type
CREATE TYPE MyType AS STRUCT(a INT64, b STRING);

-- QUERY: cast_to_custom_type
RETURN cast({a: 1, b: 'hi'}, 'MyType');
