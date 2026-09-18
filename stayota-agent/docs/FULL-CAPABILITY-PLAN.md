# StayOTA Agent 全量能力实施计划

目标：把 `hotel` 的工作台体验 + `Hotel-refund` 的受控后端能力，在 .NET 10 / Vue / PG / Redis 上补齐到可演示的「全量 MVP」。

## 能力矩阵

| 能力块 | 来源 | 目标状态 |
| --- | --- | --- |
| A–L 十二场景 + 边界态 | 两边 | 全覆盖 |
| 33 Tool 契约与执行 | Hotel-refund | 全注册 + Mock 执行 |
| 意图/槽位/政策检索/规则/风险 | hotel | 独立模块 |
| Tool 权限门（确认/版本/幂等/审计） | Hotel-refund | 完整 |
| Session / Case / Trace | Hotel-refund | PG + Redis |
| 36 条离线 Eval | Hotel-refund | API + 测试 |
| 智能处理台三栏 | hotel | 完整交互 |
| Agent 设计页 | Hotel-refund | 流程图页 |
| 运营看板/Badcase | 两边 | 完整展示 |
| 真 LLM / 真业务系统 | - | 明确不做（边界） |

## 实施顺序

1. 契约与 Mock 数据入库
2. ToolRegistry + WorkflowRunner（A–L）
3. Intent / Retrieval / Rules / Risk
4. Eval Runner
5. 前端三页补齐
6. 自动化测试与联调
