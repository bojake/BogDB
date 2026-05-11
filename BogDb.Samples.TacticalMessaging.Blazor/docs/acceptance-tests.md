# Acceptance Tests — Tactical Messaging Sample

> **Workstream 39 · BogDb.Samples.TacticalMessaging.Blazor**
>
> This document defines the acceptance criteria for the sample. Each scenario maps directly
> to a success metric or acceptance test stated in `samples.md §Workstream 39`. Tests are
> written in Gherkin-style Given/When/Then for clarity and future Reqnroll/xUnit integration.

---

## 1. Scope and Relationship to Success Metrics

| Success Metric (from samples.md) | Acceptance Scenario(s) |
|---|---|
| Seed load succeeds end-to-end | AT-01 |
| At least 6 named showcase queries run correctly | AT-02 through AT-07 |
| At least 3 snapshot comparisons produce nontrivial drift output | AT-08, AT-09, AT-10 |
| At least 1 change scenario produces a full mission-impact chain | AT-11 |
| At least 1 consensus report identifies persistent cross-baseline impact | AT-12 |
| Demo time from start to first result is under threshold | AT-13 |
| Docs let a third party run without tribal knowledge | AT-14 (manual) |

---

## 2. Seed and Load Tests

### AT-01 — Seed Load Succeeds End-to-End

**Goal:** Verify that the EF Core / SQLite seed → BogDB graph ingestion pipeline completes
without error and produces a graph of the expected minimum scale.

```gherkin
Scenario: Full seed pipeline completes without error
  Given the application is started fresh with no existing graph
  When the seed pipeline runs through all three lanes (A, B, C)
  Then the graph must contain at least 5 Standard nodes
  And the graph must contain at least 20 MessageType nodes
  And the graph must contain at least 6 TranslatorComponent nodes
  And the graph must contain at least 4 Baseline nodes
  And the graph must contain at least 10 Requirement nodes
  And the graph must contain at least 10 TestCase nodes
  And no error is thrown at any point
```

**Verification:**
```cypher
MATCH (n:Standard)   RETURN COUNT(n)  -- expect >= 5
MATCH (n:MessageType) RETURN COUNT(n) -- expect >= 20
MATCH (n:TranslatorComponent) RETURN COUNT(n) -- expect >= 6
MATCH (n:Baseline)   RETURN COUNT(n)  -- expect >= 4
MATCH (n:Requirement) RETURN COUNT(n) -- expect >= 10
MATCH (n:TestCase)   RETURN COUNT(n)  -- expect >= 10
```

---

## 3. Showcase Query Tests

### AT-02 — Shortest Path from Translator to Mission Capability

**Goal:** Verify that a multi-hop traversal from a `TranslatorComponent` through
`DEPENDS_ON` → `ENABLES` reaches a `MissionCapability` node correctly.

```gherkin
Scenario: Translator-to-capability reachability path
  Given the graph is seeded
  When the query "shortest path from TranslatorComponent Link16_J3_2_TrackMessage_Parser
       to any MissionCapability" is executed
  Then the result must contain at least one path
  And the path must pass through at least one Subsystem node
  And the result must include the mission capability name
```

---

### AT-03 — All Translators Implementing a Given Profile

**Goal:** Verify that the `IMPLEMENTS` relationship correctly associates translator
components with their normalised profile nodes.

```gherkin
Scenario: Components implementing a Link 16 J3.2 naval profile
  Given the graph contains a Profile node for Link 16 J3.2 naval surface
  When querying all TranslatorComponents that IMPLEMENT that Profile
  Then at least 1 TranslatorComponent must be returned
  And each returned component must have a name and version property
  And at least 1 component must have status = ACTIVE
```

---

### AT-04 — Certification Chain for a Translator Component

**Goal:** Verify full traversal from `TranslatorComponent` through requirements,
tests, evidence, and certification packages.

```gherkin
Scenario: Full certification lineage for a translator component
  Given the graph is seeded with certification chain data in Lane C
  When querying the certification chain for TranslatorComponent TC-001
  Then the result must include at least 1 Requirement
  And at least 1 TestCase for each included Requirement
  And at least 1 EvidenceArtifact for each TestCase
  And at least 1 CertificationPackage supported by the evidence
```

---

### AT-05 — All Platforms Affected by a Standard Edition

