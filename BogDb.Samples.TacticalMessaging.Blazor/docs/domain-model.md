# Domain Model — Tactical Messaging Sample

> **Workstream 39 · BogDb.Samples.TacticalMessaging.Blazor**
>
> This document is the authoritative reference for the graph schema used in the sample.
> It describes every node type, relationship type, and key property, and explains the
> *why* behind each modelling decision.

---

## 1. Overview

The graph models the full compliance lineage of tactical messaging standards:
from the **normative standards world** (what must be true) through the
**engineering world** (how it is implemented) to the **assurance world**
(how compliance is verified and certified), all anchored to **snapshots**
(baselines) so the model supports drift, consensus, and impact analysis
across time.

```
┌───────────────────────────────────────────────────────┐
│  STANDARDS LANE (Lane A)                              │
│  Standard → StandardEdition → MessageFamily           │
│                                  → MessageType        │
│                                     → MessageField    │
│                       Profile ──────────────┘         │
│                (CONSTRAINS MessageType)                │
└───────────────────────────────────────────────────────┘
                          │
                     IMPLEMENTS
                          │
┌───────────────────────────────────────────────────────┐
│  SYSTEM LANE (Lane B)                                 │
│  TranslatorComponent → InterfaceContract              │
│  Subsystem → TranslatorComponent                      │
│  Subsystem → Platform                                 │
└───────────────────────────────────────────────────────┘
                          │
                    SATISFIES / ENABLES
                          │
┌───────────────────────────────────────────────────────┐
│  ASSURANCE LANE (Lane C)                              │
│  Requirement → TestCase → EvidenceArtifact            │
│                           → CertificationPackage      │
│  MissionCapability                                    │
└───────────────────────────────────────────────────────┘
                          │
              CONTAINS (in any Baseline)
                          │
┌───────────────────────────────────────────────────────┐
│  SNAPSHOT LAYER                                       │
│  Baseline (Q1, Q2, Q3, Q4 / R1–R4)                   │
└───────────────────────────────────────────────────────┘
```

---

## 2. Node Types

### 2.1 Standard

The root identity of a messaging standard specification.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic primary key, e.g., `STD-L16` |
| `name` | string | Public designator, e.g., `MIL-STD-6016` |
| `alias` | string | Common name, e.g., `Link 16` |
| `status` | string | `ACTIVE` \| `DEPRECATED` \| `LEGACY` |
| `governing_body` | string | e.g., `DoD`, `NATO` |

**Design note:** The `Standard` node is intentionally thin. It is the anchor for edition
versioning and should not contain lifecycle detail that belongs on `StandardEdition`.

---

### 2.2 StandardEdition

A versioned release of a Standard. Allows the graph to represent changes across
revisions (e.g., Rev C vs Rev D of MIL-STD-6016).

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `SE-L16-C` |
| `standard_id` | string | FK to parent `Standard.id` |
| `edition_label` | string | e.g., `Rev C`, `Ed 3`, `2022` |
| `effective_date` | string | ISO date at vocabulary level, e.g., `2019-01-01` |
| `deprecated` | bool | Whether this edition is retired |

---

### 2.3 MessageFamily

A logical grouping of messages within a standard edition. In Link 16 terms, this
is the J-series family designation (J0, J3, J6, J7, J12, …).

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `MF-L16-J3` |
| `edition_id` | string | FK to `StandardEdition.id` |
| `family_designator` | string | e.g., `J3`, `VMF-C2`, `L22-NTDS` |
| `description` | string | Short human label |

---

### 2.4 MessageType

A discrete message format within a family. The finest normative unit that
specifies what a translator must handle.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `MT-J3-2` |
| `family_id` | string | FK to `MessageFamily.id` |
| `type_designator` | string | e.g., `J3.2`, `J12.3`, `VMF-C2-001` |
| `description` | string | e.g., `Track Message`, `Mission Assignment` |
| `direction` | string | `TRANSMIT` \| `RECEIVE` \| `BIDIRECTIONAL` |

