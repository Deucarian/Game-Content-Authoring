# Pack-Aware Game Content Authoring

## Ownership Model

Every authored record is displayed inside a content pack. The pack is the ownership and persistence boundary; Attack, Enemy, Wave / Encounter, Weapon / Tower, Upgrade, and All Content are reusable lenses over records in that boundary.

A pack provider owns source discovery, parsing or projection, validation, source reveal, references, and future persistence. A domain package owns common field semantics, domain validation presentation, previews, and authoring UX. A game or template package owns its schema, concrete data, balance, and adapters into domain projection contracts.

`GameContentRecordKey` provides canonical pack-scoped identity. Its stable key includes the owning package, pack, optional source, and source record ID. Semantic categories and lenses never mint a second identity for the same source record. Cross-pack references must carry an explicit target key or owner/pack pair.

## Backends

- Imported or game-owned JSON packs normally expose read, validate, and reveal-source capabilities. They do not expose editing or record creation in this milestone.
- `Project Content` is a synthetic pack over the existing `Assets/GameContent` ScriptableObject scan. Existing domain create/edit flows remain available there.
- Provider-generated named packs may project an existing ScriptableObject graph without creating a `GameContentPackManifest`. The provider remains responsible for discovery, records, validation, and actions.
- `All Packs` aggregates records for read-only search and comparison.
- Missing, invalid, and conflicted sources remain inspectable but cannot become writable by accident.

The global selector stores only stable pack identity in Unity `SessionState`. It survives window and assembly rebuilds within the project session without leaking an absolute path or selection into another Unity project.

## Provider-Owned Source Claims

A named pack whose source is already discoverable by `Project Content` implements `IGameContentSourceClaimProvider`. Each `GameContentSourceClaim` identifies an asset file through a stable `GameContentSourceIdentity`; Unity assets use their AssetDatabase GUID rather than an absolute filesystem path.

During catalog construction, claimed source records are removed from the synthetic `Project Content` projection. Their files and Unity ownership are not moved, rewritten, or transferred. Unclaimed ScriptableObjects retain the existing writable Project Content workflow. If the named provider is unavailable or reports a missing source, it contributes no active claims, so otherwise discoverable project records cannot remain hidden by stale ownership state.

Claims are pack-provider data, not a second registry. If two named packs claim the same source identity, GCA reports a source-claim conflict on both packs and Project Content. The conflicted source is excluded from writable Project Content, and GCA does not silently select a claimant. `All Packs` contains one named canonical record for a normal claim instead of a named record plus a synthetic duplicate.

## Register A Domain Lens

An editor-only provider implements both the existing provider contract and `IGameContentAuthoringLensProvider`:

```csharp
public GameContentLensDescriptor Lens { get; } = new GameContentLensDescriptor(
    "example-domain",
    "Example Domain",
    "Gameplay",
    "example-domain",
    400,
    new[] { ExampleCapabilities.Example });
```

Register it through `GameContentAuthoringProviderRegistry`. The existing registry rejects duplicate provider and lens IDs. In a custom surface, branch on `context.PackContext.IsProjectContent`: preserve the established ScriptableObject workflow for Project Content and render an immutable pack-aware browser for external packs. Never maintain a second list of domain records.

## Provide A Game Projection

Domain packages publish immutable projection types such as an Attack or Upgrade projection. A template package implements `IGameContentRecordProjectionAdapter<TProjection>` and registers it with `GameContentRecordProjectionRegistry<TProjection>` from an Editor assembly.

The adapter should:

1. Accept only records owned by that template and carrying the required capability.
2. Project common domain fields from the template-owned index.
3. Preserve `record.CanonicalKey` and canonical reference keys.
4. Keep game-specific data in template-owned metadata or extension presentation.
5. Never copy source records or write to the game source while browsing.

The generic package must not reference the domain package or parse the template's JSON. The domain package must not reference the template.

## Future Template Checklist

For another game template:

