# Deucarian Game Content Authoring

Current package version: 0.1.1

Shared editor shell for Deucarian game content authoring providers.

This package owns the `Tools/Deucarian/Tools and Quality/Game Content Authoring` window, provider discovery, the selected content-pack context, canonical record identity, lens orchestration, shared validation display, provider-owned edit-session orchestration, shared asset path helpers, rich preview panel primitives, and common create-result UI. Gameplay content types remain in their domain packages.

Provider packages implement `IGameContentAuthoringProvider` in an Editor assembly and register with `GameContentAuthoringProviderRegistry`.

## What This Package Owns

- Main editor window and `Tools/Deucarian/Tools and Quality/Game Content Authoring` menu item.
- Provider selector and empty state when no provider packages are installed.
- Shared validation/result models for authoring flows.
- Generic adapters for displaying Gameplay Foundation `ContentValidationReport` results in editor-only authoring UI.
- Shared asset path, folder, duplicate ID, and overwrite helpers.
- Shared preview/result/create button UI behavior.
- Shared preview context helpers for thumbnails, timeline rows, warnings, status text, and preview buttons.
- Read-only content-pack manifests, discovery, descriptors, provider actions, and browser UI.
- Persistent per-project-session pack selection, `All Packs`, and the synthetic writable `Project Content` pack.
- Canonical `(owningPackageId, packId, sourceId, sourceRecordId)` record keys and pack-safe reference resolution.
- Typed capability and projection-adapter contracts for reusable domain lenses.
- Unified Pack Dashboard and All Content views with search, capability/source/validation filters, sorting, source reveal, and cross-lens navigation.
- Optional named-pack edit-provider contracts, one-session-per-source locking, scalar, canonical-reference, flat ordered-collection, and structured-row controls, change review, validation gating, and recovery presentation.
- Installed provider diagnostics list.

## What This Package Does Not Own

This package does not contain attack, enemy, wave, tower, upgrade, loot, ability, projectile, combat, or template-specific gameplay logic. Provider packages keep their domain data, validation, and asset creation logic in their own editor/runtime assemblies.

## Pack-Aware Provider Setup

1. Add `com.deucarian.game-content-authoring` as a dependency of the provider package.
2. Reference `Deucarian.GameContentAuthoring.Editor` from the provider package's Editor asmdef only.
3. Implement `IGameContentAuthoringProvider`; implement `IGameContentAuthoringLensProvider` when the provider is a reusable record view.
4. Describe the lens with a stable ID, group, ordering, and typed `GameContentRecordCapability` values.
5. Use `GameContentAuthoringSurfaceContext.PackContext` and `PackRecords` rather than maintaining a second content store.
6. Keep an existing ScriptableObject create/edit surface for `Project Content`. A named pack may additionally implement `IGameContentPackEditProvider` when it owns a safe transaction backend.
7. Register the provider during editor load with `GameContentAuthoringProviderRegistry.Register(...)`.
8. Keep runtime assemblies free of references to this package.

## Content Packs And Lenses

All displayed content now has an explicit pack context. Providers that own source discovery implement `IGameContentPackProvider` and continue to register through `GameContentAuthoringProviderRegistry`; no second window or registry is involved. A pack provider owns parsing, validation, source location, and future persistence. A domain package owns its lens fields, preview, and reusable authoring UX.

`GameContentPackManifest` is an editor-only discovery asset. It stores generic pack metadata, an optional playable scene and presentation references, and `TextAsset` source references. It must not duplicate domain records. Imported sample manifests are discovered through `AssetDatabase`; duplicate `(owningPackageId, packId)` keys are reported as blocking conflicts rather than resolved silently.

The global selector includes discovered packs, `Project Content`, and read-only `All Packs`. Selection uses stable package/pack identity, survives lens switches and assembly reloads through Unity `SessionState`, and never persists an absolute asset path. `Project Content` projects the existing `Assets/GameContent` ScriptableObject scan into the same model while preserving its established create/edit surfaces.

Records advertise one or more typed capabilities. A single weapon can therefore appear in both Weapon and Attack lenses while retaining one `GameContentRecordKey`. Domain projections use `GameContentRecordProjectionRegistry<TProjection>`; template packages register typed adapters without making domain packages depend on a game template. The generic package deliberately does not parse game-specific JSON.

Creation requires a selected backend whose access descriptor reports `CanCreate`. Current JSON packs and `All Packs` do not expose enabled creation or editing, and a missing pack context is not a valid creation target.

This package ships no game-specific persistence backend. Named packs remain read-only unless their owning provider package explicitly supplies a safe transaction implementation; GCA itself never parses or serializes template-owned content.

## Safe Named-Pack Editing Foundation

Named-pack providers opt in through `IGameContentPackEditProvider`; the existing provider instance and provider ID remain the backend identity. GCA coordinates lifecycle, source locking, typed scalar, canonical-reference, and ordered-collection controls, change review, exception containment, stale/conflict display, commit/cancel/rollback actions, and refresh notifications. The provider maps records to sources and owns snapshots, revisions, field exposure, validation, persistence, rollback, recovery, and reindexing. GCA never serializes domain content.

The additive field model supports provider-approved string, integer, floating-point, boolean, enum-token, one-to-one canonical-record reference, ordered scalar collection, ordered canonical-reference collection, and ordered structured embedded-row fields. Existing providers remain compatible and expose only the optional contracts they implement. Stable IDs, pack/source/canonical IDs, provider-native reference tokens, asset and Unity object references, arbitrary nested values, and computed values remain read-only. Invalid staged values may be reviewed but errors block commit; warnings require explicit confirmation.

