# StayOTA Hotel Refund Agent（全量能力版）

结合 `hotel`（工作台）+ `Hotel-refund`（受控 Workflow / 33 Tool / A–L / Eval）的 .NET 10 实现。

技术栈：**.NET 10 + Vue3（Vben Admin 风格）+ PostgreSQL + Redis**

## 已覆盖能力

- A–L 十二场景 + 低置信度 / 服务异常边界态
- 33 Tool 契约注册与 Mock 执行（权限门：确认令牌 / 版本 / 幂等 / 审计）
- 意图 · 槽位 · 政策检索 Top3 · 规则引擎 · 风险分层
- Session（Redis）/ Case Event / Workflow Trace（PostgreSQL）
- 36 条离线 Eval（`POST /api/eval/run`）
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

## 测试

```bash
cd backend && dotnet test
curl -X POST http://127.0.0.1:5088/api/eval/run
```

## 明确不做

- 真实 LLM 网关
- StayOTA 生产订单/支付系统直连
- 官方 vue-vben-admin monorepo 整仓嵌入（当前为同风格独立模块，可迁入）