1. Register one or more explicit content packs through the existing provider registry.
2. Emit immutable record descriptors with canonical owner/pack/source/record keys.
3. Assign only capabilities that the record actually supports; a Tower need not also be a Weapon.
4. Emit pack-safe outbound and inbound references.
5. Register typed adapters for the installed domain lenses.
6. Keep template-specific schemas, fields, balance, scenes, and runtime local.
7. Reuse Project Content for unclaimed standalone ScriptableObject workflows and claim named-pack sources explicitly.
8. Do not duplicate a record to make it visible in another lens.
9. Do not enable create/edit actions unless the selected backend explicitly supports them.

## Transactional Editing Foundation

A named-pack provider opts in by implementing `IGameContentPackEditProvider` on the same instance already registered through `GameContentAuthoringProviderRegistry`. There is no edit-backend registry and no universal serializer. Providers without the optional interface remain read-only and keep their existing behavior.

GCA owns the editor transaction shell: availability checks, lifecycle and validation state, one active session per opaque physical-source lock key, scalar, canonical-reference, and ordered-collection controls, Undo/Redo dispatch, change review, stale/conflict/recovery presentation, commit/cancel/rollback orchestration, exception containment, and refresh notifications. The provider owns canonical-record-to-source mapping, the original snapshot and revision, editable field descriptors, provider-native collection locators, proposed-value and whole-source validation, stale detection, persistence, rollback, durable recovery, and descriptor reindexing.

The generic field model supports provider-approved strings, integers, floating-point numbers, booleans, enum tokens, explicitly described one-to-one `RecordReference` values, flat ordered scalar/reference collections, and the ordered structured embedded rows described below. Stable IDs, pack/source/canonical IDs, provider-native reference tokens, unrestricted Unity object selection, assets, dictionaries, nested collections, polymorphic items, and computed fields remain read-only. Errors block commit. Warnings remain reviewable and require explicit confirmation before commit.

The lifecycle is `Clean` to `Dirty`, followed by `Committed` or `RolledBack`; stale sources and incompatible ownership enter `Stale` or `Conflict`, and an ambiguous commit/rollback enters `RecoveryRequired`. `Committing` disables mutation and cancellation. Disposal never commits. Closing the window or reloading assemblies rolls back and discards ordinary uncommitted state; providers are responsible for any durable recovery record required after a failed persistence operation.

A canonical record shown through several lenses attaches to the same source session, so its staged values, commit, and cancellation are shared. A different record backed by the same physical source is blocked in this first milestone. `All Packs` never attaches to an editable session and remains read-only. Existing Project Content scanning, creation, and provider-specific ScriptableObject editors remain on their current paths, including source-claim exclusion; no migration is required.

The generic source contract exposes an opaque provider token, source label, project-relative description, and lock identity, not a writable absolute path. Future production backends must validate declared project-owned roots and reject installed package sources, `PackageCache`, `Samples~`, `Library`, `Temp`, traversal, and symlink or reparse-point escapes.

## Canonical Record References

`GameContentFieldType.RecordReference` carries a `GameContentRecordReferenceValue`, never a provider-native string or Unity object. A reference is `None`, `Resolved`, or `Broken`. A resolved value stores the target's immutable `GameContentRecordKey`; optional display metadata does not participate in identity. A broken value preserves the provider's original display information and an actionable reason without choosing a fallback.

A reference descriptor declares its target label, required capabilities, required or nullable behavior, clear behavior, runtime impact, and pack policy. Milestone 2D1 supports only `SameSelectedPack`. `All Packs`, cross-owner keys, cross-pack keys, missing records, records with blocking validation, and targets that lose required capabilities are rejected. Stable record and source IDs remain immutable.

GCA enumerates and deterministically sorts records from the selected pack, performs generic owner, pack, capability, and validation checks, then calls the active session's optional `IGameContentRecordReferenceEditSession` contract. The provider re-resolves the canonical key against its fresh authoritative source and owns source claims, native type checks, domain compatibility, serialization, rollback, and reindexing. Scalar-only sessions remain compatible and do not implement this extension.

