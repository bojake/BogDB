# Compliance Reference — Tactical Messaging Sample

> **Workstream 39 · BogDb.Samples.TacticalMessaging.Blazor**
>
> This document defines the data-fidelity rules, classification boundaries, and synthetic-data
> policy that apply to all seed data, queries, and UI output in this sample.

---

## 1. Purpose

The Tactical Messaging Compliance sample demonstrates BogDB as an impact-analysis engine for
tactical messaging and subsystem compliance. It must be usable in unclassified, publicly
distributable form. The rules in this document ensure that:

- No real classified content is introduced at any stage.
- All standards references remain at the publicly available vocabulary level.
- All platform and mission data is clearly marked synthetic.
- A third party can run the sample without tribal knowledge of any program of record.

---

## 2. Standards Vocabulary Boundaries

Each modelled standard has an explicit scope limit. Seed data, node properties, and relationship
labels must not exceed the scope described below.

### 2.1 Link 16 / MIL-STD-6016 / STANAG 5516

| In scope | Out of scope |
|---|---|
| Publicly named J-series message families (J0.x, J3.x, J6.x, J12.x, …) | Implementation crypto detail |
| Field names as published in MIL-STD-6016 vocabulary summaries | Actual bit-field encodings at classified level |
| STANAG 5516 terminal interoperability concepts | Platform-specific keying material |
| Message type identifiers and field groupings at vocabulary level | Classified emissions or waveform parameters |

**Policy:** All `MessageType.name` values for Link 16 nodes must use the form `J<family>.<type>`
(e.g., `J3.2`, `J12.3`) as publicly referenced in unclassified summaries. No field contains
values that would require classification review.

### 2.2 Link 22 / STANAG 5522

| In scope | Out of scope |
|---|---|
| STANAG 5522 message family designators | Any NATO restricted annex content |
| Concept of Link 22 as a fallback/degraded comms message set | Specific radio or network configuration parameters |
| Publicly available STANAG 5522 vocabulary terms | Implementation tables at controlled level |

**Policy:** Link 22 nodes use concept-level labels only. No seed data reproduces table contents
from restricted-distribution annexes.

### 2.3 VMF / MIL-STD-6017

| In scope | Out of scope |
|---|---|
| Variable Message Format family names and header concepts | Specific data element bit positions |
| Publicly named message sets (e.g., C2, Intel, Logistics VMF families) | Classified payload encodings |
| Concept of VMF as an interoperability protocol | Platform-specific implementation parameters |

**Policy:** All VMF nodes use public MIL-STD-6017 vocabulary only. Field names must be at the
message catalog level, not the data dictionary implementation level.

### 2.4 APP-11 / Military Message Catalog

| In scope | Out of scope |
|---|---|
| Concept of message catalog artifacts as interoperability artifacts | Any RESTRICTED content from NATO publications |
| Notion of a catalog entry referencing a message type | Actual USMTF data elements at controlled level |

**Policy:** APP-11 style nodes are modelled as `MessageFamily` or `EvidenceArtifact` nodes with
`source = "APP-11-CATALOG"` to distinguish them. No field reproduces protected content.

### 2.5 Link 11 (Legacy / Migration Comparator)

| In scope | Out of scope |
|---|---|
| Concept of Link 11 as a legacy NTDS-predecessor standard | Classified implementation |
| Use as a migration/retirement comparator baseline node | - |

**Policy:** Link 11 nodes appear only in the legacy baseline (Release 0 / Q0) to illustrate
migration impact analysis. They are clearly annotated `deprecated = true`.

---

## 3. Platform and Mission Data

All platform data is **synthetic**. No real hull number, tail number, N-number, serial number,
radio call sign, or unit designation appears in any seed file, EF Core migration, or graph node.

Synthetic platform identifiers follow the scheme:

```
PLT-<TYPE>-<SEQ>
```

Examples:
- `PLT-SHIP-001`, `PLT-SHIP-002` — surface vessel (type generic)
- `PLT-AUV-001` — autonomous underwater vehicle
- `PLT-HELO-001` — rotary wing asset
- `PLT-FIXED-001` — fixed-wing asset

Mission capability names (e.g., `MCAP-TRACK-CORRELATION`, `MCAP-FIRE-CONTROL`) are synthetic
functional labels, not references to any real program capability.

---

## 4. Identifier Policy

