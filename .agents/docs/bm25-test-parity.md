# BM25 unit-test parity: pymilvus → milvus-sdk-csharp

## Problem statement

pymilvus is the de-facto reference SDK for Milvus: every feature lands there first, with the canonical test surface. Non-Python SDKs routinely drift behind. The C# SDK's BM25 full-text support was introduced by criteo-forks PR #1, and that PR's test coverage is limited to one happy-path integration test plus eight request-object property tests. A structured audit against pymilvus's own BM25 / `Function` / analyzer test suite is the only way to know where the C# surface is under-tested and where it is missing features entirely.

This document captures that audit, the ports applied, and the gaps left behind.

## Reference

| Field | Value |
|---|---|
| pymilvus | **v2.6.12** — latest stable at the time of the audit, tagged 2026-04-09, commit `09307a9` |
| Server compatibility | Milvus 2.6.x line — same line as the `MilvusFixture` container (`milvusdb/milvus:v2.6.4`) |
| Audit source dirs | `pymilvus/tests/orm/test_schema.py`, `pymilvus/tests/test_client_abstract.py`, `pymilvus/tests/test_client_types.py`, `pymilvus/tests/prepare/test_search.py`, `pymilvus/tests/prepare/test_advanced.py`, `pymilvus/tests/prepare/test_collection.py` |

## Scope

- **In:** porting pymilvus unit tests that exercise the BM25 / `Function` / analyzer surface to equivalent C# tests; flagging feature gaps that would require new public API surface.
- **Out:** implementing the missing features themselves. Each is a separate follow-up PR.
- **Testing idiom:** fixture-less xunit classes wherever pymilvus's test is a pure unit test — matches the pattern introduced by the preceding PR's `TextAnnSearchRequestTests`. Integration tests go into the existing Milvus-container-gated class.

---

## Analysis

### 1. Test-family scope matrix