The existing workbench renders a searchable canonical-record selector rather than a free-form ID or unrestricted object field. Required broken references stay visible until the user selects a valid target; nullable fields may expose an explicit `None`. Change review shows old and new targets, owning pack, capabilities, validation, predicted inbound-reference deltas, source inbound count, and declared refresh/rebind/restart impact. Selecting a target never rewrites inbound references.

Reference targets are re-resolved during Apply, Preview, and immediately before Commit. A disappeared, stale, reclassified, multiply claimed, invalid, or provider-rejected target blocks Commit without substitution. The source transaction still locks and writes only the selected physical source; the target remains read-only and no multi-source transaction is opened.

## Ordered Collections

`GameContentFieldType.OrderedScalarCollection` and `GameContentFieldType.OrderedRecordReferenceCollection` add homogeneous, non-null ordered values to the generic field model. Scalar items use the existing string, integer, floating-point, boolean, or enum-token value kinds. Reference items use canonical `GameContentRecordReferenceValue` values and the same same-selected-pack, capability, validation, and fresh-target evaluation rules as one-to-one references. Minimum count zero permits an empty collection; descriptors may also declare a maximum count, duplicate policy, ordering description, and runtime-impact hint.

Each staged item has an opaque `GameContentCollectionItemKey`. An Add operation carries only the value, so the active session creates the key; callers cannot supply one. Keys survive reorder and in-session Undo/Redo so equal values remain independently addressable, but they are discarded when the source is reloaded and never become persisted identity. Persisted collection equality compares the ordered item values, not session keys or original indexes.

Providers opt in through the additive `IGameContentOrderedCollectionEditSession` contract. Existing scalar and one-reference providers remain source compatible and collections stay read-only when that optional contract is absent. GCA enforces generic item type, count, duplicate, key, pack, capability, and canonical-target rules. The provider still owns native element mapping, provider-specific validation, serialization, atomicity, rollback, recovery, and reindexing. Invalid staged provider states may remain visible for correction, but Preview and Commit report them and Commit is blocked.

Collection edits use Add, Remove, Move, and Replace operations in the same source transaction and Undo/Redo history as scalar and one-reference edits. Restore Original Order is a deterministic sequence of Move operations for surviving original items, with newly added items kept after them. Referenced records are re-resolved but never locked or modified: removing a reference removes only the collection entry and does not delete its target. These element operations do not add generic create, duplicate, or delete support for canonical records, assets, packs, or physical sources; those CRUD workflows remain outside this milestone. Drag-and-drop, nested collections, maps, cross-pack references, bulk editing, and multi-source transactions are also deferred.

## Structured Embedded Rows

`GameContentFieldType.OrderedStructuredCollection` represents an ordered sequence of child values serialized inside one existing physical source and owned entirely by one parent source record. Each row uses a stable provider-whitelisted schema and deterministic field descriptors. Initially supported child fields are string, integer, finite floating-point number, boolean, enum token, and one-to-one canonical `RecordReference`. Nested collections, nested structured rows, arrays, maps, dictionaries, polymorphic or managed-reference values, Unity objects, assets, raw paths, free-form IDs, and stable-ID editing are rejected.

This boundary is semantic, not merely structural:

- A flat ordered item is one scalar or one canonical reference.
- A structured embedded row is several fields forming one child value whose full lifetime belongs to the parent source record.
- A nested authored record has stable canonical, save-data, inbound-reference, source-ownership, or independent pack identity; adding or removing it is record CRUD.
- A top-level source record exists in a root collection or independent source asset; adding or removing it is record CRUD.

Providers must explicitly declare if a proposed child represents independent canonical identity, and GCA rejects that schema with a CRUD boundary error. Adding a structured row never creates a top-level authored record. Removing one never deletes a source record or referenced target. Stable identities remain immutable.

