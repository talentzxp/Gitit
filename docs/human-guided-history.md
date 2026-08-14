# GitIt v0.0.8 Reconstruction Workbench

## Working with a real folder

1. Open GitIt Desktop and choose a copied, authorized folder of Office files.
2. Review the scan preview. Temporary files whose names begin with `~$` and non-Office files are excluded before analysis begins.
3. Select **开始分析** only after the scope is correct.
4. Use **未关联文件** to select files and create a user document group, or drag the selected files onto an existing user document group. A group adds context only; it does not assert a parent-child history. GitIt then runs Core candidate, lineage, participant and semantic Diff analysis only for the affected group.
5. Select a file to inspect Core candidate parents. Use **确认此来源** only when you have independent human knowledge; **保持未确认** records a review state without creating a relation.
6. Open **Diff Workbench** from an edge, a candidate source, or two selected files. It supports side-by-side and unified views of Core semantic changes.
7. Add a note when the context cannot be represented as a parent-child relation.
8. Use **保存项目** to write a `.gitit` file. It stores the Core analysis snapshot and the user overlay, not source files.

## Evidence labels

- Solid and dashed lines are Core relationships with different confidence strength.
- A dotted question-mark line is related but unproven, not a confirmed lineage edge.
- A red line means Core found conflicting evidence.
- A blue checked line is a user-confirmed source relation. If it overlaps a Core edge, the label says that both exist.

Office authorship values remain unverified package strings. A comment proves review participation; it does not prove that person made the document edit.
