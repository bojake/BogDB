# Scalar Function Parity Matrix
# Auto-generated from gap_analysis.py against C++ `bogdb-master` headers

> **Last run:** 2026-04-04  
> **Overall:** 226/226 C++ scalar functions covered (100%)  
> **Registry-matched:** 215/226 (95.1%)  
> **Non-registry paths:** +11 (aggregates, quantifiers, lambda evaluator, internal_id)

## Summary

| Category | Count | Status |
|---|---|---|
| C++ scalar functions | 226 | baseline |
| C# registered function names | 428 | includes aliases & extensions |
| Matched in function registry | 215 | 95.1% |
| Handled via non-registry paths | +11 | aggregates, quantifiers, lambda, internal_id |
| **Effective parity** | **226/226** | **100%** ✅ |

## 11 Functions Handled Via Non-Registry Paths

### Quantifier Predicates (parser path)

| Function | Where handled |
|---|---|
| `all` | `oC_Quantifier` parser → `BoundQuantifierExpression` → `EvaluateQuantifier` |
| `any` | `oC_Quantifier` parser → `BoundQuantifierExpression` → `EvaluateQuantifier` |

### Lambda Evaluator

| Function | Where handled |
|---|---|
| `list_filter` | Lambda evaluator in `ExpressionExecutionHelper.EvalListFilter` |
| `list_reduce` | Lambda evaluator in `ExpressionExecutionHelper.EvalListReduce` |

### Internal ID Functions

| Function | Where handled |
|---|---|
| `offset` | `InternalIdFunctions.cs` (registered via FunctionDispatcher) |

### Aggregates (PhysicalAggregate)

| Function | Status |
|---|---|
| `avg` | ✅ `PhysicalAggregate` |
| `sum` | ✅ `PhysicalAggregate` |
| `min` | ✅ `PhysicalAggregate` |
| `max` | ✅ `PhysicalAggregate` |
| `count_if` | ✅ `PhysicalAggregate` — conditional counting |
| `collect` | ✅ `PhysicalAggregate` — list-accumulation |

## Exotic Type Cast Additions

| C++ Function | C# Implementation | Mapped Type |
|---|---|---|
| `to_int128` | `CastFunctions.cs` | `decimal` |
| `to_uint128` | `CastFunctions.cs` | `decimal` (rejects negative) |
| `to_serial` | `CastFunctions.cs` | `long` (INT64) |
| `to_uuid` / `uuid` | `CastFunctions.cs` | `string` (Guid-validated) |
| `blob` / `to_blob` | `CastFunctions.cs` | `byte[]` (hex/UTF-8) |

## Known Limitations

- **INT128/UINT128**: Mapped to `decimal` but `TypeCoercionHelper.Normalize` may convert to `double` in expression pipelines, losing the type distinction. `typeof()` may return `DOUBLE` instead of `INT128`.
- **BLOB**: Stored as `byte[]`; downstream string serialization may not round-trip perfectly for all binary payloads.
- **UUID**: Stored as validated `string` — functionally equivalent to C++ UUID but no distinct `UUID` LogicalTypeID.

## Test Evidence

| Test File | Tests | Status |
|---|---|---|
| `ScalarFunctionGapTests.cs` | 32 | ✅ |
| `ListPredicateTests.cs` | 16 | ✅ |
| `AggregateAndCastGapTests.cs` | 19 | ✅ |
| Golden corpus (52 corpora) | 52 | ✅ |
| **Total** | **119** | **All passing** |
