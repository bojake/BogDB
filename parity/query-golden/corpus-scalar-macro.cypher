-- corpus-scalar-macro.cypher
-- G-GOLD-008: Scalar macro CREATE/CALL surface.
-- QUERY: create_macro_add10
CREATE MACRO Add10(x) AS x + 10;

-- QUERY: call_macro_add10
RETURN Add10(5);

-- QUERY: create_macro_default_param
CREATE MACRO AddDefault(x, y := 40) AS x + y;

-- QUERY: call_macro_default_param
RETURN AddDefault(2), AddDefault(2, 3);

-- QUERY: create_macro_no_params
CREATE MACRO ReturnConst() AS 42;

-- QUERY: call_macro_no_params
RETURN ReturnConst();
