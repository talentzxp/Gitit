# GitIt v0.0.7 Human Guided Document History Review

## 1. 新增能力

- **Family management**：用户可以从未关联文件中选择多个文件创建文档组、向用户文档组加入文件、重命名文档组，以及从分析视图移除或恢复文件。用户文档组只提供上下文，不自动声明父子关系。
- **Candidate source**：选择版本时，GUI 直接展示 Core 已产生的 candidate parent、置信度、支持证据和缺失或冲突证据。候选不会自动绘制为正式血缘。
- **Timeline participation**：时间线将创建、修改、评论、保存和参与事件按 Office 证据分类。没有独立事件时间时明确说明使用文件修改时间；评论被展示为参与，不伪装成修改。
- **User confirmed relation**：用户可确认来源关系。它被保存为独立的 user-confirmed-parent 标注；图中用蓝色勾选线呈现。若 Core 同时存在推断，界面明确显示两层信息。
- **Hidden files**：隐藏只影响分析视图；不会删除或改写原始文件。
- **Project save**：`.gitit` 文件保存分析快照、用户分组、确认关系、隐藏项、家族名称和备注；不保存原始 Office 文件。
- **Folder scope preview and search**：文件夹在分析前先显示 Office 文件、临时 Office 文件及其他文件数量；支持文件名、已解析文本和参与者搜索。

## 2. Core 是否修改

Core lineage algorithm changed: **NO**

没有调整血缘评分、阈值、候选检索或 Core 推断结果。唯一的扫描边界改动是跳过名称以 `~$` 开头的 Office 临时锁定文件，使实际扫描行为与用户可见的范围预览一致。这些临时文件不是文档版本。

用户标注被放在独立的 `GitIt.UserAnnotations` 项目中；Desktop 只将其叠加到 Core 输出之上。

## 3. 测试

Tests: **20/20 passed**

已覆盖：创建用户文档组、重命名、隐藏与恢复、用户确认关系、`.gitit` 保存与加载、标注存储往返、Core 关系数量不变，以及临时 Office 锁定文件跳过。构建结果为 0 warnings / 0 errors。桌面程序完成一次隐藏启动检查。

## 4. 当前状态

Real Corpus: **WAITING**

重新运行 Real Corpus Gate 后确认：当前授权真实语料目录仍为 0 个 Office 文件。没有将合成演示或 GUI 测试解释为真实 Word、WPS、WeChat、Excel 或 PowerPoint 流程验证。

## 5. 当前限制

- 用户确认是用户知识记录，而非 Core 自动推断；它不代表可由 Office 包证据独立验证。
- `.gitit` 快照不会自动检测原始文件日后是否移动、删除或变更；当前版本优先保证可恢复的审阅状态。
- 编辑器环境、身份认证和缺失历史仍不会猜测。
- 超大图谱仍需要后续的缩放、筛选与布局改进，但不属于本阶段范围。

## 6. 下一阶段建议

READY FOR REAL CORPUS

建议以用户已知历史、已授权且复制到单独测试目录的第一批真实 Office 文件进行验证。重点记录：哪些候选关系由用户确认、哪些家族需要手动组织、以及哪些证据不足导致合理 abstention。
