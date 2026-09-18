# StayOTA Hotel Refund Agent（MEAI + Agent Framework）

结合 `hotel`（工作台）+ `Hotel-refund`（受控 Workflow / 33 Tool / A–L / Eval）的 .NET 10 实现。

技术栈：**.NET 10 + Microsoft.Extensions.AI + Microsoft Agent Framework + Vue3（Vben 风格）+ PostgreSQL + Redis**

## 架构要点

| 层 | 实现 |
| --- | --- |
| 模型接入 | `IChatClient`（默认 `DeterministicRefundChatClient`，可换成 Azure OpenAI / Foundry） |
| Agent | `ChatClientAgent`（`Microsoft.Agents.AI`） |
| A–L 编排 | `WorkflowBuilder` + `InProcessExecution`（`Microsoft.Agents.AI.Workflows`） |
| 33 Tool | `AIFunctionFactory` + `ApprovalRequiredAIFunction`（确认类写操作） |
| 领域门禁 | `ToolGateway`（确认令牌 / 版本 / 幂等 / 审计）— 保留自研 |
| 规则 / 风险 / Eval | Domain + Verifier — 保留自研 |

## 已覆盖能力

- A–L 十二场景 + 低置信度 / 服务异常边界态
- 33 Tool 契约注册与 Mock 执行（权限门：确认令牌 / 版本 / 幂等 / 审计）
- 意图 · 槽位 · 政策检索 Top3 · 规则引擎 · 风险分层
- Session（Redis）/ Case Event / Workflow Trace（PostgreSQL）
- 36 条离线 Eval（`POST /api/eval/run`）
- A–L Agent Framework Workflow（`POST /api/workflows/run-all`）
- Tool `allowed_conversation_states` 白名单门禁
- Verifier 决策/工作流断言
- 前端三页：Agent 设计 / 智能处理台 / 运营看板

## 启动

```bash
# 依赖：Postgres + Redis（可用 docker compose up -d）
export PATH="$HOME/.dotnet:$PATH"
cd backend
dotnet run --project src/Stayota.RefundAgent.Api --urls http://127.0.0.1:5088

cd ../frontend
npm install && npm run dev
```

- API Swagger: http://127.0.0.1:5088/swagger
- 前端: http://127.0.0.1:5173
- Health 会返回 `aiProvider` / `agent` / `stack`

## 测试

```bash
cd backend && dotnet test
curl -X POST http://127.0.0.1:5088/api/eval/run
curl -X POST http://127.0.0.1:5088/api/workflows/run-all
```

## AI 提供商

默认 `Deterministic`（离线演示）。可在 `appsettings.json` 或环境变量切换：

```bash
# OpenAI / 兼容网关
export AI_PROVIDER=OpenAI
export OPENAI_API_KEY=sk-...
# optional: OPENAI_ENDPOINT=https://...  OPENAI_MODEL=gpt-4o-mini

# Ollama 本地
export AI_PROVIDER=Ollama
export OLLAMA_ENDPOINT=http://127.0.0.1:11434
export OLLAMA_MODEL=llama3.2
```

配置错误时会自动回退到 Deterministic，保证 API 可启动。