Each source session assigns every row an opaque `GameContentStructuredRowKey`, including initial rows with identical persisted content. `AddRow` accepts field values only; the coordinator generates its key. Keys remain stable through Move, field replacement, Undo, and Redo, allowing duplicate rows to remain independently addressable. They are discarded on source reload or a new session, excluded from persisted equality, and must never be serialized as domain identity. A provider may separately declare an immutable native key; GCA displays it read-only, validates uniqueness when present, and never assumes it is a canonical record ID.

`IGameContentStructuredCollectionEditSession` is an optional extension on the existing edit session. It applies `AddRow`, `RemoveRow`, `MoveRow`, `ReplaceRowField`, and `RestoreOriginalOrder`, and evaluates row reference fields against fresh provider state. Existing sessions remain compatible when they do not expose the new field type. No second backend registry exists.

Structured operations share the exact mixed session history with scalar, one-reference, and flat collection operations. A new operation after Undo clears the Redo branch. Restore Original Order places surviving original rows by session-start index, keeps new rows afterward in their current relative order, and does not recreate removed rows; Undo may restore them. All operations preserve the one-session-per-physical-source lock and stale/conflict/recovery lifecycle. Referenced targets remain read-only and are never locked.

The existing GCA workbench renders a row list with index, summary, optional native key, validation state, Add/Remove, Move Up/Down, and Restore Original Order controls. Its selected-row detail uses the existing scalar, enum, and canonical-reference controls with field help and findings. Change review shows original/proposed order, added/removed/moved rows, changed child fields, old/new values, reference targets, validation findings, and refresh/rebind/restart hints. Deterministic buttons are used; drag-and-drop remains deferred.

GCA enforces only generic structure: schema, supported fields, required values, scalar constraints, enum tokens, count limits, duplicate policy, operation permissions, valid session keys, unique declared native keys, canonical reference validity, and same-selected-pack policy. Providers own domain compatibility, gameplay and ordering semantics, cross-field relationships, cycles, strict startup rules, source serialization, whole-pack validation, and durable recovery. Production implementations must whitelist row schemas and child fields, enforce project-owned source roots, reject unsafe native values, validate immediately before persistence, and fail closed.

References are re-resolved on AddRow, ReplaceRowField, Undo, Redo, Preview, and immediately before Commit. A source revision change invalidates the complete session; keys are not merged or remapped. Missing targets, lost capabilities, changed source claims, disappeared packs/providers, and broken references remain visible and block Commit until repaired or legitimately cleared when nullable.

This foundation is proven only by a private EditMode in-memory provider with no production files or assets. No production pack becomes structured-row writable. `All Packs` remains read-only, claimed named-pack sources remain excluded from writable Project Content, and existing Project Content scanning, creation, and provider-owned editing do not route through structured-row transactions.

## Safe-Editing Roadmap

Milestone 2A provides only the generic transaction contract, workbench, coordination, and an EditMode-only in-memory proof backend. No production pack is writable yet.

- 2B: limited Survivors JSON scalar editing with source hashes, minimally destructive patching, full-pack validation, atomic replacement, backup/recovery, and reindexing.
- 2C: limited Idle Auto Defense ScriptableObject scalar editing with `SerializedObject`, Unity Undo, validation, save/refresh, and source-claim preservation.
- 2D1: same-pack one-to-one canonical-record reference editing with provider-owned native mapping.
- 2D2A: generic ordered scalar and same-pack canonical-reference collection contracts, coordination, workbench controls, and an EditMode-only in-memory proof backend.
- 2D2B: generic structured embedded-row contracts, coordination, workbench controls, and an EditMode-only in-memory proof backend.
- Next: Survivors `upgrades[*].effects` production structured-row editing.
- Then: resolve Idle wave-entry identity before exposing Idle wave-entry rows.
- Later: multi-source transactions, record CRUD, and pack cloning, in that order.
