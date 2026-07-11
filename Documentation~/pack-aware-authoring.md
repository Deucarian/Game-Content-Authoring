# Pack-Aware Game Content Authoring

## Ownership Model

Every authored record is displayed inside a content pack. The pack is the ownership and persistence boundary; Attack, Enemy, Wave / Encounter, Weapon / Tower, Upgrade, and All Content are reusable lenses over records in that boundary.

A pack provider owns source discovery, parsing or projection, validation, source reveal, references, and future persistence. A domain package owns common field semantics, domain validation presentation, previews, and authoring UX. A game or template package owns its schema, concrete data, balance, and adapters into domain projection contracts.

`GameContentRecordKey` provides canonical pack-scoped identity. Its stable key includes the owning package, pack, optional source, and source record ID. Semantic categories and lenses never mint a second identity for the same source record. Cross-pack references must carry an explicit target key or owner/pack pair.

## Backends

- Imported or game-owned JSON packs normally expose read, validate, and reveal-source capabilities. They do not expose editing or record creation in this milestone.
- `Project Content` is a synthetic pack over the existing `Assets/GameContent` ScriptableObject scan. Existing domain create/edit flows remain available there.
- `All Packs` aggregates records for read-only search and comparison.
- Missing, invalid, and conflicted sources remain inspectable but cannot become writable by accident.

The global selector stores only stable pack identity in Unity `SessionState`. It survives window and assembly rebuilds within the project session without leaking an absolute path or selection into another Unity project.

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

For a future template such as Idle Auto Defense:

1. Register one or more explicit content packs through the existing provider registry.
2. Emit immutable record descriptors with canonical owner/pack/source/record keys.
3. Assign only capabilities that the record actually supports; a Tower need not also be a Weapon.
4. Emit pack-safe outbound and inbound references.
5. Register typed adapters for the installed domain lenses.
6. Keep template-specific schemas, fields, balance, scenes, and runtime local.
7. Reuse Project Content for standalone ScriptableObject workflows.
8. Do not duplicate a record to make it visible in another lens.
9. Do not enable create/edit actions unless the selected backend explicitly supports them.

## Safe-Editing Roadmap

JSON editing is intentionally deferred. A future writable JSON backend needs schema-aware edits, validation-before-commit, source hashes, atomic replacement, backups, Undo/recovery, and explicit create/duplicate/delete semantics. The current capability contract reserves those operations without implying that they are implemented.
