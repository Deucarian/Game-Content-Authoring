# Changelog

## 0.2.1 - 2026-09-09

- Separate library validation, reachability and member access from discovery. Compose list/detail/graph views and scene/overlay preview rendering.
- Extract existing-item presentation and per-window selection ownership without changing provider contracts or game packages.

## 0.2.0 - Unreleased

- Compose edit-session ownership, validation, reference policies, transactions and focused field renderers. Keep per-window/session draft state and preserve public authoring contracts.
- Own each editor window's shared-session connection; closing one window no longer resets another window's edit session.

## 0.1.1 - 2026-07-17

- Applied the coordinated portfolio governance metadata and aligned exact Deucarian dependencies.
- Standardized editor-only provider session state, validation aggregation, record reference/list drawing, and preview summaries for reuse by content packages.

## Unreleased

- Registered Game Content Authoring with Deucarian Control Center and moved its sole global menu entry to `Tools/Deucarian/Authoring/Game Content...`.
- Added generic ordered structured embedded-row descriptors, opaque session identity, Add/Remove/Move/field-replace operations, mixed history, same-pack row-reference validation, review/workbench UI, and a test-only in-memory proof backend; no production pack becomes structured-row writable.
- Added canonical one-to-one `RecordReference` editing with None/Resolved/Broken values, same-pack capability-filtered selection, optional provider evaluation, target revalidation, inbound-impact review, and scalar-backend compatibility.
- Added optional provider-owned named-pack edit transactions with scalar field models, source locking, lifecycle/validation coordination, change review, stale/conflict/recovery handling, and an EditMode-only in-memory proof backend. Production packs remain read-only.
- Added one shared content-pack context with per-session selection, All Packs browsing, explicit backend capabilities, pack-safe canonical record identity, and a writable Project Content compatibility pack.
- Added reusable capability-driven lens discovery, immutable typed projection adapters, Pack Dashboard, unified All Content browsing, shared record selection, cross-lens navigation, source/status presentation, and guarded validation.
- Prevented creation without an explicit writable pack; current JSON packs and All Packs remain read-only pending provider-owned production backends.
- Added generic read-only content-pack manifests, discovery, provider descriptors, browser models, reference navigation, validation, and exception-safe provider actions.
- Added editor-only helpers for surfacing Gameplay Foundation `ContentValidationReport` results in authoring providers.
- Moved the Game Content Authoring menu to `Tools/Deucarian/Game Content Authoring`.
- Restyled the shared authoring shell with Deucarian Editor wallpaper, frosted cards, provider sidebar, styled validation cards, and a bottom status bar.

## 0.1.0

- Added shared Deucarian Game Content Authoring editor shell.
- Added minimal provider API and registration/discovery support.
- Added shared validation, path, duplicate ID, folder, and creation-result helpers.