One active transaction owns a physical source lock. The same canonical record reached through another compatible lens attaches to that session, while a different record sharing the source is blocked until the first session finishes. `All Packs` is always read-only. Existing `Project Content` scanning, creation, and provider-specific ScriptableObject editors are unchanged and do not route through this coordinator.

Uncommitted sessions are rolled back and discarded when the window closes or assemblies reload; sessions never auto-commit. A provider-reported or exception-generated `RecoveryRequired` state stays locked for explicit review while the session is active, and the provider owns any durable recovery record. Future production backends must enforce project-owned source roots and reject writes to package sources, `PackageCache`, `Samples~`, `Library`, `Temp`, traversal targets, and symlink or reparse-point escapes.

### Structured Embedded Rows

`GameContentFieldType.OrderedStructuredCollection` models an ordered child value serialized inside one existing physical source and owned entirely by one parent source record. A row may contain deterministic string, integer, floating-point, boolean, enum-token, and one-to-one same-pack canonical `RecordReference` fields. It may not contain a nested collection, nested structured row, map, polymorphic value, Unity object, asset, raw path, raw/free-form ID, or mutable stable ID.

A structured row is not a hidden canonical record. It has no independent canonical ID, inbound references, save-data identity, source ownership, or pack projection. A child with any of those properties is a nested or top-level authored record, so adding or removing it is CRUD and is rejected by this API. Adding a structured row does not create a top-level record; removing one does not delete a record or any referenced target.

Every row receives an opaque session key. Initial rows with identical persisted values still receive distinct keys; Add callers provide fields only and the coordinator generates the key. Keys survive Move, field replacement, and in-session Undo/Redo, but are discarded on source reload and never become persisted identity. A provider may declare an immutable native row key for read-only display and uniqueness validation; GCA does not edit it or equate it with canonical identity.

Providers opt in through `IGameContentStructuredCollectionEditSession`. Add Row, Remove Row, Move Row, Replace Row Field, and Restore Original Order share the same history, source lock, stale/conflict/recovery behavior, Preview, Commit, Cancel, and Rollback controls as existing field kinds. The existing workbench renders a deterministic row list and selected-row detail panel, including scalar controls, canonical-reference selection, native-key metadata, field findings, row order, adds/removals/moves, old/new field values, reference targets, and runtime-impact hints. No second registry or editor window is introduced.

GCA owns generic schema, supported-type, required-field, count, duplicate, operation-permission, session-key, immutable-native-key, and same-pack canonical-reference checks. Providers still own schema whitelists, source roots, domain compatibility, cross-field/gameplay semantics, source serialization, whole-pack validation, atomicity, rollback, recovery, and refresh/reindex behavior. Production providers must fail closed and must not expose independently canonical children as structured rows.

This milestone is proven only by a private EditMode in-memory provider. No production pack becomes structured-row writable. `All Packs` stays read-only, and existing `Project Content` scanning, creation, and provider-owned editing remain unchanged.

The structured-editing roadmap is:

1. Survivors `upgrades[*].effects` production adapter.
2. Resolve Idle wave-entry identity.
3. Idle wave-entry structured rows.
4. Multi-source transactions.
5. Record CRUD.
6. Pack cloning.

See `Documentation~/pack-aware-authoring.md` for the ownership model, registration example, and future-template checklist.

## Gameplay Foundation Validation Reports

Provider editor code can pass a `Deucarian.GameplayFoundation.ContentValidationReport` to `GameContentAuthoringValidationReports.ToAuthoringResult(...)` or to `GameContentAuthoringContext.DrawValidation(...)` / `GameContentAuthoringPreviewContext.DrawValidation(...)`.

The adapter preserves severity, path, and message information, groups Markdown output by severity, and keeps gameplay-specific validation rules inside the provider or template that owns that content.

## Install

Stable:

```json
"com.deucarian.game-content-authoring": "https://github.com/Deucarian/Game-Content-Authoring.git#main"
```

Development:

```json
"com.deucarian.game-content-authoring": "https://github.com/Deucarian/Game-Content-Authoring.git#develop"
```

Use `#main` for stable package consumption and `#develop` when testing active package work.

## When To Use This

Use this package when you need Shared Deucarian editor shell and provider API for game content authoring packages.

Do not use this package to take ownership of capabilities outside its `AGENTS.md` boundary. Reusable behavior should stay with the package that owns that capability in the Package Registry governance docs.

## Quick Start

1. Install the package through Deucarian Package Installer or Unity Package Manager using the URL above.
2. Let Unity finish resolving packages and compiling assemblies.
3. Start from the package README sections above and the public runtime/editor APIs in this repository.

## Integrations

Direct Deucarian package dependencies:

- `com.deucarian.common`
- `com.deucarian.editor`
- `com.deucarian.gameplay-foundation`

Install optional companion packages only when their owned capability is needed by production code, samples, or tests.

## Troubleshooting

- Package does not resolve: confirm the stable or development Git URL matches the Package Registry entry and that required Deucarian dependencies are installed.
- Unity compile errors after install: let Package Manager finish resolving dependencies, then check asmdef references against `package.json` dependencies.
- Behavior appears to belong in another package: consult `AGENTS.md` and the Package Registry governance docs before moving or duplicating code.

## Validation

Run the shared package validator from this repository root:

```powershell
python C:/Repositories/Package-Registry/Tools/deucarian_package_validator.py --registry-root C:/Repositories/Package-Registry --repository-root . --config deucarian-package.json
```

Documentation-only updates should still pass:

```powershell
git diff --check
```

## License

MIT. See `LICENSE.md`.
