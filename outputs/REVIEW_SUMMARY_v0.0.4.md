# GitIt v0.0.4 review summary

## One-line conclusion

GitIt v0.0.4 completed explainable two-stage candidate retrieval, but real Word/WPS/WeChat corpus remains unpopulated, so real-world validation is pending.

## What changed

- Added a deterministic Stage 1 candidate index using RSIDs, rare content tokens, filename stems, metadata hints, and coarse structure.
- Limited deep lineage scoring to configurable Top-K candidates and attached selection evidence to every scored candidate.
- Added a corpus manifest validator that checks file existence, format, IDs, parents, cycles, and identical-file warnings.
- Added a bounded incremental API for one newly added version.
- Added scaling and retrieval metrics separate from synthetic, template-sibling, and external-corpus results.

## Tests

`dotnet test`: 13 / 13 passed

## Synthetic metrics

- Family precision/recall proxy: 89.03%
- Parent precision: 100.00%; parent recall/exact parent: 91.30%
- Branch: 100.00%; duplicate: 100.00%; abstention: 100.00%
- False confident: 0.00%
- Template sibling false family/edge: 0.00% / 0.00%

## Format samples

- Docx: 27 versions; Synthetic only.
- Xlsx: 2 versions; Insufficient sample size; synthetic only.
- Pptx: 2 versions; Insufficient sample size; synthetic only.

## Candidate retrieval and performance

- 100 files: naive 9900, retrieved 1000, reduction 89.90%, total lineage 40.5 ms; cold 109.6 ms, warm 108.4 ms.
- 500 files: naive 249500, retrieved 5000, reduction 98.00%, total lineage 310.3 ms; cold 553.7 ms, warm 603.1 ms.
- 1000 files: naive 999000, retrieved 10000, reduction 99.00%, total lineage 704.7 ms; cold 1032.3 ms, warm 877.3 ms.
- 2000 files: naive 3998000, retrieved 20000, reduction 99.50%, total lineage 2114.7 ms; cold 1792.5 ms, warm 1880.5 ms.

Incremental: existing 500 files + one file = 129.5 ms; 10 candidates.

## External real corpus

External corpus infrastructure: READY

Real Word corpus: NOT POPULATED

Real WPS corpus: NOT POPULATED

Real WeChat transfer corpus: NOT POPULATED

## Three successful cases

1. Normal synthetic DOCX chain: shared RSID/revision/content evidence retains direct parents and GitIt selects the recorded parent.
2. Branch case: two versions based on one known parent are retained as separate child edges.
3. Exact duplicate: byte-identical copies are grouped by SHA-256 rather than asserted as a new lineage edge.

## Three uncertain or failure cases

1. Reconstructed content has no reliable provenance; the engine must abstain or mark it RelatedButUnproven.
2. Removing document properties, revisions, and comments can erase high-value provenance evidence.
3. Highly similar template descendants can look related; the dedicated sibling benchmark checks for false families and edges.

## Most dangerous false-positive scenarios

1. Large content reconstruction after provenance is stripped.
2. WPS or privacy cleanup rewriting OOXML provenance.
3. Highly similar documents independently produced from a shared template.

## Did optimization reduce accuracy?

NO — optimized and naive synthetic edge sets matched in this run.

## Recommendation

Recommendation:

DO NOT ENTER PC ALPHA YET

Reason: the only current release blocker is human-made external Word/WPS/WeChat corpus validation. Do not add more Core features until that review is complete.
