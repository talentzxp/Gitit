# GitIt Minimal GUI Test Harness Review

## Outcome

The WPF desktop harness builds and starts successfully. It is a local visual test tool for GitIt Core, not a production GUI and not evidence that the Real Corpus Gate has passed.

## Build and tests

- `dotnet build GitIt.sln --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test GitIt.sln --no-build`: 16 / 16 passed.
- Desktop startup smoke test: the WPF process remained running for three seconds when launched hidden, then was stopped by the test harness. No automated pixel-level visual inspection was performed.

## GUI implemented

- Folder selection and folder drag-and-drop.
- Background Core analysis with a visible scanning/analyzing status.
- Family list showing versions, duplicates, related-but-unproven items, and unlinked versions.
- DAG-oriented node-and-line lineage view; weaker edges are dashed, duplicate nodes are labelled, and related-but-unproven files are not converted into parent edges.
- Node details showing Core-selected parent, confidence, status, evidence, warnings, file hash, metadata, RSID count, and revision count.
- Edge details showing Core semantic-Diff categories and individual supported changes.
- Participant list using Core participant evidence only.
- Visible warnings and unsupported-content notices.

## Architecture boundary

`GitIt.Desktop` references `GitIt.Core`; Core does not reference the desktop project. The desktop adapter invokes Core scanner, analyzer, lineage, Diff, and people outputs. It does not reimplement lineage, confidence, family detection, participant inference, or semantic Diff.

## Known limitations

1. This is a WPF test harness for Windows, with no installer, cloud, synchronization, editing, telemetry, accounts, or product settings.
2. The graph is a deliberately simple automatic layout for inspection; it has no graph editing, zoom, or production-grade visual polish.
3. Visual startup was smoke-tested only. The next meaningful validation is to import manually created Word/WPS/WeChat, Excel/WPS, and PowerPoint/WPS files.

## Core changes

No lineage, scoring, parser, Diff, or participant-inference logic was changed. Only a thin desktop adapter and its ViewModel test coverage were added.

## Real Corpus status

`WAITING FOR MANUAL CORPUS`

The GUI completion does not change the Real Corpus Gate result.

## Recommendation

`READY FOR MANUAL GUI-ASSISTED REAL CORPUS TEST`