---

### 2.5 MessageField

An individual data element within a message type. The leaf node of the
standards layer. Change events typically target this level.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `MFL-J3-2-001` |
| `message_type_id` | string | FK to `MessageType.id` |
| `field_name` | string | Vocabulary-level name, e.g., `TRACK_NUMBER` |
| `data_category` | string | `IDENTITY` \| `KINEMATICS` \| `EMITTER` \| `LINK` \| `STATUS` |
| `mandatory` | bool | Whether the field is required by the standard |

**Design note:** `data_category` is a synthetic classification to enable
"all kinematic-field changes" queries without reproducing encoding tables.

---

### 2.6 Profile

A named constraint set applied to a `MessageType` for a specific use-case or
platform class. Profiles are what translator components actually implement.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `PRF-L16-NAVAL-J3` |
| `name` | string | e.g., `Naval Surface Warfare J3.2 Profile` |
| `message_type_id` | string | FK to the constrained `MessageType.id` |
| `platform_class` | string | e.g., `SURFACE`, `SUBSURFACE`, `AIR` |
| `required_fields` | string | Comma-separated list of `MessageField.id` values |

---

### 2.7 TranslatorComponent

A software component that parses, generates, or translates tactical messages.
The central node of the system lane. Impact analysis BFS typically originates
from or terminates on a `TranslatorComponent`.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `TCOMP-001` |
| `name` | string | e.g., `Link16_J3_2_TrackMessage_Parser` |
| `version` | string | e.g., `2.4.1` |
| `language` | string | Implementation language, e.g., `C`, `Ada`, `C++` |
| `status` | string | `ACTIVE` \| `DEPRECATED` \| `UNDER_CHANGE` |

---

### 2.8 InterfaceContract

A formal API or data-exchange contract between two components or between a
component and an external system. A change to a `TranslatorComponent` may
break one or more `InterfaceContract` nodes.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `IC-001` |
| `name` | string | e.g., `Gateway-to-Correlator Track Feed` |
| `protocol` | string | e.g., `UDP/MULTICAST`, `MQ`, `REST` |
| `version` | string | Contract version |

---

### 2.9 Requirement

A verifiable statement of what a translator or subsystem must do with respect
to a specified profile or message type.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `REQ-L16-001` |
| `statement` | string | Short requirement text |
| `type` | string | `FUNCTIONAL` \| `INTEROPERABILITY` \| `PERFORMANCE` |
| `priority` | string | `SHALL` \| `SHOULD` \| `MAY` |
| `allocating_standard` | string | FK to `Standard.id` |

---

### 2.10 TestCase

A named test procedure that verifies one or more requirements.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `TC-001` |
| `name` | string | e.g., `J3.2 Track Decode Round-Trip` |
| `method` | string | `SIMULATION` \| `HARDWARE_IN_LOOP` \| `ANALYSIS` |
| `pass_criteria` | string | Plain-language pass criterion |
| `status` | string | `PASSING` \| `FAILING` \| `BLOCKED` \| `NOT_RUN` |

---

### 2.11 EvidenceArtifact

A concrete artefact produced by a test case execution: a log, report,
attestation, or recorded result.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `EA-001` |
| `artifact_type` | string | `TEST_LOG` \| `ANALYSIS_REPORT` \| `ATTESTATION` |
| `produced_by` | string | FK to `TestCase.id` |
| `date_produced` | string | ISO date |
| `verdict` | string | `PASS` \| `FAIL` \| `INCONCLUSIVE` |

---

### 2.12 CertificationPackage

An aggregate collection of evidence that supports a formal accreditation or
interoperability certification milestone.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `CP-001` |
| `name` | string | e.g., `Link 16 Naval Interoperability Package R2` |
| `certification_authority` | string | e.g., `NATO-NCIA`, `DoD-JITC`, `SYNTHETIC` |
| `status` | string | `IN_PROGRESS` \| `SUBMITTED` \| `APPROVED` |

