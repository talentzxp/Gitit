# GitIt Real Corpus Gate Review

## Corpus overview

- Gate status: WAITING FOR MANUAL FILE CREATION
- DOCX: 0 versions
- XLSX: 0 versions
- PPTX: 0 versions
- Families: 0; known parent edges: 0

## Environment

- No manual environment was recorded.

## Lineage results

- Correct: 0
- Wrong: 0
- Abstained: 0
- High-confidence wrong: 0

- No real lineage edge was evaluated.

## Diff validation

- No expected changes were available for review.

## Participant evidence

- No real participant evidence was observed.

### Transition survival

- No real transitions were compared.

## New real-world issues

- No authorized manual Office files are present.

## Current three most dangerous scenarios

1. Provenance stripped after content reconstruction or privacy cleanup.
2. Word/WPS cross-editor save rewriting OOXML traces.
3. Independently created files derived from a highly similar template.

## Core decision

Critical before Alpha: None established by this gate run.

Nice-to-have: richer expected-change annotations after the first manual run.

Known limitation: abstention is an allowed result when provenance is insufficient.

## Recommendation

Recommendation:

WAITING FOR MANUAL CORPUS

Manual action required:

MANUAL_ACTION_REQUIRED: personally create authorized Word/WPS/WeChat, Excel/WPS, and PowerPoint/WPS files following real-world-corpus/README.md, update corpus.json, validate it, then run `dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus`.