**Goal:** Verify multi-hop traversal from a `StandardEdition` through profiles,
components, and subsystems to platforms.

```gherkin
Scenario: Platforms affected by a Link 16 edition update
  Given the graph contains StandardEdition SE-L16-C
  When querying all Platforms reachable via DEFINES_FAMILY → HAS_TYPE → CONSTRAINS
       → IMPLEMENTS → DEPENDS_ON → ON_PLATFORM
  Then at least 2 Platform nodes must be returned
  And each returned platform must have a platform_type property
  And response must include the traversal path depth
```

---

### AT-06 — Unverified Requirements

**Goal:** Verify that the negative-pattern query (requirements with no passing evidence)
returns a non-empty result when such requirements exist.

```gherkin
Scenario: Identifying requirements with no passing test evidence
  Given the graph contains at least 2 Requirements with no PASS verdict on their evidence
  When querying for Requirements lacking a TestCase with a passing EvidenceArtifact
  Then those 2 Requirements must appear in the results
  And the result must include the requirement statement and priority
  And no Requirement with a PASS evidence must appear in the results
```

---

### AT-07 — Cross-Standard Field Overlap

**Goal:** Verify the query that identifies `MessageField` nodes shared (by name) across
multiple standards, revealing potential harmonisation opportunities.

```gherkin
Scenario: MessageFields shared across two or more standards
  Given the graph is seeded with at least Link 16 and VMF fields
  And at least one field name (e.g., TRACK_NUMBER) appears in both standards
  When querying for fields whose field_name appears in 2 or more MessageFamily trees
  Then at least 1 field name must be returned
  And the result must group by field_name and list the standards
```

---

## 4. Snapshot Comparison Tests

### AT-08 — Baseline Q1 vs Q2 Node Delta

**Goal:** Verify that comparing Q1 and Q2 baselines produces a nontrivial diff
(i.e., some nodes are added, some removed, some shared).

```gherkin
Scenario: Drift from Q1 to Q2 baseline
  Given baselines BL-Q1-2024 and BL-Q2-2024 are both sealed and loaded
  When BuildChangeReviewOverview is called with fromBaseline=Q1, toBaseline=Q2
  Then AddedNodeCount must be > 0
  And RemovedNodeCount must be > 0
  And the SummaryLabel must be non-empty
  And the total delta must be non-zero
```

---

### AT-09 — Baseline Q2 vs Q3 Relationship Delta

**Goal:** Verify that the Q2→Q3 drift captures relationship-level changes,
not just node-level changes.

```gherkin
Scenario: Relationship drift from Q2 to Q3 baseline
  Given baselines BL-Q2-2024 and BL-Q3-2024 are both sealed and loaded
  When SummarizeProjectedNodeChanges is called with fromBaseline=Q2, toBaseline=Q3
  Then at least 1 relationship type must appear in the AddedRels list
  And at least 1 relationship type must appear in the RemovedRels list
  And the result must distinguish between IMPLEMENTS and SATISFIES relationship changes
```

---

### AT-10 — Baseline Q3 vs Q4 Impact Class Drift

**Goal:** Verify that the Q3→Q4 drift identifies a change in `TranslatorComponent`
count, specifically simulating a decommission of a legacy component.

```gherkin
Scenario: Translator component retirement visible in Q3 to Q4 drift
  Given baseline Q3 contains TranslatorComponent TC-LEGACY-001 (status=DEPRECATED)
  And baseline Q4 does not contain TC-LEGACY-001
  When BuildChangeReviewOverview is called with fromBaseline=Q3, toBaseline=Q4
  Then TC-LEGACY-001 must appear only in the RemovedNodes list
  And the affected Subsystem must also appear in the impact propagation chain
```

---

## 5. Change Impact Scenario Tests

### AT-11 — Full Mission Impact Chain from Field Change

**Goal:** Verify that a simulated change to a `MessageField` propagates all the way
to a `MissionCapability` through the complete compliance lineage.

This is the "money scenario" for the sample and must produce output touching all
six hop types: `MessageField → TranslatorComponent → Subsystem → MissionCapability`
and `TranslatorComponent → Requirement → TestCase → EvidenceArtifact → CertificationPackage`.

