# Deucarian Game Content Authoring

Current package version: 0.1.0

Shared editor shell for Deucarian game content authoring providers.

This package owns the `Tools/Deucarian/Game Content Authoring` window, provider discovery, shared validation display, shared asset path helpers, rich preview panel primitives, and common create-result UI. Gameplay content types remain in their domain packages.

Provider packages implement `IGameContentAuthoringProvider` in an Editor assembly and register with `GameContentAuthoringProviderRegistry`.

## What This Package Owns

- Main editor window and `Tools/Deucarian/Game Content Authoring` menu item.
- Provider selector and empty state when no provider packages are installed.
- Shared validation/result models for authoring flows.
- Generic adapters for displaying Gameplay Foundation `ContentValidationReport` results in editor-only authoring UI.
- Shared asset path, folder, duplicate ID, and overwrite helpers.
- Shared preview/result/create button UI behavior.
- Shared preview context helpers for thumbnails, timeline rows, warnings, status text, and preview buttons.
- Read-only content-pack manifests, discovery, descriptors, provider actions, and browser UI.
- Installed provider diagnostics list.

## What This Package Does Not Own

This package does not contain attack, enemy, wave, tower, upgrade, loot, ability, projectile, combat, or template-specific gameplay logic. Provider packages keep their domain data, validation, and asset creation logic in their own editor/runtime assemblies.

## Provider Setup

1. Add `com.deucarian.game-content-authoring` as a dependency of the provider package.
2. Reference `Deucarian.GameContentAuthoring.Editor` from the provider package's Editor asmdef only.
3. Implement `IGameContentAuthoringProvider`.
4. Draw domain-specific preview content from `DrawPreview(...)`, and release any editor-only preview state from `StopPreview()`.
5. Register the provider during editor load with `GameContentAuthoringProviderRegistry.Register(...)`.
6. Keep runtime assemblies free of references to this package.

## Read-Only Content Packs

Providers that expose a named collection of authored records can additionally implement `IGameContentPackProvider`. They continue to register through `GameContentAuthoringProviderRegistry` and can render `GameContentPackBrowser` from the existing `IGameContentAuthoringSurfaceProvider` hook. No second window or registry is involved.

`GameContentPackManifest` is an editor-only discovery asset. It stores generic pack metadata, an optional playable scene and presentation references, and `TextAsset` source references. It must not duplicate domain records. Imported sample manifests are discovered through `AssetDatabase`; duplicate `(owningPackageId, packId)` keys are reported as blocking conflicts rather than resolved silently.

The generic browser consumes `GameContentPackDescriptor`, category and record descriptors, validation results, references, and provider-defined actions. Domain providers remain responsible for parsing their own source format, assigning categories, interpreting references, validating records, and executing actions. This package does not parse domain JSON.

This first content-pack milestone is read-only. It supports discovery, browsing, search, filtering, deterministic sorting, inspection, references, validation, source reveal, and provider actions. JSON editing, write-back, record creation, duplication, deletion, and content-pack cloning require a later transactional authoring contract.

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
