# StayOTA Hotel Refund Agent

基于 `hotel`（工作台体验）+ `Hotel-refund`（受控 Workflow / Tool 权限 / Eval 口径）结合实现的新项目。

技术栈：**.NET 10 + Vue3（Vben Admin 风格布局）+ PostgreSQL + Redis**

> 这是可运行的 MVP 骨架，不是生产成品；订单/政策/金额均为演示数据。

## 架构

```text
Vue 智能处理台（学 hotel）
  ├─ 场景轨 / 会话区 / 决策轨迹 / 运营看板
  └─ Ant Design Vue + Vue Router + Pinia（可迁入正式 Vben Admin 作为业务模块）

.NET 10 API（学 Hotel-refund）
  ├─ RulesEngine：金额 / 风险 / 路径
  ├─ ToolGateway：读写下发、确认令牌、幂等、审计
  ├─ AgentOrchestrator：显式决策链路
  ├─ PostgreSQL：订单 / 案件 / Workflow / Tool 审计
  └─ Redis：确认令牌 / 幂等键 / Session
```

## 目录

```text
stayota-agent/
  backend/     .NET 10 解决方案
  frontend/    Vue3 + Ant Design Vue（Vben 风格）
  docker-compose.yml
  README.md
```

## 快速启动

### 1. 依赖

- .NET 10 SDK
- Node.js 20+
- PostgreSQL 16
- Redis 7

可用本目录 compose：

```bash
docker compose up -d
```

或本地服务：

```text
Postgres: Host=127.0.0.1;Database=stayota_refund;Username=stayota;Password=stayota
Redis: 127.0.0.1:6379
```

### 2. 后端

```bash
cd backend
dotnet restore
dotnet run --project src/Stayota.RefundAgent.Api --urls http://127.0.0.1:5088
```

- Swagger: http://127.0.0.1:5088/swagger
- Health: http://127.0.0.1:5088/health

### 3. 前端

```bash
cd frontend
npm install
npm run dev
```

打开 http://127.0.0.1:5173

### 4. 测试

```bash
cd backend
dotnet test
```

## 已实现场景

| Code | 名称 | 来源灵感 |
| --- | --- | --- |
| free-cancellation | 免费取消 | hotel + Hotel-refund A |
| deducted-cancel | 扣费取消 | Hotel-refund B |
| non-cancellable | 不可取消协商 | hotel |
| flight-cancelled | 航班取消补材料 | hotel 主推场景 |
| no-room | 到店无房转人工 | hotel / Hotel-refund E |
| refund-progress | 退款进度 | hotel |
| payment-anomaly | 支付异常 | Hotel-refund J |

## 明确未实现

- 真实 LLM / Prompt 版本管理
- 正式 vue-vben-admin 单体仓库接入（当前为同风格独立前端，便于后续迁入）
- StayOTA 真实订单/支付/供应商接口
- 生产鉴权、监控、灰度

## API 摘要

- `GET /api/scenarios`
- `POST /api/agent/message`
- `POST /api/confirmations`
- `GET /health`
