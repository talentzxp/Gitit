# Real-world corpus experiment plan

The first-batch, step-by-step instructions and the canonical manifest example now live in [real-world-corpus/README.md](../real-world-corpus/README.md). The required execution command is:

```powershell
dotnet run --project src/GitIt.Cli -- corpus validate real-world-corpus
dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus
```

With no manually created Office files, the only valid outcome is `WAITING FOR MANUAL CORPUS`; do not claim a real-world pass.

GitIt cannot operate Microsoft Word, WPS, WeChat, or an end-user privacy-cleaning workflow here. No real-world pass is claimed until an authorized human creates the files, records the answers, and validates the manifest.

## Before starting

Use non-sensitive dummy text. For every saved version, copy the file (do not rename the only working copy) into the matching folder under `real-world-corpus/`. Record the editor, editor version, transfer method, operation, date, and parent ID in `corpus.json`. Keep parent IDs out of filenames, Office metadata, and folders that GitIt scans. Run `gitit corpus validate real-world-corpus` before benchmarking.

## DOCX-01 Word-only

1. In Microsoft Word, create a five-page document with five headings and two tables; save `v1.docx`.
2. Change three body paragraphs and save a copy as `v2.docx`.
3. Change Normal style line spacing and save `v3.docx`.
4. Enable Track Changes, edit two paragraphs, add one comment, and save `v4.docx`.
5. Reopen `v3.docx`, modify a table, and save `v3-branch.docx` as a branch.
6. Add each file and its direct parent to a DOCX manifest family.

## DOCX-02 Word/WPS and DOCX-03 WeChat flow

1. Create `v1.docx` and edit it in Word into `v2.docx`.
2. Open `v2.docx` in WPS, edit it, and save `v3.docx`; reopen in Word and save `v4.docx`; reopen in WPS and save `v5.docx`.
3. For the WeChat flow, send `v1` from PC A through WeChat, download on PC B, edit in Word or WPS to `v2`, send back through WeChat, and make `v3` on PC A.
4. Capture `rsidRoot`, RSIDs, LastModifiedBy, revisions, comments, and style rewrites as observations only. Do not add WPS-specific engine rules yet.

## DOCX-04 privacy cleanup and DOCX-05 copy/paste

1. Make a copy of a normal chain, remove personal information, accept revisions, remove comments, clear document properties, and save each cleanup stage.
2. Separately create a blank document, Ctrl+A/Ctrl+C the original content into it, and save it.
3. The copy/paste case should be labelled as no parent; expected GitIt behavior is `RelatedButUnproven`, low confidence, or abstention—not an invented direct edge.

## XLSX and PPTX

Create Office-only, Office/WPS, branch, and template-sibling families. For XLSX vary values, formulas, formats, sheet addition, hidden columns, and widths. For PPTX vary text, font size, shape position, slide addition/deletion, layout, and theme. For template siblings, make Nanjing/Suzhou/Wuxi-style independent documents from one template and set every parent to null.

## Completion checklist

- Run `gitit corpus validate real-world-corpus`.
- Keep the manifest outside the folders passed to normal `gitit analyze` commands.
- Benchmark engine output first; only then load parent ground truth for scoring.
- Report sample counts by format/editor/transfer. Small samples are exploratory, not proof.
