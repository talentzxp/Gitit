# GitIt Real Corpus Gate

This folder must contain only Office files that you personally created or are authorized to retain. Do not use generated fixtures and do not add private work documents. Before any files are added, the correct gate status is `WAITING FOR MANUAL FILE CREATION`.

## First-batch layout

```text
docx/chain-word-wechat-wps/  main five-version chain
docx/branch/                 two branches from the same v3
docx/destructive/            privacy cleanup, copy/paste, Save As
xlsx/basic-chain/            five-version Excel/WPS chain
pptx/basic-chain/            five-version PowerPoint/WPS chain
manifests/                   optional working notes; corpus.json is the scored manifest
```

Keep exactly these first-batch scenarios. The target is roughly 20–30 total files, not a large statistical corpus.

## Create the DOCX main chain

1. In Microsoft Word, create a three-to-five page document with headings, body text, a table, one image, header/footer, and a visible custom style. Save `docx/chain-word-wechat-wps/v1.docx`.
2. In Word, edit two or three paragraphs, alter one number, and add one sentence. Save a copy as `v2-word-edit.docx`.
3. Send `v2-word-edit.docx` through WeChat, download it on another device, open it in WPS, change a heading font size, a table cell, and paragraph formatting. Save `v3-wechat-wps.docx`.
4. Send v3 back through WeChat, open it in Word, change one paragraph, add a comment, and optionally make a small Track Changes edit. Save `v4-wechat-word.docx`.
5. In Word, change Normal/body style line spacing or default font without changing much text. Save `v5-style-drift.docx`.
6. From v3 create `branch-a-word.docx` in Word by changing body text only, and `branch-b-wps.docx` in WPS by changing a table only. Neither branch is the parent of the other.
7. From a baseline copy make `privacy-cleaned.docx`, `copy-paste-rebuilt.docx`, and a small-change `word-wps-save-as.docx`. The copy/paste file should have `parent: null` because GitIt is expected to abstain or report related-but-unproven.

## Create the XLSX and PPTX chains

- **XLSX**: save v1; change 3–5 values for v2; change a formula for v3; use WPS to change a number format, column width, and hide a column for v4; use Excel to add a sheet and edit a cell for v5.
- **PPTX**: save v1; change title text for v2; move a shape and change its font size for v3; use WPS to add a slide and change a text box for v4; use PowerPoint to delete a slide or adjust layout for v5.

## Record the ground truth

After files exist, update `corpus.json`. This information is loaded only after GitIt has finished engine analysis. Use relative paths and structured `expectedChanges` entries where possible.

```json
{
  "schemaVersion": "GitIt Real World Corpus v1",
  "families": [{
    "id": "docx-main-chain",
    "format": "docx",
    "versions": [
      { "id": "v1", "file": "docx/chain-word-wechat-wps/v1.docx", "parent": null, "editor": "Microsoft Word", "editorVersion": "unknown", "transfer": "local", "operation": "create" },
      { "id": "v2", "file": "docx/chain-word-wechat-wps/v2-word-edit.docx", "parent": "v1", "editor": "Microsoft Word", "editorVersion": "unknown", "transfer": "local", "operation": "edit-and-save", "expectedChanges": [{ "type": "content" }] },
      { "id": "v3", "file": "docx/chain-word-wechat-wps/v3-wechat-wps.docx", "parent": "v2", "editor": "WPS", "editorVersion": "unknown", "transfer": "WeChat", "operation": "edit-and-save", "expectedChanges": [{ "type": "format" }, { "type": "table" }] }
    ]
  }]
}
```

## Run the gate

```powershell
dotnet run --project src/GitIt.Cli -- corpus validate real-world-corpus
dotnet run --project src/GitIt.Benchmarks -- real real-world-corpus
```

The second command creates `outputs/REAL_CORPUS_REVIEW.md` and prints a copyable review. It distinguishes correct edges, wrong edges, abstentions, expected changes detected/missed, and actual participant-evidence survival. It never treats an empty corpus as a pass.
