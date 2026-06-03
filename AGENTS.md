# AGENTS.md

<!-- BEGIN OPENSPEC-SCHEMA-ROUTING -->
## OpenSpec Schema 路由规则

创建 OpenSpec change 时不得使用裸命令：

```bash
openspec new change "<name>"
```

必须先判断变更类型，并显式传入 schema：

```bash
openspec new change "<name>" --schema spec-driven
openspec new change "<name>" --schema superpowers-bridge
```

不确定时先进入 explore，不创建 change。

### Schema 选择

使用 `superpowers-bridge`：
- 新功能
- UI/交互重构
- 影响多个文件或模块
- 有设计取舍
- 需要验收标准
- 需要测试、构建或手测证据
- 需要复盘沉淀

使用 `spec-driven`：
- 小 bugfix
- 文案、配置、样式微调
- 单文件低风险改动
- 没有明显设计分歧
- 不需要 retrospective
<!-- END OPENSPEC-SCHEMA-ROUTING -->

