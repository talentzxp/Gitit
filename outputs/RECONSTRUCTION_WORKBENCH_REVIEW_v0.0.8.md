# GitIt v0.0.8 Reconstruction Workbench Review

## Participant Timeline

参与者是否真正进入时间线：**YES**。

桌面时间线不再只是版本列表。每项事件统一包含事件类型、参与者、版本、时间或时间区间、时间精度、证据类型、证据强度和说明。支持创建、修改、评论、最后保存、参与和版本观察。

- Revision Author 加 Revision Date 显示为 **修改 / 精确**。
- Comment Author 显示为 **评论 / 版本时间**，并明确说明评论不证明修改。
- LastModifiedBy 显示为 **最后保存 / 版本时间**，不伪装为修订作者。
- 某人在后续版本首次出现且没有精确修订时间时，显示为 **参与 / 区间推定**，例如 `2026-08-01 ～ 2026-08-05`；不会制造小时级时间。

覆盖十二名参与者的时间线测试证明 UI 不会因为人数多而丢弃事件。示例中 Participant 01、Participant 02、Participant 03、Participant 04、Participant 05 都保留了“最后保存 / 版本时间”和“首次出现参与证据 / 区间推定”；另外七名也同样保留。

## Drag-to-Group

拖入文档组后是否触发局部重新推理：**YES**。

未关联文件列表与搜索结果支持多选拖拽到用户文档组。加入前，文件只是用户分组外的未关联项；加入后，GitIt 只对该用户组的 Profile 集合调用既有 Core candidate retrieval、lineage inference 和 SemanticDiffer，更新本组候选、图、时间线和 Diff 缓存。

before：文件不在用户组，只有全局分析结果。  
after：文件加入用户组，`LocalReanalysisCount` 增加，局部 `LocalGroupAnalysis` 提供新的候选来源与 Diff。

归组仍只表示 `USER ASSERTION: belongs to group`，不创建父子边，也不降低 Core 阈值。

## Candidate Visibility

弱关系不会完全消失。默认图保留 probable、用户确认和 RelatedButUnproven；勾选“显示候选关系”后，其他 Core candidate 以更细灰色虚线、问号、置信度和“强血缘证据不足”说明出现。版本详情始终可展示最多十二条候选来源及其支持/缺失证据。用户可确认，或记录“保持未确认”。

## Diff Viewer

Diff Workbench 可从 Core lineage edge、candidate source 或任意两个用户选中文件打开，提供 Side-by-Side 与 Unified 两种视图，并按内容、格式、结构和公式筛选。

- **DOCX**：段落内容、样式/格式、表格结构及单元格语义变化；旧值和新值分列显示。
- **XLSX**：工作表、单元格、公式、合并单元格、行列尺寸和格式变化；地址保留为例如 `Sheet2!F27`。
- **PPTX**：幻灯片文本、形状、位置、尺寸、字体、主题或布局引用变化；不声称进行页面级视觉渲染。

Diff 是 GitIt 的 Inspect 层，而非产品的全部：它建立在混乱文件夹的发现、重建和用户修正之后。

## Core Safety

Lineage thresholds changed: **NO**。

v0.0.8 没有修改阈值、评分权重或候选检索标准。Core 新增的仅是读取 DOCX 修订作者和修订日期，以便真实保存的修订时间可以按精确事件显示；局部重分析调用现有 Core 引擎。

## Tests

**23 / 23 passed**。

覆盖 Revision Author + Date、评论、保存、首次出现区间、十二名参与者保留、创建用户组、局部重分析触发、Core 关系数量不被 GUI 改写、DOCX/XLSX/PPTX 手动比较、candidate Diff、项目保存和临时 Office 锁文件跳过。

## Known Problems

1. 局部重分析目前在桌面线程上同步执行；大用户组尚未提供进度条或取消操作。
2. DOCX Diff Workbench 显示 Core 的段落级语义 Before/After，尚未实现字符级富文本词语高亮和完整未改段落上下文。
3. `.gitit` 快照不会自动核对源文件之后是否被移动、删除或再次修改；真实语料测试需要记录这种情况。

## Product Boundary

Does GitIt currently depend on users manually ordering every version before diff? **NO**。

普通 pairwise Diff 要求用户先知道 A 和 B。GitIt 从混乱文件夹中自动发现相关文件、恢复可能历史和参与者证据、保留不确定候选，并允许用户用分组与确认补充上下文；随后才可点击任意已发现或用户指定的步骤检查 Diff。

## Real Corpus and Recommendation

Real Corpus: **WAITING FOR MANUAL CORPUS**。

Recommendation: **READY FOR REAL CORPUS**。

这表示应以授权的真实 Office 文件检验工作台流程，不代表 Real Corpus Gate 已通过。