| Family | pymilvus tests | Portable to C# today? | Planned C# coverage |
|---|---|---|---|
| A — analyzer-enabled VarChar | 1 | Yes, shape-adapted | 3 tests (adapted — C# stores JSON verbatim, Python stores dict) |
| B — `Function` / `FunctionSchema` creation | 4 | Partial — BM25 yes, TextEmbedding + Rerank only when those factories land | 2 tests (BM25 only) |
| C — `FunctionSchema` validation | 10 | Partial — 6 of 10 | 6 tests |
| D — `Function.verify(schema)` semantic check | 7 | **No** — no `Verify` method in C# | 0 — **Gap #1** |
| E — `Function` equality | 2 | **No** — no `Equals` override | 0 — **Gap #2** |
| F — Function round-trip | 2 | Covered by `DescribeAsync_returns_Functions_and_analyzer_flags` in the preceding PR | integration (already present) |
| G — `FunctionScore` reranker | 2 | **No** — type doesn't exist | 0 — **Gap #3** |
| H — `CollectionSchema` with functions | 1 | Covered by the same describe-round-trip test | integration (already present) |
| I — `AnnSearchRequest` | 5 | 4 already covered by `TextAnnSearchRequestTests`; 1 non-portable (Python-specific `__str__`); `expr_params` not in C# | 0 new — **Gap #6 for expr_params** |
| J — `FunctionSchema` (gRPC-side abstraction) | 4 | Yes, remapped onto C# domain type | covered via family B + existing `FunctionId`/`Parameters` surface |
| K — `HybridSearchRequest` | 4 | Partial — 2 usable, 2 require Gap #3 | 1 new integration test |
| L — `RunAnalyzerAsync` | 5 | **No** — API doesn't exist | 0 — **Gap #4** |
| M — Function → gRPC conversion | 3 | Indirectly covered via the describe round-trip test | — |
| **bonus** — `FunctionType` enum ordinals | 1 (parametric ×4) | Yes, but tautological in C# (enum ordinals are declarative) | 0 (dropped as tautological) |

### 2. Translation plan (no code — see the commit for bodies)

Three placements, driven by whether the test needs a running Milvus:

| Target file | Fixture-bound | Tests added | Purpose |
|---|---|---|---|
| `Milvus.Client.Tests/FunctionSchemaTests.cs` (new) | No | 10 | Ports pymilvus `TestFunctionCreation`, `TestFunctionSchema`, and the portable subset of `TestFunctionValidation`. Covers: BM25 ctor populates every property; `CreateBm25` factory coerces single input/output names; ctor rejects null/whitespace name, null/empty input names, null/empty output names; `Parameters` dictionary is mutable; `Description` is preserved; server-assigned `FunctionId` defaults to zero on user construction. |
| `Milvus.Client.Tests/FieldSchemaAnalyzerTests.cs` (new) | No | 3 | Ports `TestFieldSchemaTypeParams.test_analyzer_params_dict`, shape-adapted (pymilvus tests dict→JSON serialisation; C# takes the JSON string verbatim). Covers: analyzer disabled by default; setting `enableAnalyzer: true` wires the flag through; `analyzerParams` is preserved verbatim without SDK-side validation. |
| `Milvus.Client.Tests/HybridSearchTests.cs` (existing) | Yes | 1 | Adapts `TestHybridSearchRequest.test_hybrid_search_with_extra_params` to the C# surface. The pymilvus parametrization exercises `HybridSearchParameters`-level keys (`group_by_field`, `group_size`, `strict_group_size`) which are typed C# properties already covered. This adaptation instead guards `ExtraParameters` wire-through on the TEXT leg specifically — the interesting regression surface `TextAnnSearchRequest` introduces. |

**Pertinence audit (2026-04-17)** — three candidate tests were dropped before the port:

- `Enum_value_matches_proto_ordinal` — tautological in C#; enum ordinals are declarative and the compiler enforces them.
- `Create_text_embedding_function_sets_all_properties` — `TextEmbedding` is not a first-class feature in the C# SDK (no `CreateTextEmbedding` factory). Deferred until that surface lands.
- `Create_rerank_function_sets_all_properties` — same rationale; deferred to Gap #3 (`FunctionReranker`).

### 3. Deliberate no-ops (already covered / not portable)

| pymilvus test | Why no C# addition |
|---|---|
| `TestFunctionCreation.test_output_none_default` | Python allows `Function("f", BM25, ["in"])` with no output. C# ctor and `CreateBm25` both require outputs. Non-portable by design. |
| `TestFunctionValidation.test_invalid_description_type` | Python rejects non-string description at runtime. C# types `string?` — compile-time guarantee. |
| `TestFunctionValidation.test_invalid_function_type` | Python takes a string for function type. C# `MilvusFunctionType` is an enum — compile-time. |
| `TestFunctionValidation.test_duplicate_fields[*]` | Duplicate-field validation not enforced in C# ctor. **Gap #5.** |
| `TestFunctionValidation.test_invalid_params_type` | Python rejects non-dict params. C# `Parameters` is typed `IDictionary<string,string>` — compile-time. |
| `TestAnnSearchRequest.test_ann_search_request_basic|with_expr|invalid_expr_type|str` | Basic properties + Expression: covered by existing `TextAnnSearchRequestTests`. `expr_params`: **Gap #6**. `__str__`: Python-specific. |
| `TestHybridSearchRequest.test_hybrid_search_with_base_ranker` | Positive path covered by existing `HybridSearch_with_RRF_reranker` / `HybridSearch_with_weighted_reranker`. |
| `TestHybridSearchRequest.test_hybrid_search_invalid_ranker` | Python raises on wrong ranker type. C# `HybridSearchAsync` takes `IReranker` — compile-time. |
| `TestConvertFunctionToFunctionSchema.*` | pymilvus tests `Function`→`FunctionSchema` gRPC translation. In C# that translation lives in `MilvusClient.Collection.cs` and is covered end-to-end by `DescribeAsync_returns_Functions_and_analyzer_flags`. |

### 4. Coverage summary

| Family | pymilvus tests | C# before port | C# after port | Remaining gap |
|---|---|---|---|---|
| A | 1 | 0 | 3 | — |
| B | 4 | 0 | 2 (BM25 only) | TextEmbedding + Rerank tests blocked on gaps #3 + future |
| C | 10 | 1 | 7 | 3 (gap #5) |
| D | 7 | 0 | 0 | 7 (gap #1) |
| E | 2 | 0 | 0 | 2 (gap #2) |
| F | 2 | integration | integration | — |
| G | 2 | 0 | 0 | 2 (gap #3) |
| H | 1 | integration | integration | — |
| I | 5 | 8 (superset) | 8 | 1 (gap #6) |
| J | 4 | implicit | 4 | — |
| K | 4 | 1 integration | 2 integration | 2 (gap #3) |
| L | 5 | 0 | 0 | 5 (gap #4) |
| M | 3 | covered via F | covered via F | — |
| bonus FunctionType | 1 | 0 | 0 | — (tautological) |

After the port: **41 C# tests** pertinent to BM25 ↔ **47 pymilvus test concepts**. Remaining delta: **22 tests behind 6 feature gaps** — each tracked below.

---

## Progress

| Status | Item |
|---|---|
| ✅ Applied | `FunctionSchemaTests.cs` — 10 fixture-less unit tests |
| ✅ Applied | `FieldSchemaAnalyzerTests.cs` — 3 fixture-less unit tests |
| ✅ Applied | `HybridSearchTests.HybridSearch_with_BM25_leg_extra_parameters` — 1 integration test |
| ✅ Verified | `dotnet build` on `Milvus.Client` + `Milvus.Client.Tests`: 0 warnings / 0 errors |
| ✅ Verified | Fixture-less unit-test run: 55/55 passed with no Docker (filter: `FunctionSchemaTests`, `FieldSchemaAnalyzerTests`, `TextAnnSearchRequestTests`, `FieldTests`, `MilvusSparseVectorTests`, `TimestampUtilsTests`) |
| ⏳ Pending | Full integration-test run on Windows/Rider (user-side) |
| ⏳ Pending | Push `feature/bm25-pymilvus-test-parity` to `criteo-forks/milvus-sdk-csharp` and open the PR |

---

## Deferred items — gap action plan

Each gap is a standalone follow-up PR. None are required for the ports in this PR. Priority order: blocker first, nice-to-have last.

### Gap #1 — `FunctionSchema.Verify(CollectionSchema)`

**What pymilvus does.** `Function.verify(schema)` runs client-side semantic validation before hitting the server:
- BM25: exactly one input; input must be VARCHAR; output must be SPARSE_FLOAT_VECTOR.
- TEXTEMBEDDING: input VARCHAR; output vector type.
- UNKNOWN: always raises.
- RERANK / MINHASH: permissive (for now).

**Why it matters.** A misconfigured BM25 function (e.g. input field is INT64) reaches the server and fails with a less-friendly error — wasting a round-trip. Client-side validation gives faster, clearer feedback.

**Action plan.**
1. Add `public void Verify(CollectionSchema schema)` on `FunctionSchema`, switching on `Type`.
2. Port `TestFunctionVerify.test_function_verify` as an xunit Theory with 7 InlineData cases.
3. New fixture-less file `Milvus.Client.Tests/FunctionSchemaVerifyTests.cs`.
4. Estimated diff: ~60 LOC prod + ~80 LOC test.

### Gap #2 — `FunctionSchema` value equality

**What pymilvus does.** `Function.__eq__` — two Functions with identical name, type, input/output names, description, and params compare equal.

**Why it matters.** Makes `CollectionSchema.Functions.Contains(...)` work intuitively; enables `Assert.Equal(expected, actual)` on describe-round-trip results.

**Action plan.**
1. Override `Equals(object?)` and `GetHashCode()` on `FunctionSchema`.
2. Equality keys: `Name`, `Type`, `InputFieldNames` sequence, `OutputFieldNames` sequence, `Description`, `Parameters` dictionary content. **Exclude** `FunctionId` (server-assigned, may differ across round-trips).
3. Port `TestFunctionEquality.test_function_equality` + `test_function_not_equal_to_non_function`.
4. Estimated diff: ~20 LOC prod + ~15 LOC test.

### Gap #3 — `FunctionReranker : IReranker`

**What pymilvus does.** `FunctionScore(Function(..., FunctionType.RERANK, ...))` attaches a server-side function as a reranker on hybrid search.

**Why it matters.** Milvus 2.5+ supports function-based rerankers. The C# SDK currently exposes only `RrfReranker` and `WeightedReranker`. A user who wants a server-side custom ranking function has no path.

**Action plan.**
1. New class `Milvus.Client/FunctionReranker.cs : IReranker`, wrapping a `FunctionSchema` of type `Rerank`.
2. Extend `HybridSearchAsync` to recognise `FunctionReranker` and serialise into the hybrid-search `function_score` proto field.
3. Port `TestHybridSearchRequest.test_hybrid_search_with_function_ranker` as an integration test (requires running Milvus).
4. Port `TestFunctionScore.test_function_score_single_function` + `test_function_score_list_of_functions` as fixture-less unit tests.
5. **Also add back** `Create_rerank_function_sets_all_properties` (dropped from the main port — see the pertinence audit note) once a `CreateRerank` factory exists.
6. Estimated diff: ~100 LOC prod + ~60 LOC test.

### Gap #4 — `MilvusClient.RunAnalyzerAsync(...)`

**What pymilvus does.** `client.run_analyzer(texts, analyzer_params=..., with_hash=..., with_detail=..., collection_name=..., field_name=...)` — calls Milvus's stand-alone analyzer RPC and returns per-text token arrays. Used to debug tokenisation and tune analyzer params before committing a schema.

**Why it matters.** BM25 quality depends on the analyzer. Without `RunAnalyzerAsync`, C# users cannot preview tokenisation without inserting data and running a full search.

**Action plan.**
1. Add `public async Task<IReadOnlyList<AnalyzerResult>> RunAnalyzerAsync(...)` on `MilvusClient`. The RPC is `milvus.proto: RunAnalyzer`.
2. New domain type `AnalyzerResult` with per-text `Tokens`, optionally with hash + detail.
3. Port all 5 `TestRunAnalyzer.*` cases as argument-validation unit tests; add at least one integration test that actually calls the server (gated on Milvus ≥2.5).
4. Estimated diff: ~150 LOC prod + ~100 LOC test.

### Gap #5 — Duplicate-field validation in `FunctionSchema` ctor

**What pymilvus does.** Rejects `["x","x"]`, `["y","y"]`, and `input=["f"], output=["f"]` with `ParamError`.

**Why it matters.** Minor: the server also rejects. But the client-side check is cheap and fails fast.

**Action plan.**
1. In `FunctionSchema`'s public ctor, add three checks: distinct inputs, distinct outputs, empty intersection.
2. Port the three parametric cases of `TestFunctionValidation.test_duplicate_fields`.
3. Estimated diff: ~15 LOC prod + ~15 LOC test.

### Gap #6 — `AnnSearchRequest.ExpressionParameters` (parameterised expressions)

**What pymilvus does.** `AnnSearchRequest(..., expr="id > {min_id}", expr_params={"min_id": 100})` — templated expression with named parameters.

**Why it matters.** Safer than string concatenation for dynamic filters. Not BM25-specific but the pymilvus `AnnSearchRequest` suite includes it and the C# SDK is silently behind.

**Action plan.**
1. Add `ExpressionParameters` dictionary on the abstract `AnnSearchRequest`.
2. Wire through `CreateSearchRequestFromAnnSearchRequest` into the gRPC `SearchRequest.expr_template_values`.
3. Port `TestAnnSearchRequest.test_ann_search_request_with_expr_params`.
4. Estimated diff: ~30 LOC prod + ~10 LOC test.

---

## Delivery summary (after this PR merges)

**Shipped by this PR:** 14 tests, 2 new files, 1 existing-file append. No production code changes.

**Gap queue:** 6 PRs pending prioritisation. Total estimated: ~375 LOC prod + ~280 LOC test.

**Recommended first-two-gaps:** #1 (`Verify`) and #2 (`Equals`/`GetHashCode`) — smallest scope, largest friction-reduction per LOC. They unlock stricter test assertions for every future BM25-related change.
