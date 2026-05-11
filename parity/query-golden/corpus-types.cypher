-- corpus-types.cypher
-- Type coverage beyond INT64/STRING: INT16, INT32, FLOAT, DOUBLE, BOOL.

-- SCHEMA
NODE Metric  id:INT64  small:INT16  qty:INT32  ratio:FLOAT  exact:DOUBLE  active:BOOL  label:STRING

-- SETUP
CREATE (:Metric {id:1, active:true,  label:'alpha'});
CREATE (:Metric {id:2, active:false, label:'beta'});
CREATE (:Metric {id:3, active:true,  label:'gamma'});
CREATE (:Metric {id:4, active:false, label:'delta'});
MATCH (m:Metric {id:1}) SET m.small = CAST(7 AS INT16),  m.qty = CAST(100 AS INT32), m.ratio = CAST(1.5 AS FLOAT),  m.exact = CAST(1.125 AS DOUBLE);
MATCH (m:Metric {id:2}) SET m.small = CAST(3 AS INT16),  m.qty = CAST(40 AS INT32),  m.ratio = CAST(2.75 AS FLOAT), m.exact = CAST(9.5 AS DOUBLE);
MATCH (m:Metric {id:3}) SET m.small = CAST(12 AS INT16), m.qty = CAST(5 AS INT32),   m.ratio = CAST(0.25 AS FLOAT), m.exact = CAST(3.14159 AS DOUBLE);
MATCH (m:Metric {id:4}) SET m.small = CAST(1 AS INT16),  m.qty = CAST(250 AS INT32), m.ratio = CAST(5.5 AS FLOAT),  m.exact = CAST(2.5 AS DOUBLE);

-- QUERY: scan_all_rows
MATCH (m:Metric) RETURN m.id, m.small, m.qty, m.ratio, m.exact, m.active, m.label ORDER BY m.id;

-- QUERY: bool_true_filter
MATCH (m:Metric) WHERE m.active = true RETURN m.id, m.label ORDER BY m.id;

-- QUERY: float_threshold
MATCH (m:Metric) WHERE m.ratio > 1.5 RETURN m.id, m.ratio ORDER BY m.id;

-- QUERY: double_order_desc
MATCH (m:Metric) RETURN m.label, m.exact ORDER BY m.exact DESC;

-- QUERY: int_arithmetic_projection
MATCH (m:Metric) RETURN m.id, m.small + m.qty AS total ORDER BY m.id;

-- QUERY: mixed_numeric_projection
MATCH (m:Metric) RETURN m.id, m.qty / 2 AS half_qty, m.exact + m.ratio AS combined ORDER BY m.id;

-- QUERY: aggregate_sum_int32
MATCH (m:Metric) RETURN SUM(m.qty) AS total_qty;

-- QUERY: aggregate_avg_double
MATCH (m:Metric) RETURN AVG(m.exact) AS avg_exact;
