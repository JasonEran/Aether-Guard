# CP3407 Design Artifacts (External Tool Evidence)

Last updated: 2026-02-27

This page is the grading-facing registry for the rubric design requirement
(architecture UML, database design, and interface prototype) using external tools.

## Artifact Register

| Artifact | Required Tool Type | Share Link | Export File | Status |
| --- | --- | --- | --- | --- |
| Architecture UML (v2.3) | UML tool (Gliffy/draw.io/Lucidchart) | `TBD` | `docs/design/exports/architecture-uml-v2.3.png` | Pending link |
| Database ERD (v2.3) | Database diagram tool (GenMyModel/dbdiagram.io) | `TBD` | `docs/design/exports/database-erd-v2.3.png` | Ready source |
| UI Prototype (v2.3) | Prototyping tool (NinjaMock/Figma) | `TBD` | `docs/design/exports/ui-prototype-v2.3.pdf` | Ready spec |

## Ready-to-Use Source Assets

### Architecture UML

- Base service and class relations are documented in:
  - `docs/diagrams/Class-Diagram-v2.3.md`
  - `docs/ARCHITECTURE-v2.3.md`

Suggested export format:

- One overview diagram (cross-service)
- Four component diagrams (Core/Agent/AI/Web)

### Database ERD

- Import-ready DBML file:
  - `docs/design/AetherGuard-v2.3.dbml`
- Source of truth from code:
  - `src/services/core-dotnet/AetherGuard.Core/Data/ApplicationDbContext.cs`
  - `src/services/core-dotnet/AetherGuard.Core/models/*`

### UI Prototype

- Prototype screen and interaction spec:
  - `docs/design/UI-Prototype-Spec-v2.3.md`
- Implemented components to mirror:
  - `src/web/dashboard/components/*`
  - `src/web/dashboard/app/DashboardClient.tsx`

## Upload Procedure (Assessor-Friendly)

1. Build diagram/prototype in external tool.
2. Export image/PDF and store under `docs/design/exports/`.
3. Paste public share links into the register above.
4. Update status to `Completed`.
5. Link this page from PR/release evidence comments.

## Minimum Completion Criteria (for HD Design)

- [ ] External-tool UML link + export
- [ ] External-tool ERD link + export
- [ ] External-tool UI prototype link + export
- [ ] Each artifact includes short rationale text (why this design choice)
- [ ] Artifacts are consistent with implemented code paths
