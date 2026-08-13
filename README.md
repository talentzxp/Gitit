# GitIt v0.0.6

GitIt is a local-first, evidence-led Office Document Lineage Engine. It does not invent a history that an Office package did not preserve: direct parent edges require corroboration; content-only copies remain **related but unproven**; timestamps are supporting or conflicting evidence, never a hard ordering rule.

## Commands

```powershell
dotnet restore GitIt.sln --locked-mode
dotnet test GitIt.sln

dotnet run --project src/GitIt.Cli -- analyze C:\project-folder
dotnet run --project src/GitIt.Cli -- lineage C:\project-folder
dotnet run --project src/GitIt.Cli -- people C:\project-folder
dotnet run --project src/GitIt.Cli -- explain C:\project-folder version.docx
dotnet run --project src/GitIt.Cli -- diff C:\file-a.xlsx C:\file-b.xlsx
dotnet run --project src/GitIt.Cli -- corpus validate real-world-corpus --json

dotnet run --project src/GitIt.Benchmarks
dotnet run --project src/GitIt.Benchmarks -- demo
dotnet run --project src/GitIt.Desktop
```

All CLI commands accept `--json`. The stable renderer contract is [GitIt Analysis Result v1](docs/analysis-result-v1.schema.json); future UIs consume it without recomputing lineage.

`gitit explain <folder> <version-or-file> [--json]` gives the right-panel-ready explanation for one version: selected parent, alternatives, evidence, conflicts, and participation evidence. Run the synthetic benchmark runner with `dotnet run --project src/GitIt.Benchmarks`; it produces `outputs/benchmark-report.json`, `outputs/benchmark-report.md`, and `outputs/REVIEW_SUMMARY_v0.0.4.md`. After manually preparing authorized files, run `dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus` to generate `outputs/REAL_CORPUS_REVIEW.md`.

`GitIt.Desktop` is a Windows-only WPF visual test harness. It imports a folder through the same Core engine and presents human-readable family names, a version lineage view, a timeline, evidence, semantic Diff, participants, warnings, and technical provenance. It does not recalculate Core scores or infer editing environments. Run `dotnet run --project src/GitIt.Benchmarks -- demo` to create an isolated synthetic GUI demonstration under `demo`; it is not Real Corpus Gate evidence.

## Evidence model

Lineage scoring is deliberately decomposed. Each candidate carries independent evidence for content similarity, structure similarity, style similarity, RSID continuity, revision continuity, metadata, timestamps, filenames, and content containment. Scores and thresholds live in `LineageWeights`; every contribution has a strength:

- `Strong`: RSID/session continuity or corroborated tracked-change evidence.
- `Medium`: structural continuity and Office metadata.
- `Weak`: filename and timestamp support.
- `Conflicting`: for example, a child timestamp earlier than its candidate parent.
- `ParticipationOnly`: comment authorship. It proves review participation, not an edit.

The current engine asserts a `Probable` or `Possible` parent only with provenance or strongly corroborated content. Otherwise it reports `RelatedButUnproven` or leaves a document unlinked. Creator and LastModifiedBy values are Office strings, not authenticated real identities.

## Supported

- Scan DOCX, XLSX, and PPTX, including SHA-256 duplicate detection.
- DOCX: body paragraphs, style fingerprints, tables, RSIDs, tracked revisions, revision authors, and comment authors.
- XLSX semantic diff: sheets added/removed/reordered; cell value/formula/type/style changes; merged ranges; hidden rows/columns; row height and column width. Changes are addressed as `Sheet2!F27`.
- PPTX semantic diff: slides added/removed; text changes; basic shapes added/removed; position, size, font, size, bold, and color signatures; layout/theme reference changes.
- Participant evidence: common metadata, Word revisions/comments, spreadsheet comments, and PowerPoint comment authors where present.
- Explainable two-stage candidate retrieval: RSIDs, rare content tokens, filename stems, metadata hints, and coarse structure select a configurable Top-K before deep scoring.
- `GitIt.GroundTruth` produces a known, dirty Office history; `GitIt.Benchmarks` compares inferred edges with the hidden answer and records candidate and scaling metrics at 10, 50, 100, 500, 1000, and 2000 files.

## Partially supported

- XLSX/PPTX lineage uses the generic evidence model; their deep semantic diff is stronger than their provenance evidence in this release.
- XLSX/PPTX comments are surfaced when their standard Open XML parts are present. Threaded-person identity is not resolved yet.
- PPTX slide reorder detection is positional; duplicate or heavily redesigned slides can remain uncertain.
- DOCX diff aligns body paragraphs by order, so large section moves are reported as content/structure change rather than an exact move operation.

## Not supported

- VBA, PowerQuery, PivotTable deep semantics, advanced charts, external connections, animations, SmartArt, embedded OLE, media timelines, headers/footers/text boxes, and full revision-timeline reconstruction. Detected package parts are reported as partially analyzed rather than silently ignored.
- Cloud storage, accounts, LLM-based lineage guessing, or identity inference. The current WPF application is an explicitly bounded test harness, not a production GUI.

## Benchmark cases

The generated corpus includes a 20-version chain, branches, filename pollution, inconsistent Office/filesystem timestamps, exact duplicates in separate folders, small DOCX/XLSX/PPTX changes, metadata loss, a copy/paste reconstruction, and an unrelated document. It explicitly reports family detection, parent edge precision/recall, exact-parent and branch accuracy, duplicate accuracy, abstention rate, and false confident prediction rate.

The most error-prone real-world case remains a **content-preserving reconstruction after metadata and RSID loss**: it may clearly belong to the same family, but the engine should not claim its exact parent. A long chain that has been repeatedly rebuilt or saved by different Office applications has the same limitation.

## Reality corpus status

[real-world-corpus](real-world-corpus) is an empty, authorized-file-only corpus interface with a manifest whose answers are loaded only after analysis. Run `gitit corpus validate real-world-corpus` before using it, then `dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus` for the gate review. The first-batch manual procedure is in [real-world-corpus/README.md](real-world-corpus/README.md); it covers Word/WPS/WeChat, branch, destructive, XLSX, and PPTX flows. Current evidence is **synthetic robustness and template-sibling validation**, not a claim that real Word/WPS flows have passed.

## Layout

```text
src/GitIt.Core          parser, semantic diff, evidence model, lineage engine, JSON result
src/GitIt.Cli           analyze / lineage / people / explain / diff / corpus validate
src/GitIt.GroundTruth   reproducible dirty-history generator with hidden answers
src/GitIt.Benchmarks    accuracy and performance benchmark runner
src/GitIt.Desktop       minimal WPF Core visual test harness
tests/GitIt.Tests       parser, semantic diff, abstention, duplicate, and JSON contract tests
docs/                   stable Analysis Result v1 JSON Schema
```
