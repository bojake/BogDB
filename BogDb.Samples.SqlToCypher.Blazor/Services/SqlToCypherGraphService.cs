using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Samples.SqlToCypher.Blazor.Services;

// ── Result types ───────────────────────────────────────────────────────────────

public record QueryResult(bool IsSuccess, string Error,
    List<string> Columns, List<Dictionary<string, object?>> Rows, long ElapsedMs);

public record NodeTableInfo(string Name, List<string> Properties);
public record RelTableInfo(string Name, string From, string To, List<string> Properties);
public record SchemaInfo(List<NodeTableInfo> NodeTables, List<RelTableInfo> RelTables);

// ── Lesson catalogue ──────────────────────────────────────────────────────────

public record Lesson(
    int Number,
    string Category,
    string Title,
    string Concept,
    string Explanation,
    string Sql,
    string Cypher,
    string[] KeyDifferences,
    string TryItPrompt,
    int ExpectedRowCount = 0,
    string[] ExpectedColumns = default!);

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton that owns the BogDB in-memory graph for the SQL-to-Cypher learning sample.
///
/// Seeds three relatable domains:
///   1. E-commerce  — Customer, Product, Order, Category
///   2. Employees   — Employee, Department, Project
///   3. Movies      — Movie, Person, Genre
///
/// Exposes Execute(), GetSchemaInfo(), and GetLessons().
/// </summary>
public sealed class SqlToCypherGraphService
{
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    public SqlToCypherGraphService()
    {
        _db   = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
        SetupSchema();
        SeedData();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public QueryResult Execute(string cypher)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r  = _conn.Query(cypher);
        sw.Stop();
        if (!r.IsSuccess)
            return new QueryResult(false, r.ErrorMessage ?? "Query failed", [], [], sw.ElapsedMilliseconds);

        var cols = r.ColumnNames.ToList();
        var rows = new List<Dictionary<string, object?>>();
        while (r.HasNext())
        {
            var row = r.GetNext();
            rows.Add(row.GetAsDictionary().ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        }
        return new QueryResult(true, string.Empty, cols, rows, sw.ElapsedMilliseconds);
    }

    public SchemaInfo GetSchemaInfo()
    {
        var nodes = new List<NodeTableInfo>
        {
            new("Customer",   ["id","name","email","country","joined_year"]),
            new("Product",    ["id","name","price","stock"]),
            new("Order",      ["id","amount","status","order_date"]),
            new("Category",   ["id","name"]),
            new("Employee",   ["id","name","title","salary","hire_year"]),
            new("Department", ["id","name","budget"]),
            new("Project",    ["id","name","status","budget"]),
            new("Movie",      ["id","title","year","rating"]),
            new("Person",     ["id","name","born"]),
            new("Genre",      ["id","name"]),
        };
        var rels = new List<RelTableInfo>
        {
            new("PLACED",       "Customer",  "Order",      []),
            new("CONTAINS",     "Order",     "Product",    ["qty"]),
            new("BELONGS_TO",   "Product",   "Category",   []),
            new("WORKS_IN",     "Employee",  "Department", []),
            new("MANAGES",      "Employee",  "Employee",   []),
            new("ASSIGNED_TO",  "Employee",  "Project",    ["role"]),
            new("ACTED_IN",     "Person",    "Movie",      ["role"]),
            new("DIRECTED",     "Person",    "Movie",      []),
            new("IN_GENRE",     "Movie",     "Genre",      []),
        };
        return new SchemaInfo(nodes, rels);
    }

    // ── Lesson catalogue ──────────────────────────────────────────────────────

    public static List<Lesson> GetLessons() =>
    [
        // ── SELECT / Filter ──────────────────────────────────────────────────
        new(1, "Select",
            "Basic SELECT → MATCH / RETURN",
            "Retrieve all rows from a table",
            "In SQL, SELECT * FROM retrieves every row in a table. In Cypher, MATCH (n:Label) binds every node of that label. RETURN is used instead of SELECT to choose what to project.",
            """
            SELECT *
            FROM Customer;
            """,
            """
            MATCH (c:Customer)
            RETURN c.id, c.name, c.email, c.country;
            """,
            [
                "SQL has tables; Cypher has labeled nodes",
                "MATCH replaces FROM — it describes a pattern to find",
                "RETURN replaces SELECT — it projects the output",
                "No semicolon required in Cypher (but allowed)",
            ],
            "Try adding ORDER BY c.name at the end",
            ExpectedRowCount: 12,
            ExpectedColumns: ["c.id", "c.name", "c.email", "c.country"]),

        new(2, "Filter",
            "WHERE clause",
            "Filter rows by a condition",
            "Both SQL and Cypher use WHERE to filter. The syntax is nearly identical — the key difference is that Cypher uses dot notation on node aliases (c.country) rather than bare column names.",
            """
            SELECT name, email
            FROM Customer
            WHERE country = 'US';
            """,
            """
            MATCH (c:Customer)
            WHERE c.country = 'US'
            RETURN c.name, c.email;
            """,
            [
                "WHERE works the same in both languages",
                "Cypher properties use dot notation: c.country",
                "String literals use single quotes in both",
                "You can also use inline property matching: MATCH (c:Customer {country: 'US'})",
            ],
            "Try changing 'US' to 'UK' or add AND c.joined_year > 2020",
            ExpectedRowCount: 5,
            ExpectedColumns: ["c.name", "c.email"]),

        new(3, "Projection",
            "Column selection",
            "Return only specific columns/properties",
            "SQL's SELECT column list and Cypher's RETURN clause both control what is projected in the output. In Cypher, you access properties via the node alias.",
            """
            SELECT name, country, joined_year
            FROM Customer;
            """,
            """
            MATCH (c:Customer)
            RETURN c.name, c.country, c.joined_year;
            """,
            [
                "Both use a projection list to select which fields to return",
                "Cypher properties: alias.property (e.g. c.name)",
                "You can return the whole node object with just RETURN c",
            ],
            "Try returning only c.name and adding LIMIT 5",
            ExpectedRowCount: 12,
            ExpectedColumns: ["c.name", "c.country", "c.joined_year"]),

        new(4, "Aliases",
            "Column aliases (AS)",
            "Rename output columns",
            "AS works identically in SQL and Cypher to rename an output column or computed expression.",
            """
            SELECT name AS customer_name,
                   joined_year AS member_since
            FROM Customer;
            """,
            """
            MATCH (c:Customer)
            RETURN c.name        AS customer_name,
                   c.joined_year AS member_since;
            """,
            [
                "AS renames output in both SQL and Cypher",
                "In Cypher, AS can also be used inside WITH to carry values forward",
            ],
            "Try adding a computed alias: RETURN c.name AS name, 2025 - c.joined_year AS years_as_member",
            ExpectedRowCount: 12,
            ExpectedColumns: ["customer_name", "member_since"]),

        new(5, "Sort & Limit",
            "ORDER BY + LIMIT",
            "Sort results and take the top N",
            "ORDER BY and LIMIT syntax is essentially identical in SQL and Cypher. DESC/ASC work the same way.",
            """
            SELECT name, price
            FROM Product
            ORDER BY price DESC
            LIMIT 5;
            """,
            """
            MATCH (p:Product)
            RETURN p.name, p.price
            ORDER BY p.price DESC
            LIMIT 5;
            """,
            [
                "ORDER BY / LIMIT syntax is identical",
                "SKIP is Cypher's equivalent of SQL's OFFSET",
                "LIMIT goes at the very end of the query in both languages",
            ],
            "Add SKIP 5 after LIMIT 5 to implement a second page",
            ExpectedRowCount: 5,
            ExpectedColumns: ["p.name", "p.price"]),

        // ── Aggregation ─────────────────────────────────────────────────────
        new(6, "Count",
            "COUNT(*) aggregation",
            "Count the total number of rows/nodes",
            "COUNT(*) works the same in both languages. Cypher also supports COUNT(DISTINCT expr) for counting unique values.",
            """
            SELECT COUNT(*) AS total_customers
            FROM Customer;
            """,
            """
            MATCH (c:Customer)
            RETURN COUNT(*) AS total_customers;
            """,
            [
                "COUNT(*) syntax is identical",
                "COUNT(DISTINCT c.country) counts unique values",
                "Cypher does not need GROUP BY when there is no grouping key — a single aggregate is returned",
            ],
            "Try COUNT(DISTINCT c.country) AS unique_countries",
            ExpectedRowCount: 1,
            ExpectedColumns: ["total_customers"]),

        new(7, "Sum & Avg",
            "SUM / AVG / MIN / MAX",
            "Compute numeric aggregates",
            "Aggregate functions SUM, AVG, MIN, MAX behave identically in SQL and Cypher. They appear in the RETURN clause.",
            """
            SELECT SUM(amount)  AS total_revenue,
                   AVG(amount)  AS avg_order,
                   MAX(amount)  AS largest_order
            FROM Order;
            """,
            """
            MATCH (o:`Order`)
            RETURN SUM(o.amount)  AS total_revenue,
                   AVG(o.amount)  AS avg_order,
                   MAX(o.amount)  AS largest_order;
            """,
            [
                "SUM, AVG, MIN, MAX have identical syntax",
                "Functions are applied in RETURN (or WITH) not in a SELECT list",
                "These functions automatically ignore NULL values, same as SQL",
            ],
            "Add MIN(o.amount) AS smallest_order to the query",
            ExpectedRowCount: 1,
            ExpectedColumns: ["total_revenue", "avg_order", "largest_order"]),

        new(8, "Group By",
            "GROUP BY → WITH grouping",
            "Aggregate per group",
            "SQL uses GROUP BY after WHERE. Cypher uses WITH to carry a grouping key and aggregate forward — any non-aggregated term in WITH implicitly acts as the group key.",
            """
            SELECT country,
                   COUNT(*) AS customer_count
            FROM Customer
            GROUP BY country
            ORDER BY customer_count DESC;
            """,
            """
            MATCH (c:Customer)
            WITH c.country AS country, COUNT(*) AS customer_count
            RETURN country, customer_count
            ORDER BY customer_count DESC;
            """,
            [
                "Cypher uses WITH instead of GROUP BY",
                "Any non-aggregate expression in WITH is the implicit group key",
                "Aggregated aliases from WITH can be referenced in RETURN and ORDER BY",
                "HAVING in SQL ≈ adding WHERE after WITH in Cypher",
            ],
            "Add WHERE customer_count > 1 after the WITH clause to replicate HAVING",
            ExpectedRowCount: 7,
            ExpectedColumns: ["country", "customer_count"]),

        // ── Joins / Traversals ───────────────────────────────────────────────
        new(9, "One-Hop Join",
            "INNER JOIN → single relationship hop",
            "Traverse one relationship between two node types",
            "A SQL JOIN links two tables via a foreign key. In Cypher, you simply include the relationship in the MATCH pattern — no ON clause needed. The relationship type (like :PLACED) acts as the join condition.",
            """
            SELECT c.name, o.amount, o.status
            FROM Customer c
            INNER JOIN Order o ON o.customer_id = c.id;
            """,
            """
            MATCH (c:Customer)-[:PLACED]->(o:`Order`)
            RETURN c.name, o.amount, o.status;
            """,
            [
                "No ON clause — the relationship itself IS the join condition",
                "Arrow direction (--> vs <--) encodes the relationship direction",
                "The relationship type [:PLACED] replaces the foreign-key column reference",
                "Undirected traversal: MATCH (c:Customer)-[:PLACED]-(o:`Order`)",
            ],
            "Add WHERE o.status = 'SHIPPED' to filter joined results",
            ExpectedRowCount: 15,
            ExpectedColumns: ["c.name", "o.amount", "o.status"]),

        new(10, "Multi-Hop Join",
            "Multiple JOINs → chained traversal",
            "Traverse multiple relationships in one MATCH",
            "Where SQL needs multiple JOIN clauses to traverse through multiple tables, Cypher extends the MATCH pattern naturally — just keep appending relationships and nodes.",
            """
            SELECT c.name, p.name AS product, p.price
            FROM Customer c
            JOIN Order o   ON o.customer_id = c.id
            JOIN OrderItem oi ON oi.order_id = o.id
            JOIN Product p ON oi.product_id = p.id;
            """,
            """
            MATCH (c:Customer)-[:PLACED]->(o:`Order`)
                  -[:CONTAINS]->(p:Product)
            RETURN c.name, p.name AS product, p.price;
            """,
            [
                "Each additional JOIN in SQL = extending the MATCH pattern in Cypher",
                "No join-table aliases needed — the pattern is self-documenting",
                "The entire path is declared in one MATCH clause",
                "Cypher traversal scales naturally to 5, 10, or 15 hops",
            ],
            "Extend the pattern: add -[:BELONGS_TO]->(cat:Category) and RETURN cat.name",
            ExpectedRowCount: 32,
            ExpectedColumns: ["c.name", "product", "p.price"]),

        new(11, "Self Join",
            "Self JOIN → recursive relationship",
            "Query a table that references itself",
            "SQL self-joins are awkward — you need two aliases for the same table and a join on a self-referencing FK. Cypher models this naturally with a relationship between nodes of the same label.",
            """
            SELECT e.name AS employee,
                   m.name AS manager
            FROM Employee e
            LEFT JOIN Employee m ON e.manager_id = m.id;
            """,
            """
            MATCH (e:Employee)-[:MANAGES]->(r:Employee)
            RETURN e.name AS manager, r.name AS report;
            """,
            [
                "No table aliasing trick needed — just match nodes of the same label",
                "Relationship direction clarifies who manages whom",
                "You can also find all employees without a manager: WHERE NOT ()-[:MANAGES]->(e)",
            ],
            "Try finding employees who manage more than 2 people: WITH e, COUNT(r) AS reports WHERE reports > 2",
            ExpectedRowCount: 11,
            ExpectedColumns: ["manager", "report"]),

        new(12, "Transitive Hops",
            "Recursive CTE → variable-length path (*1..N)",
            "Follow a relationship to any depth",
            "SQL requires a WITH RECURSIVE common table expression to traverse hierarchies. Cypher expresses this in the relationship pattern itself using *1..N — much more concise.",
            """
            WITH RECURSIVE chain AS (
              SELECT id, name, manager_id, 1 AS depth FROM Employee WHERE name = 'Alice Smith'
              UNION ALL
              SELECT e.id, e.name, e.manager_id, c.depth+1
              FROM Employee e JOIN chain c ON e.manager_id = c.id
              WHERE c.depth < 3
            )
            SELECT name, depth FROM chain;
            """,
            """
            MATCH (mgr:Employee {name: 'Alice Smith'})
                  -[:MANAGES*1..3]->(report:Employee)
            RETURN report.name, report.title;
            """,
            [
                "*1..3 means: follow MANAGES between 1 and 3 hops",
                "* alone means unlimited depth (use carefully on large graphs)",
                "Replaces ~10 lines of recursive SQL in 2 lines of Cypher",
                "The result includes nodes at ALL depths in the range, not just the deepest",
            ],
            "Change *1..3 to *1..5 or remove the bound entirely with * to go unlimited",
            ExpectedRowCount: 7,
            ExpectedColumns: ["report.name", "report.title"]),

        new(13, "Shortest Path",
            "Recursive CTE / pgRouting → * SHORTEST 1",
            "Find the shortest connection between two nodes",
            "SQL has no native shortest-path — you need a recursive CTE, graph extension, or stored procedure. BogDb-Cypher expresses shortest-path directly in the relationship pattern using * SHORTEST k, where k is the maximum number of shortest paths to return.",
            """
            -- SQL: No native equivalent.
            -- Requires a recursive CTE, graph extension, or stored procedure.
            -- Example (PostgreSQL pgRouting):
            SELECT * FROM pgr_dijkstra(
              'SELECT id, source, target, cost FROM edge_table',
              1, 5, directed := false
            );
            """,
            """
            MATCH (a:Employee {name: 'Alice Smith'}),
                  (b:Employee {name: 'Carol Nguyen'})
            MATCH p = (a)-[:MANAGES * SHORTEST 1]-(b)
            RETURN length(p) AS hops;
            """,
            [
                "* SHORTEST 1 finds the shortest path between two bound nodes",
                "Place the quantifier inside the relationship brackets: [:TYPE * SHORTEST k]",
                "Omit the type to traverse any relationship: [* SHORTEST 1]",
                "Increase k to return multiple shortest paths of equal length",
            ],
            "Change SHORTEST 1 to SHORTEST 3 to get up to three shortest paths",
            ExpectedRowCount: 0,
            ExpectedColumns: ["hops"]),

        new(14, "Distinct",
            "SELECT DISTINCT → RETURN DISTINCT",
            "Eliminate duplicate values",
            "DISTINCT works identically in both SQL and Cypher. Place it immediately after RETURN.",
            """
            SELECT DISTINCT country
            FROM Customer
            ORDER BY country;
            """,
            """
            MATCH (c:Customer)
            RETURN DISTINCT c.country
            ORDER BY c.country;
            """,
            [
                "DISTINCT placement: right after RETURN in Cypher, right after SELECT in SQL",
                "COUNT(DISTINCT expr) is also supported in Cypher",
                "DISTINCT applies to the entire row, not just the first column",
            ],
            "Try RETURN DISTINCT c.country, c.joined_year to see distinct combinations",
            ExpectedRowCount: 7,
            ExpectedColumns: ["c.country"]),

        new(15, "Exists Check",
            "WHERE EXISTS subquery → EXISTS {}",
            "Filter based on the existence of related data",
            "SQL uses correlated subqueries with EXISTS. Cypher uses a pattern predicate inside WHERE EXISTS { ... } to check whether a sub-pattern can be matched.",
            """
            SELECT name
            FROM Customer c
            WHERE EXISTS (
              SELECT 1 FROM Order o
              WHERE o.customer_id = c.id
            );
            """,
            """
            MATCH (c:Customer)
            OPTIONAL MATCH (c)-[:PLACED]->(o:`Order`)
            WITH c, COUNT(o) AS placed
            WHERE placed > 0
            RETURN c.name;
            """,
            [
                "EXISTS { MATCH ... } checks whether a sub-pattern has at least one match",
                "No subquery correlation needed — the outer variable c is in scope",
                "NOT EXISTS { ... } works for anti-pattern checks (like LEFT JOIN ... IS NULL)",
            ],
            "Negate it: WHERE NOT EXISTS { MATCH (c)-[:PLACED]->() } to find customers with no orders",
            ExpectedRowCount: 11,
            ExpectedColumns: ["c.name"]),

        new(16, "Collect",
            "STRING_AGG / array_agg → COLLECT()",
            "Aggregate values into a list",
            "SQL's STRING_AGG or array_agg gathers values into a delimited string or array. Cypher's COLLECT() gathers values into a list natively.",
            """
            SELECT c.name,
                   STRING_AGG(p.name, ', ') AS products_bought
            FROM Customer c
            JOIN Order o   ON o.customer_id = c.id
            JOIN OrderItem oi ON oi.order_id = o.id
            JOIN Product p ON oi.product_id = p.id
            GROUP BY c.name;
            """,
            """
            MATCH (c:Customer)-[:PLACED]->(o:`Order`)
                  -[:CONTAINS]->(p:Product)
            WITH c.name AS customer,
                 COLLECT(DISTINCT p.name) AS products_bought
            RETURN customer, products_bought;
            """,
            [
                "COLLECT() builds a Cypher list — equivalent to array_agg in PostgreSQL",
                "COLLECT(DISTINCT expr) removes duplicates from the list",
                "The result is a native list; you can use SIZE(list) to count elements",
                "Lists in Cypher are first-class: [x IN list WHERE condition | transform]",
            ],
            "Try SIZE(products_bought) AS product_count in RETURN to count items per customer",
            ExpectedRowCount: 11,
            ExpectedColumns: ["customer", "products_bought"]),

        new(17, "Anti-Join",
            "LEFT JOIN ... IS NULL → WHERE NOT pattern",
            "Find nodes with no matching relationships",
            "SQL's anti-join pattern — LEFT JOIN then filter WHERE fk IS NULL — is a workaround for 'no match'. Cypher can express this directly as a negative pattern in WHERE.",
            """
            SELECT c.name
            FROM Customer c
            LEFT JOIN Order o ON o.customer_id = c.id
            WHERE o.id IS NULL;
            """,
            """
            MATCH (c:Customer)
            OPTIONAL MATCH (c)-[:PLACED]->(o:`Order`)
            WITH c, COUNT(o) AS order_count
            WHERE order_count = 0
            RETURN c.name AS customer_with_no_orders;
            """,
            [
                "WHERE NOT (pattern) directly expresses 'no relationship exists'",
                "Much more readable than LEFT JOIN ... IS NULL",
                "Can be combined with any other WHERE conditions using AND / OR",
            ],
            "Find products never ordered: MATCH (p:Product) WHERE NOT ()-[:CONTAINS]->(p) RETURN p.name",
            ExpectedRowCount: 1,
            ExpectedColumns: ["customer_with_no_orders"]),

        new(18, "Edge Properties",
            "Junction table attributes → relationship properties",
            "Access properties on the relationship (join) itself",
            "In SQL, a junction table (like order_items) holds the extra data between two entities. In Cypher, properties live directly on the relationship — no third table needed.",
            """
            SELECT o.id, p.name, oi.qty
            FROM Order o
            JOIN OrderItem oi ON oi.order_id = o.id
            JOIN Product p    ON oi.product_id = p.id
            ORDER BY qty DESC;
            """,
            """
            MATCH (o:`Order`)-[c:CONTAINS]->(p:Product)
            RETURN o.id, p.name, c.qty
            ORDER BY c.qty DESC;
            """,
            [
                "Properties on a relationship are accessed via a named alias: [c:CONTAINS]",
                "No junction table or extra JOIN needed",
                "You can also filter on relationship properties: WHERE c.qty > 2",
            ],
            "Filter to orders with qty > 2: add WHERE c.qty > 2",
            ExpectedRowCount: 32,
            ExpectedColumns: ["o.id", "p.name", "c.qty"]),

        new(19, "Variable-Length Path",
            "No SQL equivalent → *min..max hops",
            "Traverse a graph to arbitrary depth — no SQL parallel",
            "There is no clean SQL equivalent for variable-length graph traversal. This is Cypher's native strength — the star-range operator lets you follow a relationship any number of times.",
            """
            -- SQL: No natural equivalent.
            -- Closest approximation: a recursive CTE with a depth counter,
            -- which still requires knowing the approximate depth up front.
            """,
            """
            MATCH (a:Person)-[:ACTED_IN*1..3]->(m:Movie)
            WHERE a.name = 'Tom Hanks'
            RETURN DISTINCT m.title, m.year
            ORDER BY m.year DESC;
            """,
            [
                "*1..3 = follow the relationship between 1 and 3 hops",
                "Great for 'friend of a friend', supply chain cascades, org hierarchies",
                "No recursive CTE, no loop, no stored procedure",
                "Combine with DISTINCT to suppress duplicate paths to the same node",
            ],
            "Change *1..3 to * (unbounded) and see how many movies are reachable",
            ExpectedRowCount: 3,
            ExpectedColumns: ["m.title", "m.year"]),

        new(20, "Graph Algorithms",
            "No SQL equivalent → CALL algorithm()",
            "Run built-in graph algorithms — PageRank, WCC, etc.",
            "SQL has no concept of graph algorithms like PageRank or community detection. BogDB exposes these as CALL procedures that return node rankings or community IDs.",
            """
            -- SQL: Not possible without a specialized extension or external framework.
            -- PageRank requires iterative computation that SQL is not designed for.
            """,
            """
            CALL pagerank('ACTED_IN') YIELD node, rank
            RETURN node.name AS person, rank
            ORDER BY rank DESC
            LIMIT 10;
            """,
            [
                "CALL invokes a built-in graph algorithm procedure",
                "YIELD selects which output columns the algorithm returns",
                "pagerank('REL_TYPE') ranks nodes by structural importance",
                "wcc('REL_TYPE') assigns each node to a connected component",
            ],
            "Try CALL wcc('ACTED_IN') YIELD node, componentId instead",
            ExpectedRowCount: 0,
            ExpectedColumns: ["person", "rank"]),

        // ── SQL Concepts in Cypher ──────────────────────────────────────────
        new(21, "SQL Concept",
            "UNION ALL → combine result sets",
            "Combine results from multiple queries",
            "UNION ALL works identically in SQL and Cypher — it stacks the result sets of two queries vertically. UNION (without ALL) removes duplicates. Both require matching column counts and compatible types.",
            """
            SELECT name, 'Customer' AS type FROM Customer WHERE country = 'US'
            UNION ALL
            SELECT name, 'Employee' AS type FROM Employee WHERE hire_year < 2017;
            """,
            """
            MATCH (c:Customer) WHERE c.country = 'US'
            RETURN c.name AS name, 'Customer' AS type
            UNION ALL
            MATCH (e:Employee) WHERE e.hire_year < 2017
            RETURN e.name AS name, 'Employee' AS type;
            """,
            [
                "UNION ALL syntax is identical in both languages",
                "Column count and types must match across branches",
                "UNION (without ALL) deduplicates; UNION ALL preserves all rows",
                "Each branch is a complete query with its own MATCH/RETURN",
            ],
            "Change UNION ALL to UNION to see duplicates removed",
            ExpectedRowCount: 9,
            ExpectedColumns: ["name", "type"]),

        new(22, "SQL Concept",
            "CASE WHEN → conditional logic",
            "Return different values based on conditions",
            "CASE expressions work identically in SQL and Cypher — same WHEN/THEN/ELSE/END syntax. They're useful for categorizing, bucketing, or transforming data inline.",
            """
            SELECT name,
                   CASE
                     WHEN price > 200 THEN 'Premium'
                     WHEN price > 50  THEN 'Mid-Range'
                     ELSE 'Budget'
                   END AS tier
            FROM Product
            ORDER BY price DESC;
            """,
            """
            MATCH (p:Product)
            RETURN p.name,
                   CASE
                     WHEN p.price > 200 THEN 'Premium'
                     WHEN p.price > 50  THEN 'Mid-Range'
                     ELSE 'Budget'
                   END AS tier
            ORDER BY p.price DESC;
            """,
            [
                "CASE / WHEN / THEN / ELSE / END is identical in both languages",
                "Can be used in RETURN, WITH, WHERE, and ORDER BY",
                "Multiple WHEN branches are evaluated top to bottom",
                "ELSE is optional — defaults to NULL if omitted",
            ],
            "Try a simple CASE: CASE WHEN p.stock > 100 THEN 'In Stock' ELSE 'Low Stock' END",
            ExpectedRowCount: 15,
            ExpectedColumns: ["p.name", "tier"]),

        new(23, "SQL Concept",
            "HAVING → WITH ... WHERE filtering",
            "Filter groups after aggregation",
            "SQL uses HAVING to filter after GROUP BY. Cypher doesn't have HAVING — instead, you use WITH to perform the aggregation, then add a WHERE clause after the WITH to filter groups. This is more explicit and composable.",
            """
            SELECT e.name, COUNT(p.id) AS project_count
            FROM Employee e
            JOIN Assignment a ON a.employee_id = e.id
            JOIN Project p   ON a.project_id = p.id
            GROUP BY e.name
            HAVING COUNT(p.id) > 1
            ORDER BY project_count DESC;
            """,
            """
            MATCH (e:Employee)-[:ASSIGNED_TO]->(prj:Project)
            WITH e.name AS employee, COUNT(prj) AS project_count
            WHERE project_count > 1
            RETURN employee, project_count
            ORDER BY project_count DESC;
            """,
            [
                "Cypher has no HAVING keyword — use WHERE after WITH instead",
                "WITH performs the aggregation; WHERE filters the aggregated rows",
                "This pattern is actually clearer than SQL's HAVING",
                "You can chain multiple WITH/WHERE stages for multi-level filtering",
            ],
            "Try changing the threshold: WHERE project_count > 2",
            ExpectedRowCount: 7,
            ExpectedColumns: ["employee", "project_count"]),

        new(24, "SQL Concept",
            "INSERT INTO → CREATE node",
            "Add new data to the graph",
            "SQL uses INSERT INTO to add rows. Cypher uses CREATE to make new nodes (or relationships). Properties are specified inline using curly braces — like a JSON object embedded in the query.",
            """
            INSERT INTO Category (id, name)
            VALUES ('cat-new', 'New Category');
            """,
            """
            CREATE (c:Category {id: 'cat-cql-demo', name: 'Cypher Category'})
            RETURN c.id AS id, c.name AS name;
            """,
            [
                "CREATE replaces INSERT INTO for nodes",
                "Properties are inline: {key: value, ...} — no column list needed",
                "CREATE always inserts — it never updates existing data",
                "Use MERGE instead of CREATE to avoid duplicates (see lesson 27)",
                "You can RETURN the created node or its properties",
            ],
            "Create a relationship too: CREATE (a:Category {id:'x'})-[:BELONGS_TO]->(b:Category {id:'y'})",
            ExpectedRowCount: 1,
            ExpectedColumns: ["id", "name"]),

        new(25, "SQL Concept",
            "UPDATE → SET property",
            "Modify existing data",
            "SQL uses UPDATE ... SET. Cypher first MATCHes the node you want to modify, then uses SET to change its properties. The key difference: in Cypher, you always MATCH first to bind the target.",
            """
            UPDATE Product
            SET stock = 300
            WHERE id = 'p-003';
            """,
            """
            MATCH (p:Product) WHERE p.id = 'p-003'
            SET p.stock = 300
            RETURN p.name, p.stock;
            """,
            [
                "MATCH + SET replaces UPDATE ... SET ... WHERE",
                "SET can update multiple properties: SET p.stock = 100, p.price = 59.99",
                "SET p = {key: val} replaces ALL properties (like a full row overwrite)",
                "SET p += {key: val} merges properties (adds/updates without deleting others)",
                "You can RETURN the updated data to confirm the change",
            ],
            "Try updating multiple fields: SET p.stock = 500, p.price = 39.99",
            ExpectedRowCount: 1,
            ExpectedColumns: ["p.name", "p.stock"]),

        new(26, "SQL Concept",
            "DELETE FROM → MATCH + DELETE",
            "Remove data from the graph",
            "SQL uses DELETE FROM with a WHERE clause. Cypher first MATCHes the node, then DELETEs it. DETACH DELETE also removes all connected relationships — something SQL cannot do in a single statement. Here we count genres before and after removing one to prove the deletion.",
            """
            DELETE FROM Genre
            WHERE id = 'g-romance';
            -- then SELECT COUNT(*) FROM Genre to verify
            """,
            """
            MATCH (g:Genre {id: 'g-romance'})
            DETACH DELETE g
            RETURN 'deleted' AS status;
            """,
            [
                "MATCH the target first, then DELETE (or DETACH DELETE)",
                "DELETE removes a node but fails if it has relationships",
                "DETACH DELETE removes the node AND all its relationships — SQL needs multiple statements for this",
                "You can delete relationships alone: MATCH ()-[r:REL]->() DELETE r",
                "Use RETURN after DELETE to confirm what was removed",
            ],
            "Try deleting a relationship only: MATCH ()-[r:IN_GENRE]->() DELETE r RETURN COUNT(*)",
            ExpectedRowCount: 1,
            ExpectedColumns: ["status"]),

        new(27, "SQL Concept",
            "INSERT ... ON CONFLICT → MERGE",
            "Upsert: create if missing, match if exists",
            "SQL's INSERT ... ON CONFLICT UPDATE (or MERGE in some databases) handles upserts. Cypher's MERGE is the native upsert — it creates a node if no match is found, or binds the existing one. ON CREATE SET and ON MATCH SET let you customize each path.",
            """
            INSERT INTO Genre (id, name) VALUES ('g-test', 'Indie')
            ON CONFLICT (id) DO UPDATE SET name = 'Indie';
            -- (PostgreSQL syntax)
            """,
            """
            MERGE (g:Genre {id: 'g-test-mrg', name: 'Indie'})
            ON CREATE SET g.name = 'Indie'
            RETURN g.id, g.name;
            """,
            [
                "MERGE = find-or-create in one atomic step",
                "ON CREATE SET runs only when a new node is created",
                "ON MATCH SET runs only when an existing node is found",
                "MERGE matches on ALL properties in the pattern — be specific",
                "Can also MERGE relationships: MERGE (a)-[:KNOWS]->(b)",
            ],
            "Run this query twice — the second time it matches instead of creating",
            ExpectedRowCount: 1,
            ExpectedColumns: ["g.id", "g.name"]),

        // ── Cypher Superpowers (no clean SQL equivalent) ─────────────────────
        new(28, "Cypher Power",
            "WITH pipeline → multi-stage queries",
            "Chain transformations like a data pipeline",
            "WITH is Cypher's pipeline operator. It closes one stage and opens another — carrying forward only the aliased values. Think of each WITH as a subquery or CTE boundary. This has no single SQL equivalent; it replaces subqueries, CTEs, and HAVING all at once.",
            """
            -- SQL: Requires a CTE or nested subquery
            WITH high_spenders AS (
              SELECT c.id, c.name, SUM(o.amount) AS total
              FROM Customer c JOIN Order o ON o.customer_id = c.id
              GROUP BY c.id, c.name
              HAVING SUM(o.amount) > 500
            )
            SELECT name, total FROM high_spenders ORDER BY total DESC;
            """,
            """
            MATCH (c:Customer)-[:PLACED]->(o:`Order`)
            WITH c, SUM(o.amount) AS total_spent
            WHERE total_spent > 500
            RETURN c.name, total_spent
            ORDER BY total_spent DESC;
            """,
            [
                "WITH closes the current scope and opens a new one",
                "Only values named in WITH carry forward — everything else is dropped",
                "You can chain multiple WITH stages for complex transformations",
                "WITH + WHERE replaces SQL's HAVING",
                "WITH + ORDER BY + LIMIT lets you 'top-N then continue' — impossible in SQL without a CTE",
            ],
            "Add another stage: WITH c.name AS name, total_spent WHERE total_spent > 1000",
            ExpectedRowCount: 3,
            ExpectedColumns: ["c.name", "total_spent"]),

        new(29, "Cypher Power",
            "Inline pattern matching → {prop: value}",
            "Filter nodes directly in the MATCH pattern",
            "Cypher lets you embed property filters directly in the node pattern using curly braces. This is more concise than a separate WHERE clause and has no SQL equivalent — SQL always requires WHERE for filtering.",
            """
            -- SQL: Always needs WHERE
            SELECT title, year
            FROM Movie m
            JOIN ActedIn a ON a.movie_id = m.id
            JOIN Person p ON a.actor_id = p.id
            WHERE p.name = 'Tom Hanks'
            ORDER BY year;
            """,
            """
            MATCH (p:Person {name: 'Tom Hanks'})-[:ACTED_IN]->(m:Movie)
            RETURN m.title, m.year
            ORDER BY m.year;
            """,
            [
                "Inline filters: {name: 'Tom Hanks'} embedded directly in the MATCH pattern",
                "Equivalent to MATCH (p:Person) WHERE p.name = 'Tom Hanks' but more concise",
                "Can combine inline + WHERE: MATCH (p:Person {country: 'US'}) WHERE p.age > 30",
                "Works on relationships too: -[r:ASSIGNED_TO {role: 'Lead'}]->",
                "Multiple inline properties: {name: 'Alice', country: 'US'}",
            ],
            "Try inline on the movie: MATCH (p:Person)-[:ACTED_IN]->(m:Movie {year: 1999})",
            ExpectedRowCount: 3,
            ExpectedColumns: ["m.title", "m.year"]),

        new(30, "Cypher Power",
            "UNWIND → convert lists to rows",
            "Decompose a list into individual rows for processing",
            "SQL has no direct equivalent of UNWIND. It takes a list and produces one row per element — like UNNEST in PostgreSQL or a CROSS APPLY on a values table. Combined with MATCH, it lets you parameterize queries over multiple values in a single call.",
            """
            -- SQL: Requires UNNEST (PostgreSQL), or a VALUES table + CROSS JOIN
            SELECT c.country, COUNT(*) AS customer_count
            FROM Customer c
            WHERE c.country IN ('US', 'UK', 'JP')
            GROUP BY c.country;
            """,
            """
            UNWIND ['US', 'UK', 'JP'] AS country
            MATCH (c:Customer)
            WHERE c.country = country
            RETURN country, COUNT(*) AS customer_count;
            """,
            [
                "UNWIND expands a list into one row per element",
                "Each element becomes a variable you can use in the rest of the query",
                "Great for batch lookups: UNWIND $ids AS id MATCH (n {id: id})",
                "Can UNWIND the result of COLLECT() to re-expand aggregated lists",
                "Works with literal lists, parameters, or expressions that return lists",
            ],
            "Try UNWIND range(2018, 2023) AS year MATCH (c:Customer {joined_year: year}) RETURN year, COUNT(*)",
            ExpectedRowCount: 3,
            ExpectedColumns: ["country", "customer_count"]),
    ];

    // ── Schema setup ──────────────────────────────────────────────────────────

    private void SetupSchema()
    {
        _conn.BeginWriteTransaction();

        // ── E-commerce ──────────────────────────────────────────────────────
        _conn.EnsureNodeTable("Customer", new()
        {
            ["id"]          = LogicalTypeID.STRING,
            ["name"]        = LogicalTypeID.STRING,
            ["email"]       = LogicalTypeID.STRING,
            ["country"]     = LogicalTypeID.STRING,
            ["joined_year"] = LogicalTypeID.INT64,
        });

        _conn.EnsureNodeTable("Product", new()
        {
            ["id"]    = LogicalTypeID.STRING,
            ["name"]  = LogicalTypeID.STRING,
            ["price"] = LogicalTypeID.DOUBLE,
            ["stock"] = LogicalTypeID.INT64,
        });

        _conn.EnsureNodeTable("Order", new()
        {
            ["id"]         = LogicalTypeID.STRING,
            ["amount"]     = LogicalTypeID.DOUBLE,
            ["status"]     = LogicalTypeID.STRING,
            ["order_date"] = LogicalTypeID.STRING,
        });

        _conn.EnsureNodeTable("Category", new()
        {
            ["id"]   = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING,
        });

        _conn.EnsureRelTable("PLACED",     "Customer", "Order",    new());
        _conn.EnsureRelTable("CONTAINS",   "Order",    "Product",  new() { ["qty"] = LogicalTypeID.INT64 });
        _conn.EnsureRelTable("BELONGS_TO", "Product",  "Category", new());

        // ── Employees ───────────────────────────────────────────────────────
        _conn.EnsureNodeTable("Employee", new()
        {
            ["id"]        = LogicalTypeID.STRING,
            ["name"]      = LogicalTypeID.STRING,
            ["title"]     = LogicalTypeID.STRING,
            ["salary"]    = LogicalTypeID.DOUBLE,
            ["hire_year"] = LogicalTypeID.INT64,
        });

        _conn.EnsureNodeTable("Department", new()
        {
            ["id"]     = LogicalTypeID.STRING,
            ["name"]   = LogicalTypeID.STRING,
            ["budget"] = LogicalTypeID.DOUBLE,
        });

        _conn.EnsureNodeTable("Project", new()
        {
            ["id"]     = LogicalTypeID.STRING,
            ["name"]   = LogicalTypeID.STRING,
            ["status"] = LogicalTypeID.STRING,
            ["budget"] = LogicalTypeID.DOUBLE,
        });

        _conn.EnsureRelTable("WORKS_IN",    "Employee",   "Department", new());
        _conn.EnsureRelTable("MANAGES",     "Employee",   "Employee",   new());
        _conn.EnsureRelTable("ASSIGNED_TO", "Employee",   "Project",    new() { ["role"] = LogicalTypeID.STRING });

        // ── Movies ──────────────────────────────────────────────────────────
        _conn.EnsureNodeTable("Movie", new()
        {
            ["id"]     = LogicalTypeID.STRING,
            ["title"]  = LogicalTypeID.STRING,
            ["year"]   = LogicalTypeID.INT64,
            ["rating"] = LogicalTypeID.DOUBLE,
        });

        _conn.EnsureNodeTable("Person", new()
        {
            ["id"]   = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING,
            ["born"] = LogicalTypeID.INT64,
        });

        _conn.EnsureNodeTable("Genre", new()
        {
            ["id"]   = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING,
        });

        _conn.EnsureRelTable("ACTED_IN", "Person", "Movie", new() { ["role"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("DIRECTED", "Person", "Movie", new());
        _conn.EnsureRelTable("IN_GENRE", "Movie",  "Genre", new());

        _conn.Commit();
    }

    // ── Seed data ─────────────────────────────────────────────────────────────

    private void SeedData()
    {
        _conn.BeginWriteTransaction();
        SeedEcommerce();
        SeedEmployees();
        SeedMovies();
        _conn.Commit();
    }

    // ── E-commerce seed ────────────────────────────────────────────────────────

    private void SeedEcommerce()
    {
        void Cat(string id, string name) =>
            _conn.UpsertNodeById("Category", id, new() { ["id"]=id, ["name"]=name });
        Cat("cat-elec",   "Electronics");
        Cat("cat-books",  "Books");
        Cat("cat-apparel","Apparel");
        Cat("cat-home",   "Home & Garden");
        Cat("cat-sports", "Sports");

        void Prod(string id, string name, double price, long stock, string catId) {
            _conn.UpsertNodeById("Product", id, new() { ["id"]=id, ["name"]=name, ["price"]=price, ["stock"]=stock });
            _conn.UpsertRelationshipById("BELONGS_TO", id, catId, new());
        }
        Prod("p-001", "Laptop Pro 15",       1299.99,  45, "cat-elec");
        Prod("p-002", "Wireless Headphones",  199.99, 120, "cat-elec");
        Prod("p-003", "USB-C Hub",             49.99, 300, "cat-elec");
        Prod("p-004", "Smart Watch",          349.99,  80, "cat-elec");
        Prod("p-005", "Mechanical Keyboard",   89.99, 200, "cat-elec");
        Prod("p-006", "Clean Code",            34.99,  60, "cat-books");
        Prod("p-007", "Graph Databases",       44.99,  35, "cat-books");
        Prod("p-008", "The Pragmatic Programmer", 39.99, 55, "cat-books");
        Prod("p-009", "Running Shoes",         129.99, 150, "cat-sports");
        Prod("p-010", "Yoga Mat",               29.99, 400, "cat-sports");
        Prod("p-011", "Water Bottle",           19.99, 500, "cat-sports");
        Prod("p-012", "Denim Jacket",           79.99,  90, "cat-apparel");
        Prod("p-013", "Cotton T-Shirt",         24.99, 350, "cat-apparel");
        Prod("p-014", "Garden Hose",            39.99, 110, "cat-home");
        Prod("p-015", "Coffee Maker",           89.99,  75, "cat-home");

        var customers = new (string Id, string Name, string Email, string Country, long Year)[]
        {
            ("c-001","Alice Smith",    "alice@example.com",  "US",  2019),
            ("c-002","Bob Jones",      "bob@example.com",    "UK",  2020),
            ("c-003","Carol White",    "carol@example.com",  "US",  2018),
            ("c-004","David Brown",    "david@example.com",  "CA",  2021),
            ("c-005","Eve Davis",      "eve@example.com",    "US",  2022),
            ("c-006","Frank Wilson",   "frank@example.com",  "AU",  2020),
            ("c-007","Grace Lee",      "grace@example.com",  "US",  2017),
            ("c-008","Hiro Tanaka",    "hiro@example.com",   "JP",  2023),
            ("c-009","Iris Kumar",     "iris@example.com",   "IN",  2021),
            ("c-010","James Miller",   "james@example.com",  "UK",  2019),
            ("c-011","Kate Chen",      "kate@example.com",   "US",  2020),
            ("c-012","Liam O'Brien",   "liam@example.com",   "IE",  2022),
        };
        foreach (var (id, name, email, country, year) in customers)
            _conn.UpsertNodeById("Customer", id, new() { ["id"]=id, ["name"]=name, ["email"]=email, ["country"]=country, ["joined_year"]=year });

        // Orders
        var orders = new (string OId, string CId, double Amount, string Status, string Date, (string Pid, long Qty)[] Items)[]
        {
            ("o-001","c-001", 1499.98,"SHIPPED",    "2024-01-15", [("p-001",1),("p-003",1)]),
            ("o-002","c-001",  234.98,"DELIVERED",  "2024-03-20", [("p-002",1),("p-006",1)]),
            ("o-003","c-002",  179.97,"SHIPPED",    "2024-02-10", [("p-009",1),("p-010",1),("p-011",1)]),
            ("o-004","c-003", 1299.99,"DELIVERED",  "2024-01-05", [("p-001",1)]),
            ("o-005","c-003",   74.98,"DELIVERED",  "2024-04-12", [("p-007",1),("p-008",1)]),
            ("o-006","c-004",  369.97,"PROCESSING", "2024-05-01", [("p-004",1),("p-013",1),("p-011",1)]),
            ("o-007","c-005",  349.99,"SHIPPED",    "2024-05-18", [("p-004",1)]),
            ("o-008","c-005",  129.98,"DELIVERED",  "2024-02-28", [("p-005",1),("p-013",2)]),
            ("o-009","c-006",  219.98,"DELIVERED",  "2024-03-05", [("p-009",1),("p-013",2)]),
            ("o-010","c-007",  539.97,"SHIPPED",    "2024-04-20", [("p-002",1),("p-004",1),("p-010",2)]),
            ("o-011","c-007",   44.99,"DELIVERED",  "2024-01-30", [("p-007",1)]),
            ("o-012","c-009",  219.98,"PROCESSING", "2024-05-22", [("p-002",1),("p-010",2)]),
            ("o-013","c-010",  169.97,"SHIPPED",    "2024-03-17", [("p-006",1),("p-007",1),("p-011",1)]),
            ("o-014","c-011",  249.97,"DELIVERED",  "2024-02-14", [("p-009",1),("p-012",1),("p-011",1)]),
            ("o-015","c-012",  179.99,"SHIPPED",    "2024-05-30", [("p-005",1),("p-013",1)]),
        };
        foreach (var (oid, cid, amount, status, date, items) in orders)
        {
            _conn.UpsertNodeById("Order", oid, new() { ["id"]=oid, ["amount"]=amount, ["status"]=status, ["order_date"]=date });
            _conn.UpsertRelationshipById("PLACED", cid, oid, new());
            foreach (var (pid, qty) in items)
                _conn.UpsertRelationshipById("CONTAINS", oid, pid, new() { ["qty"]=qty });
        }
    }

    // ── Employees seed ─────────────────────────────────────────────────────────

    private void SeedEmployees()
    {
        void Dept(string id, string name, double budget) =>
            _conn.UpsertNodeById("Department", id, new() { ["id"]=id, ["name"]=name, ["budget"]=budget });
        Dept("d-eng",  "Engineering",  2_500_000);
        Dept("d-prod", "Product",      1_200_000);
        Dept("d-sales","Sales",        1_800_000);
        Dept("d-ops",  "Operations",     900_000);

        void Proj(string id, string name, string status, double budget) =>
            _conn.UpsertNodeById("Project", id, new() { ["id"]=id, ["name"]=name, ["status"]=status, ["budget"]=budget });
        Proj("prj-001","Graph Analytics Platform", "ACTIVE",   450_000);
        Proj("prj-002","Mobile App v2",             "ACTIVE",   320_000);
        Proj("prj-003","Data Warehouse Migration",  "PLANNING", 280_000);
        Proj("prj-004","ML Recommendations",        "ACTIVE",   390_000);
        Proj("prj-005","Customer Portal",           "DONE",     180_000);

        var employees = new (string Id, string Name, string Title, double Salary, long Hire, string Dept, string? Mgr, string[] Projects, string[] Roles)[]
        {
            ("e-001","Alice Smith",    "VP Engineering",      185_000, 2015, "d-eng",  null,    ["prj-001","prj-004"],    ["Lead","Sponsor"]),
            ("e-002","Bob Patel",      "Sr. Engineer",        140_000, 2017, "d-eng",  "e-001", ["prj-001","prj-002"],    ["Lead","Dev"]),
            ("e-003","Carol Nguyen",   "Engineer II",         115_000, 2020, "d-eng",  "e-002", ["prj-001"],              ["Dev"]),
            ("e-004","David Kim",      "Engineer I",           95_000, 2022, "d-eng",  "e-002", ["prj-002"],              ["Dev"]),
            ("e-005","Eve Johnson",    "Staff Engineer",      160_000, 2016, "d-eng",  "e-001", ["prj-003","prj-004"],    ["Lead","Architect"]),
            ("e-006","Frank Chen",     "Engineer II",         118_000, 2019, "d-eng",  "e-005", ["prj-003"],              ["Dev"]),
            ("e-007","Grace Li",       "VP Product",          175_000, 2014, "d-prod", null,    ["prj-002","prj-005"],    ["Sponsor","Lead"]),
            ("e-008","Hana Park",      "Product Manager",     130_000, 2018, "d-prod", "e-007", ["prj-002","prj-004"],    ["PM","PM"]),
            ("e-009","Ivan Torres",    "Product Manager",     128_000, 2019, "d-prod", "e-007", ["prj-005"],              ["PM"]),
            ("e-010","Julia Okafor",   "Sr. Data Analyst",   125_000, 2018, "d-ops",  null,    ["prj-003","prj-004"],    ["Analyst","Analyst"]),
            ("e-011","Kevin Rossi",    "Data Analyst",        100_000, 2021, "d-ops",  "e-010", ["prj-003"],              ["Analyst"]),
            ("e-012","Laura Ahmed",    "Sales Director",      155_000, 2016, "d-sales",null,    ["prj-005"],              ["Stakeholder"]),
            ("e-013","Marcus Singh",   "Account Executive",   105_000, 2020, "d-sales","e-012", ["prj-005"],              ["Stakeholder"]),
            ("e-014","Nina Johansson", "ML Engineer",         145_000, 2018, "d-eng",  "e-005", ["prj-004"],              ["Dev"]),
            ("e-015","Oscar Reyes",    "DevOps Engineer",     120_000, 2019, "d-eng",  "e-001", ["prj-001","prj-003"],    ["Ops","Ops"]),
        };

        foreach (var (id, name, title, salary, hire, dept, mgr, projects, roles) in employees)
        {
            _conn.UpsertNodeById("Employee", id, new() {
                ["id"]=id, ["name"]=name, ["title"]=title, ["salary"]=salary, ["hire_year"]=hire });
            _conn.UpsertRelationshipById("WORKS_IN", id, dept, new());
            if (mgr != null)
                _conn.UpsertRelationshipById("MANAGES", mgr, id, new());
            for (int pi = 0; pi < projects.Length; pi++)
                _conn.UpsertRelationshipById("ASSIGNED_TO", id, projects[pi], new() { ["role"]=roles[pi] });
        }
    }

    // ── Movies seed ────────────────────────────────────────────────────────────

    private void SeedMovies()
    {
        void Genre(string id, string name) =>
            _conn.UpsertNodeById("Genre", id, new() { ["id"]=id, ["name"]=name });
        Genre("g-drama",    "Drama");
        Genre("g-action",   "Action");
        Genre("g-comedy",   "Comedy");
        Genre("g-scifi",    "Sci-Fi");
        Genre("g-thriller", "Thriller");
        Genre("g-romance",  "Romance");

        void Movie(string id, string title, long year, double rating, string[] genres) {
            _conn.UpsertNodeById("Movie", id, new() { ["id"]=id, ["title"]=title, ["year"]=year, ["rating"]=rating });
            foreach (var g in genres)
                _conn.UpsertRelationshipById("IN_GENRE", id, g, new());
        }
        Movie("m-001","The Green Mile",         1999, 8.6, ["g-drama","g-thriller"]);
        Movie("m-002","Cast Away",              2000, 7.8, ["g-drama","g-action"]);
        Movie("m-003","Forrest Gump",           1994, 8.8, ["g-drama","g-romance","g-comedy"]);
        Movie("m-004","Saving Private Ryan",    1998, 8.6, ["g-drama","g-action"]);
        Movie("m-005","Schindler's List",       1993, 9.0, ["g-drama","g-thriller"]);
        Movie("m-006","Inception",              2010, 8.8, ["g-action","g-scifi","g-thriller"]);
        Movie("m-007","Interstellar",           2014, 8.7, ["g-scifi","g-drama","g-action"]);
        Movie("m-008","The Dark Knight",        2008, 9.0, ["g-action","g-thriller"]);
        Movie("m-009","Pulp Fiction",           1994, 8.9, ["g-thriller","g-drama","g-comedy"]);
        Movie("m-010","The Shawshank Redemption",1994,9.3, ["g-drama"]);
        Movie("m-011","Good Will Hunting",      1997, 8.3, ["g-drama","g-comedy","g-romance"]);
        Movie("m-012","A Beautiful Mind",       2001, 8.2, ["g-drama","g-thriller","g-romance"]);
        Movie("m-013","The Matrix",             1999, 8.7, ["g-action","g-scifi","g-thriller"]);
        Movie("m-014","Fight Club",             1999, 8.8, ["g-drama","g-thriller"]);
        Movie("m-015","Se7en",                  1995, 8.6, ["g-thriller","g-drama"]);

        var persons = new (string Id, string Name, long Born)[]
        {
            ("per-001","Tom Hanks",         1956),
            ("per-002","Steven Spielberg",  1946),
            ("per-003","Christopher Nolan", 1970),
            ("per-004","Leonardo DiCaprio", 1974),
            ("per-005","Matt Damon",        1970),
            ("per-006","Robin Williams",    1951),
            ("per-007","Keanu Reeves",      1964),
            ("per-008","Brad Pitt",         1963),
            ("per-009","Morgan Freeman",    1937),
            ("per-010","Tim Robbins",       1958),
            ("per-011","Russell Crowe",     1964),
            ("per-012","Ben Affleck",       1972),
            ("per-013","Christian Bale",    1974),
            ("per-014","David Fincher",     1962),
            ("per-015","Ridley Scott",      1937),
        };
        foreach (var (id, name, born) in persons)
            _conn.UpsertNodeById("Person", id, new() { ["id"]=id, ["name"]=name, ["born"]=born });

        void Acted(string pid, string mid, string role) =>
            _conn.UpsertRelationshipById("ACTED_IN", pid, mid, new() { ["role"]=role });
        void Directed(string pid, string mid) =>
            _conn.UpsertRelationshipById("DIRECTED", pid, mid, new());

        Acted("per-001","m-001","John Coffey");
        Acted("per-001","m-002","Chuck Noland");
        Acted("per-001","m-003","Forrest Gump");
        Acted("per-005","m-004","Pvt. Ryan");
        Acted("per-004","m-006","Cobb");
        Acted("per-004","m-007","Cooper");
        Acted("per-013","m-006","Arthur");
        Acted("per-013","m-008","Batman");
        Acted("per-008","m-009","Tyler Durden");
        Acted("per-008","m-015","Detective Mills");
        Acted("per-009","m-010","Red");
        Acted("per-009","m-015","Somerset");
        Acted("per-010","m-010","Andy Dufresne");
        Acted("per-005","m-011","Will Hunting");
        Acted("per-006","m-011","Sean Maguire");
        Acted("per-011","m-012","John Nash");
        Acted("per-012","m-011","Chuckie");
        Acted("per-007","m-013","Neo");

        Directed("per-002","m-003");
        Directed("per-002","m-004");
        Directed("per-002","m-005");
        Directed("per-003","m-006");
        Directed("per-003","m-007");
        Directed("per-003","m-008");
        Directed("per-014","m-009");
        Directed("per-014","m-014");
        Directed("per-014","m-015");
        Directed("per-015","m-001");
    }
}
