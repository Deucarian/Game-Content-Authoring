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

GCA owns the editor transaction shell: availability checks, lifecycle and validation state, one active session per opaque physical-source lock key, scalar and canonical-reference controls, Undo/Redo dispatch, change review, stale/conflict/recovery presentation, commit/cancel/rollback orchestration, exception containment, and refresh notifications. The provider owns canonical-record-to-source mapping, the original snapshot and revision, editable field descriptors, proposed-value and whole-source validation, stale detection, persistence, rollback, durable recovery, and descriptor reindexing.

The generic field model supports provider-approved strings, integers, floating-point numbers, booleans, enum tokens, and explicitly described one-to-one `RecordReference` values. Stable IDs, pack/source/canonical IDs, provider-native reference tokens, unrestricted Unity object selection, assets, lists, arrays, dictionaries, nested structural changes, and computed fields remain read-only. Errors block commit. Warnings remain reviewable and require explicit confirmation before commit.

The lifecycle is `Clean` to `Dirty`, followed by `Committed` or `RolledBack`; stale sources and incompatible ownership enter `Stale` or `Conflict`, and an ambiguous commit/rollback enters `RecoveryRequired`. `Committing` disables mutation and cancellation. Disposal never commits. Closing the window or reloading assemblies rolls back and discards ordinary uncommitted state; providers are responsible for any durable recovery record required after a failed persistence operation.

A canonical record shown through several lenses attaches to the same source session, so its staged values, commit, and cancellation are shared. A different record backed by the same physical source is blocked in this first milestone. `All Packs` never attaches to an editable session and remains read-only. Existing Project Content scanning, creation, and provider-specific ScriptableObject editors remain on their current paths, including source-claim exclusion; no migration is required.

The generic source contract exposes an opaque provider token, source label, project-relative description, and lock identity, not a writable absolute path. Future production backends must validate declared project-owned roots and reject installed package sources, `PackageCache`, `Samples~`, `Library`, `Temp`, traversal, and symlink or reparse-point escapes.

## Canonical Record References

`GameContentFieldType.RecordReference` carries a `GameContentRecordReferenceValue`, never a provider-native string or Unity object. A reference is `None`, `Resolved`, or `Broken`. A resolved value stores the target's immutable `GameContentRecordKey`; optional display metadata does not participate in identity. A broken value preserves the provider's original display information and an actionable reason without choosing a fallback.

A reference descriptor declares its target label, required capabilities, required or nullable behavior, clear behavior, runtime impact, and pack policy. Milestone 2D1 supports only `SameSelectedPack`. `All Packs`, cross-owner keys, cross-pack keys, missing records, records with blocking validation, and targets that lose required capabilities are rejected. Stable record and source IDs remain immutable.

GCA enumerates and deterministically sorts records from the selected pack, performs generic owner, pack, capability, and validation checks, then calls the active session's optional `IGameContentRecordReferenceEditSession` contract. The provider re-resolves the canonical key against its fresh authoritative source and owns source claims, native type checks, domain compatibility, serialization, rollback, and reindexing. Scalar-only sessions remain compatible and do not implement this extension.

The existing workbench renders a searchable canonical-record selector rather than a free-form ID or unrestricted object field. Required broken references stay visible until the user selects a valid target; nullable fields may expose an explicit `None`. Change review shows old and new targets, owning pack, capabilities, validation, predicted inbound-reference deltas, source inbound count, and declared refresh/rebind/restart impact. Selecting a target never rewrites inbound references.

Reference targets are re-resolved during Apply, Preview, and immediately before Commit. A disappeared, stale, reclassified, multiply claimed, invalid, or provider-rejected target blocks Commit without substitution. The source transaction still locks and writes only the selected physical source; the target remains read-only and no multi-source transaction is opened.

## Safe-Editing Roadmap

Milestone 2A provides only the generic transaction contract, workbench, coordination, and an EditMode-only in-memory proof backend. No production pack is writable yet.

- 2B: limited Survivors JSON scalar editing with source hashes, minimally destructive patching, full-pack validation, atomic replacement, backup/recovery, and reindexing.
- 2C: limited Idle Auto Defense ScriptableObject scalar editing with `SerializedObject`, Unity Undo, validation, save/refresh, and source-claim preservation.
- 2D1: same-pack one-to-one canonical-record reference editing with provider-owned native mapping.
- Later 2D milestones: reference collections and other specialized complex fields.
- Later: create, duplicate, delete, and content-pack cloning workflows.