---

### 2.13 Subsystem

An operational subsystem that depends on one or more translator components
and ultimately enables a mission capability. The bridge between the engineering
and operational worlds.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `SS-001` |
| `name` | string | e.g., `Tactical Picture Gateway` |
| `domain` | string | `C2` \| `INTEL` \| `LOGISTICS` \| `FIRE_CONTROL` |

---

### 2.14 Platform

A synthetic operational platform that hosts one or more subsystems.
All platform data is purely synthetic — see `compliance.md`.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `PLT-SHIP-001` |
| `platform_type` | string | `SURFACE` \| `SUBSURFACE` \| `AIR` \| `LAND` |
| `synthetic` | bool | Always `true` |

---

### 2.15 MissionCapability

A high-level operational capability enabled by one or more subsystems.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `MCAP-001` |
| `name` | string | e.g., `MCAP-TRACK-CORRELATION` |
| `category` | string | `SA` (Situational Awareness) \| `C2` \| `FIRES` \| `LOGISTICS` |

---

### 2.16 Baseline

A named snapshot of the graph at a point in time. Used as the anchor for
drift analysis and consensus computation.

| Property | Type | Description |
|---|---|---|
| `id` | string | e.g., `BL-Q1-2024` |
| `label` | string | Human label, e.g., `Q1 2024 Release` |
| `snapshot_date` | string | ISO date |
| `sealed` | bool | Whether the baseline is immutable |

---

### 2.17 ChangeEvent *(scenario-injected)*

Not part of the persistent schema. Injected during impact-analysis scenarios
to represent a discrete change proposal or regression finding.

| Property | Type | Description |
|---|---|---|
| `id` | string | Synthetic key, e.g., `CHG-001` |
| `description` | string | Human-readable change summary |
| `severity` | string | `LOW` \| `MODERATE` \| `HIGH` \| `CRITICAL` |
| `affecting_standard` | string | FK to `Standard.id` |

---

## 3. Relationship Types

| Relationship | From | To | Key Properties | Meaning |
|---|---|---|---|---|
| `HAS_EDITION` | Standard | StandardEdition | — | This standard has this versioned edition |
| `DEFINES_FAMILY` | StandardEdition | MessageFamily | — | This edition defines this message family |
| `HAS_TYPE` | MessageFamily | MessageType | — | This family contains this message type |
| `HAS_FIELD` | MessageType | MessageField | `mandatory` | This type includes this field |
| `CONSTRAINS` | Profile | MessageType | `profile_id` | This profile constrains this message type |
| `IMPLEMENTS` | TranslatorComponent | Profile | `implementation_version` | This component implements this profile |
| `USES_FIELD` | TranslatorComponent | MessageField | `access` (READ/WRITE) | This component reads or writes this field |
| `DEPENDS_ON` | Subsystem | TranslatorComponent | `dependency_type` | This subsystem relies on this component |
| `ON_PLATFORM` | Subsystem | Platform | `deployment_date` | This subsystem is deployed on this platform |
| `SATISFIES` | TranslatorComponent | Requirement | — | This component satisfies this requirement |
| `VERIFIED_BY` | Requirement | TestCase | — | This requirement is verified by this test |
| `PRODUCES` | TestCase | EvidenceArtifact | `run_date` | This test produced this evidence |
| `SUPPORTS` | EvidenceArtifact | CertificationPackage | — | This evidence supports this certification |
| `ENABLES` | Subsystem | MissionCapability | — | This subsystem enables this capability |
| `CONTAINS` | Baseline | *(any node)* | `valid_from`, `valid_to` | This baseline snapshot includes this entity |
| `AFFECTS` | ChangeEvent | MessageField \| MessageType \| TranslatorComponent | `severity` | This change event affects this entity |

---

## 4. Modelling Rationale

### Why graph rather than relational joins?

