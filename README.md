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

## Gameplay Foundation Validation Reports

Provider editor code can pass a `Deucarian.GameplayFoundation.ContentValidationReport` to `GameContentAuthoringValidationReports.ToAuthoringResult(...)` or to `GameContentAuthoringContext.DrawValidation(...)` / `GameContentAuthoringPreviewContext.DrawValidation(...)`.

The adapter preserves severity, path, and message information, groups Markdown output by severity, and keeps gameplay-specific validation rules inside the provider or template that owns that content.