```gherkin
Scenario: End-to-end blast radius from a field-level change
  Given a ChangeEvent CHG-001 with severity=HIGH is injected targeting
        MessageField MFL-J3-2-001 (J3.2 TRACK_NUMBER field)
  When the impact analysis query is executed
  Then the result must include at least 1 TranslatorComponent node
  And the result must include at least 1 Subsystem node
  And the result must include at least 1 MissionCapability node
  And the result must include at least 1 Requirement node impacted by the change
  And the result must include at least 1 TestCase that verifies an impacted requirement
  And the result must include at least 1 EvidenceArtifact from an impacted test
  And the result must include at least 1 CertificationPackage that relied on that evidence
  And the full chain must be returned in a single graph query without client-side joins
```

**Cypher reference:**
```cypher
MATCH (chg:ChangeEvent {id: 'CHG-001'})-[:AFFECTS]->(mf:MessageField)
MATCH (tc:TranslatorComponent)-[:USES_FIELD]->(mf)
MATCH (ss:Subsystem)-[:DEPENDS_ON]->(tc)
MATCH (ss)-[:ENABLES]->(cap:MissionCapability)
MATCH (tc)-[:SATISFIES]->(req:Requirement)-[:VERIFIED_BY]->(t:TestCase)
MATCH (t)-[:PRODUCES]->(ea:EvidenceArtifact)-[:SUPPORTS]->(cp:CertificationPackage)
RETURN chg.id, mf.field_name, tc.name, ss.name, cap.name,
       req.id, t.name, ea.verdict, cp.name
```

---

## 6. Consensus and Persistence Tests

### AT-12 — Persistent Cross-Baseline Impact Identification

**Goal:** Verify that the consensus analytics surface nodes impacted in 3 or more
of the 4 baselines as "persistent risk" items.

```gherkin
Scenario: Consensus report identifies persistently impacted items
  Given all 4 baselines (Q1, Q2, Q3, Q4) are seeded
  And at least 1 TranslatorComponent undergoes a change in at least 3 of those baselines
  When the consensus computation is run across all 4 baseline pairs
  Then the report must include that component in the "persistent impact" list
  And the report must show the count of baselines in which it appeared as changed
  And the report must order results by impact persistence count descending
```

---

## 7. Performance and Usability Tests

### AT-13 — Demo Time to First Meaningful Result

**Goal:** Verify the demo is fast enough that it does not lose audience attention.

```gherkin
Scenario: Application provides first visible result within acceptable time
  Given the application is started with seed data already loaded
  When the user navigates to the Impact Analysis page and selects a change event
  Then the blast radius result must be displayed within 5 seconds
  And the result must contain at least 5 rows
  And the UI must not show a loading spinner for more than 3 seconds after interaction
```

---

### AT-14 — Third-Party Runability (Manual)

**Goal:** Verify that the sample can be started by a third party with no prior
knowledge of the codebase.

```gherkin
Scenario: Third-party operator can run the sample without assistance
  Given only the project README and this docs directory
  When the operator follows the README getting-started steps
  Then the application starts without error
  And the seed data loads automatically
  And the showcase queries all return results without additional configuration
  And no documentation asks the reader to "ask the team" for missing information
```

---

## 8. Compliance Guard Tests

### AT-15 — No Classified Content in Any Output

**Goal:** Verify that no query result, page render, or export contains any content
that would require classification review per the rules in `compliance.md`.

```gherkin
Scenario: All rendered content remains within unclassified bounds
  Given the application is running with fully seeded data
  When every page in the application is rendered
  Then no page may display bit-level encoding from any standard
  And no page may display a real platform identifier or hull number
  And no page may display content from restricted-distribution annexes
  And the platform_type property is always rendered as a synthetic label
```

---

## 9. Appendix — Test Data Requirements

| Scenario | Minimum seed required |
|---|---|
| AT-01 | Full seed (all three lanes) |
| AT-02 to AT-07 | Full seed, all standards, all components |
| AT-08 | Q1 and Q2 baselines both sealed |
| AT-09 | Q2 and Q3 baselines both sealed |
| AT-10 | Q3 baseline with TC-LEGACY-001; Q4 baseline without it |
| AT-11 | ChangeEvent CHG-001 targeting MFL-J3-2-001; full Lane A/B/C |
| AT-12 | All 4 baselines; at least 1 component changed in 3 of 4 |
| AT-13 | Full seeded application running locally |
| AT-14 | Clean checkout, README only |
| AT-15 | Full seeded application running locally |
