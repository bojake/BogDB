-- corpus-agg.cypher
-- Aggregate functions: COUNT, SUM, AVG, MIN, MAX, GROUP BY, HAVING, DISTINCT.

-- SCHEMA
NODE Employee  id:INT64  dept:STRING  salary:INT64

-- SETUP
CREATE (:Employee {id:1, dept:'eng',  salary:100000});
CREATE (:Employee {id:2, dept:'eng',  salary:120000});
CREATE (:Employee {id:3, dept:'eng',  salary:90000});
CREATE (:Employee {id:4, dept:'mkt',  salary:80000});
CREATE (:Employee {id:5, dept:'mkt',  salary:95000});
CREATE (:Employee {id:6, dept:'ops',  salary:70000});

-- QUERY: count_all
MATCH (e:Employee) RETURN COUNT(*) AS cnt;

-- QUERY: count_by_dept
MATCH (e:Employee) RETURN e.dept, COUNT(*) AS cnt ORDER BY e.dept;

-- QUERY: sum_salary_by_dept
MATCH (e:Employee) RETURN e.dept, SUM(e.salary) AS total ORDER BY e.dept;

-- QUERY: avg_salary_by_dept
MATCH (e:Employee) RETURN e.dept, AVG(e.salary) AS avg_sal ORDER BY e.dept;

-- QUERY: min_max_salary
MATCH (e:Employee) RETURN MIN(e.salary) AS min_sal, MAX(e.salary) AS max_sal;

-- QUERY: having_dept_count_gt_1
MATCH (e:Employee) WITH e.dept AS dept, COUNT(*) AS cnt WHERE cnt > 1 RETURN dept, cnt ORDER BY dept;

-- QUERY: distinct_depts
MATCH (e:Employee) RETURN DISTINCT e.dept ORDER BY e.dept;

-- QUERY: count_distinct_depts
MATCH (e:Employee) RETURN COUNT(DISTINCT e.dept) AS unique_depts;
