-- corpus-types-extended.cypher
-- Extended typed-schema coverage: unsigned integers plus DATE/TIMESTAMP columns.

-- SCHEMA
NODE Event  id:INT64  u32:UINT32  u64:UINT64  created:DATE  observed_at:TIMESTAMP  active:BOOL  label:STRING

-- SETUP
CREATE (:Event {id:1, active:true,  label:'alpha'});
CREATE (:Event {id:2, active:false, label:'beta'});
CREATE (:Event {id:3, active:true,  label:'gamma'});
MATCH (e:Event {id:1}) SET e.u32 = to_uint32(7),  e.u64 = to_uint64(1000), e.created = date('2024-01-15'), e.observed_at = timestamp('2024-01-15T08:00:00Z');
MATCH (e:Event {id:2}) SET e.u32 = to_uint32(42), e.u64 = to_uint64(2500), e.created = date('2024-02-20'), e.observed_at = timestamp('2024-02-20T09:30:00Z');
MATCH (e:Event {id:3}) SET e.u32 = to_uint32(5),  e.u64 = to_uint64(9999), e.created = date('2024-03-10'), e.observed_at = timestamp('2024-03-10T12:45:00Z');

-- QUERY: scan_all_rows
MATCH (e:Event) RETURN e.id, e.u32, e.u64, e.created, e.observed_at, e.active, e.label ORDER BY e.id;

-- QUERY: unsigned_filter
MATCH (e:Event) WHERE e.u32 > 6 RETURN e.id, e.u32 ORDER BY e.id;

-- QUERY: unsigned_order_desc
MATCH (e:Event) RETURN e.label, e.u64 ORDER BY e.u64 DESC;

-- QUERY: date_order
MATCH (e:Event) RETURN e.id, e.created ORDER BY e.created;

-- QUERY: timestamp_order
MATCH (e:Event) RETURN e.id, e.observed_at ORDER BY e.observed_at DESC;

-- QUERY: date_extract_month
MATCH (e:Event) RETURN e.id, date_part('month', e.created) AS month ORDER BY e.id;

-- QUERY: timestamp_to_epoch_ms
MATCH (e:Event) RETURN e.id, to_epoch_ms(e.observed_at) AS ms ORDER BY e.id;

-- QUERY: active_recent
MATCH (e:Event) WHERE e.active = true AND e.created >= date('2024-01-31') RETURN e.id, e.label ORDER BY e.id;
