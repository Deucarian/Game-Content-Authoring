# Deucarian Game Content Authoring Agent Notes

Package ID: `com.deucarian.game-content-authoring`
Repository: `Deucarian/Game-Content-Authoring`

Follow the canonical Deucarian governance docs in [Package Registry](https://github.com/Deucarian/Package-Registry/blob/develop/ARCHITECTURE.md), especially capability ownership and dependency rules.

## Ownership

This package owns:

- Shared Game Content Authoring editor window, provider discovery/registration, validation display, asset path helpers, preview/result UI primitives, and create-result UI.

Registered capabilities:
- `game-content-authoring`

This package must not own:

- Attack, enemy, wave, tower, weapon, upgrade, loot, ability, projectile, combat, template, or product-specific gameplay logic.
- Runtime content definitions owned by gameplay/domain packages.
- Package install/update/remove behavior.

## Dependencies

Allowed dependency shape:

- Editor-only package. Runtime assemblies should not depend on this package.
- May depend on Common, Editor, and Gameplay Foundation for shared editor helpers and validation report models.

Required dependencies and why:

- `com.deucarian.common`: shared primitives used by authoring helpers.
- `com.deucarian.editor`: shared editor shell/resources.
- `com.deucarian.gameplay-foundation`: shared content validation report model displayed by the authoring UI.

Optional/version-defined dependencies:

- None.

Architecture exceptions:

- `Editor/GameContentAuthoringObjectPreview.cs` may use direct Unity object destruction for HideAndDontSave transient preview clones/colliders during editor preview cleanup.

## Policies

- Editor UI: Use shared Editor shell/resources; do not create a parallel editor framework.
- Gameplay boundaries: provider packages own their domain assets, validation, and creation logic.
- Runtime: Keep runtime assemblies free of references to this editor-only package.
- Logging: Do not introduce direct Unity Debug calls.
- Unity object lifetime: Keep direct destruction limited to the editor preview cleanup file listed in `deucarian-package.json`.
- Testing: Keep provider-registration, validation display, path helper, and authoring UI behavior covered by EditMode tests.

## Validation

Run the shared validator before committing:

```powershell
python C:/Repositories/Package-Registry/Tools/deucarian_package_validator.py --registry-root C:/Repositories/Package-Registry --repository-root . --config deucarian-package.json
```

Also run existing repository tests when changing code or asmdefs. Documentation-only updates should still run `git diff --check`.

## Codex Guidance

- Inspect current files before changing anything.
- Work on `develop`; do not edit or merge `main` unless the task is promotion-only.
- Do not edit `Library/PackageCache`.
- Do not guess package versions or dependency versions.
- Do not add package dependencies casually; update asmdefs, `package.json`, `deucarian-package.json`, Package Registry, and fallback catalogs together when a dependency is truly required.
- Do not create local copies of shared helpers.
- Keep commits focused and report exactly what changed and what was validated.