| Identifier type | Rule |
|---|---|
| Standard names | Use public standard designator (e.g., `MIL-STD-6016`, `STANAG 5516`) |
| Message family IDs | Use public family label (e.g., `J3`, `J12`, `VMF-C2`) |
| Message type IDs | Synthetic sequential within family: `J3.2-TRACK`, `VMF-C2-001` |
| Component IDs | Synthetic: `TC-<SEQ>`, `IC-<SEQ>` |
| Requirement IDs | Synthetic: `REQ-<STANDARD>-<SEQ>`, e.g., `REQ-L16-001` |
| Test case IDs | Synthetic: `TC-<SEQ>` |
| Evidence artifact IDs | Synthetic: `EA-<SEQ>` |
| Certification package IDs | Synthetic: `CP-<SEQ>` |
| Baseline IDs | `BL-Q1-2024`, `BL-Q2-2024`, `BL-Q3-2024`, `BL-Q4-2024` (or `BL-R1` through `BL-R4`) |

---

## 5. Seed Data Lanes

The seed strategy is divided into three lanes to keep concerns separate and allow independent
extension without risking cross-contamination.

### Lane A — Standards Lane

Populates the upper half of the graph: the normative standards world.

```
Standard → StandardEdition → MessageFamily → MessageType → MessageField
                                                         ↑
                                                     Profile
```

Seeded standards:
1. **Link 16** (MIL-STD-6016 / STANAG 5516) — active, primary standard
2. **Link 22** (STANAG 5522) — active, SATCOM/degraded path
3. **VMF** (MIL-STD-6017) — active, C2 / Intel / Logistics families modelled
4. **Link 11** (legacy) — deprecated, present in baseline Q0/R0 only
5. **APP-11 Catalog** — message catalog artifacts referenced by the above

### Lane B — System Lane

Populates the middle: the engineering implementation world.

```
TranslatorComponent → Profile       (IMPLEMENTS)
TranslatorComponent → MessageField  (USES_FIELD)
Subsystem → TranslatorComponent     (DEPENDS_ON)
Subsystem → Platform                (ON_PLATFORM)
```

Seeded subsystems (all synthetic, naval/C2 inspired):
1. Tactical Picture Gateway
2. Track Correlation Service
3. Fire Control Interface Adapter
4. Comms Mediation Service
5. Mission Planning Exchange Adapter
6. Message Validation / Translation Service

### Lane C — Assurance Lane

Populates the lower half: the evidence and certification world.

```
TranslatorComponent → Requirement   (SATISFIES)
Requirement → TestCase              (VERIFIED_BY)
TestCase → EvidenceArtifact         (PRODUCES)
EvidenceArtifact → CertificationPackage (SUPPORTS)
Subsystem → MissionCapability       (ENABLES)
```

Baselines seed four snapshots (Q1–Q4 or R1–R4), each containing a `CONTAINS` relationship
to every relevant node valid at that snapshot. This is what enables drift analysis and
consensus computation across snapshot windows.

---

## 6. Change Event Modelling

`ChangeEvent` nodes represent discrete change proposals or regression findings. They are not
part of the core graph schema but are injected during scenario execution.

```
(:ChangeEvent {id, description, severity, standard, field})-[:AFFECTS]->
  (:MessageField | :MessageType | :TranslatorComponent)
```

Severity values: `LOW`, `MODERATE`, `HIGH`, `CRITICAL`

The impact analysis runs BFS from the affected node outward through `USES_FIELD → IMPLEMENTS`,
`DEPENDS_ON`, `SATISFIES → VERIFIED_BY → PRODUCES → SUPPORTS`, and `ENABLES` edges to
enumerate the full blast radius.

---

## 7. Prohibited Content Checklist

Before committing any seed data or documentation to this project, verify:

- [ ] No bit-level encoding tables from any standard
- [ ] No platform names, hull numbers, or operational unit identifiers
- [ ] No test programme names or programme of record references
- [ ] No encryption, keying, or COMSEC references
- [ ] No FOUO/CUI/RESTRICTED markings applied to this repository
- [ ] All standard citations use public-domain designators only
- [ ] All data labelled `synthetic = true` where applicable

---

## 8. Reference Documents (Public Only)

| Document | Notes |
|---|---|
| MIL-STD-6016 (public summary) | Link 16 vocabulary reference level |
| STANAG 5516 (public concept) | NATO Link 16 terminal interoperability |
| STANAG 5522 (public concept) | NATO Link 22 terminal interoperability |
| MIL-STD-6017 (public summary) | VMF vocabulary reference level |
| APP-11 (public concept) | NATO military message catalog concept |