A relational schema for this domain requires at minimum:
- 5-way join to link a `ChangeEvent` to affected `MissionCapability`
- Self-referential CTEs to traverse baseline snapshots
- Separate staging tables to compute consensus across baselines

BogDB expresses the same as:

```cypher
-- Blast radius from a field change
MATCH (chg:ChangeEvent {id: $id})-[:AFFECTS]->(mf:MessageField)
       <-[:USES_FIELD]-(tc:TranslatorComponent)
       <-[:DEPENDS_ON]-(ss:Subsystem)
       -[:ENABLES]->(cap:MissionCapability)
RETURN chg.id, mf.field_name, tc.name, ss.name, cap.name
```

The graph traversal is O(subgraph) rather than O(full cross-product), and it
naturally expresses the propagation semantics of an impact change.

### Why snapshots via Baseline nodes rather than temporal properties?

The BogDB change-review framework uses per-shard snapshots. Modelling baselines
as first-class `Baseline` nodes with `CONTAINS` edges mirrors this pattern and
allows the `BuildChangeReviewOverview` and `SummarizeProjectedNodeChanges` APIs
to operate on baseline pairs directly without offline ETL.

### Why separate Profiles from MessageTypes?

A `MessageType` is normative (defined by the standard). A `Profile` is
implementation-advisory (defined by a capability class or platform class).
Keeping them separate allows a single `J3.2` node to be constrained by
multiple profiles (Surface, Air, Subsurface) without duplicating the type.

---

## 5. Query Patterns

The following canonical query shapes correspond to the six required showcase queries.

### Q1 — Blast Radius from a Field Change
```cypher
MATCH (chg:ChangeEvent {id: $changeId})-[:AFFECTS]->(mf:MessageField)
      <-[:USES_FIELD]-(tc:TranslatorComponent)
      <-[:DEPENDS_ON]-(ss:Subsystem)
      -[:ENABLES]->(cap:MissionCapability)
RETURN mf.field_name, tc.name, ss.name, cap.name
ORDER BY cap.name
```

### Q2 — Full Certification Chain for a Translator Component
```cypher
MATCH (tc:TranslatorComponent {id: $id})-[:SATISFIES]->(req:Requirement)
      -[:VERIFIED_BY]->(t:TestCase)-[:PRODUCES]->(ea:EvidenceArtifact)
      -[:SUPPORTS]->(cp:CertificationPackage)
RETURN tc.name, req.id, t.name, ea.verdict, cp.name
```

### Q3 — All Platforms Affected by a Standard Edition Change
```cypher
MATCH (se:StandardEdition {id: $editionId})-[:DEFINES_FAMILY]->(:MessageFamily)
      -[:HAS_TYPE]->(:MessageType)<-[:CONSTRAINS]-(p:Profile)
      <-[:IMPLEMENTS]-(tc:TranslatorComponent)<-[:DEPENDS_ON]-(ss:Subsystem)
      -[:ON_PLATFORM]->(plt:Platform)
RETURN DISTINCT plt.id, plt.platform_type, ss.name, tc.name
```

### Q4 — Unverified Requirements (no passing test evidence)
```cypher
MATCH (tc:TranslatorComponent)-[:SATISFIES]->(req:Requirement)
WHERE NOT EXISTS {
    MATCH (req)-[:VERIFIED_BY]->(t:TestCase)-[:PRODUCES]->(ea:EvidenceArtifact)
    WHERE ea.verdict = 'PASS'
}
RETURN req.id, req.statement, tc.name
ORDER BY req.priority
```

### Q5 — Baseline Drift (Q2 vs Q3 node deltas)
Leverages the BogDB `BuildChangeReviewOverview` API on paired baseline shards.

### Q6 — Consensus Across All Baselines (Impact Persistence)
Leverages the BogDB `SummarizeProjectedNodeChanges` + selection-family analytics
to identify nodes impacted in 3 or more of 4 baselines.
