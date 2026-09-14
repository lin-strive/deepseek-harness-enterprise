# 架构与技术选型基线

## 模块边界

```text
Windows Desktop Shell
  ├─ Desktop.Core：激活客户端、Harness 进程、更新校验
  ├─ WebView2：仅加载 127.0.0.1 上的 Harness UI
  └─ Credential Manager：保存员工虚拟 Key
             │ HTTP（当前测试）/ HTTPS（正式环境）
             ▼
Nginx :8765 ──> Spring Boot ControlPlane ──管理 API──> LiteLLM ──> DeepSeek API
      │                    │                              │
      │                    └── company_harness PostgreSQL└── LiteLLM PostgreSQL + Redis
      │
      └── 公网不转发管理 API

管理员浏览器 ──公网 HTTP（临时测试）──> :8766 Nginx ──> Vue 管理网页 + ControlPlane 管理 API
```

## 技术决策

| 范围 | 选择 | 理由 |
|---|---|---|
| Windows 壳 | .NET 8 + WPF + WebView2 | Windows 首发、安装与系统集成成熟。 |
| 客户端核心 | 独立 `Desktop.Core` 类库 | 进程、更新、激活逻辑可在没有 UI 的情况下测试。 |
| 管理服务 | Java 21 + Spring Boot 4 | 与公司 Java 技术栈一致，便于持续维护；Flyway 提供版本化迁移。 |
| 管理网页 | Vue 3 + Vite | 浏览器完成员工、配额和激活码管理，部署为 Nginx 静态资源。 |
| 反向代理 | Nginx | 分离员工入口与管理入口；当前测试期临时公开管理端口。 |
| 模型网关 | LiteLLM Proxy | 复用虚拟 Key、预算、RPM、TPM、并发和用量能力。 |
| 数据库 | PostgreSQL 16 | 事务与并发兑换可靠；ControlPlane 和 LiteLLM 使用独立数据库与账号。 |
| 分布式限流 | Redis | 多网关实例共享限流计数。 |
| 更新签名 | RSA-PSS/SHA-256 + 包 SHA-256 | Windows/.NET 原生支持，清单与包分别校验。 |
| 上游集成 | 固定官方包，不修改源码 | 便于跟随更新并保持可回滚。 |

LiteLLM 的 `max_budget` 使用美元口径，而产品对员工展示和配置人民币。ControlPlane 在签发 Key 时使用受控汇率 `CNY_PER_USD` 换算，并把原人民币额度和汇率写入非敏感元数据。价格表和汇率必须版本化。

## 信任边界

- 客户端是不可信环境，不接触 DeepSeek 真实 Key 或 LiteLLM Master Key。
- ControlPlane 仅在签发、停用和轮换时使用 LiteLLM 管理 API，不代理模型正文。
- LiteLLM 是模型数据面，仅记录 Token、费用、时延和状态等元数据。
- Harness 绑定 `127.0.0.1`；桌面壳清除个人 Provider 环境变量并关闭遥测。
- 管理网页和 `/api/v1/admin/*` 由独立端口 `8766` 暴露并使用 HTTP Basic 认证；当前测试期临时允许公网直连，公网员工入口 `8765` 对管理路径仍返回 404。
- ControlPlane 使用独立 `company_harness` PostgreSQL 数据库；Preview 会话和一次性兑换状态跨容器重启保留。
- Confirm 对激活会话和激活码执行数据库行锁；并发确认只能有一个事务签发并登记虚拟 Key。
- 禁用员工时，ControlPlane 先通过 LiteLLM `/key/delete` 撤销全部活跃 Key，再在同一管理事务中禁用本地 Key 记录并作废未使用激活码；若网关撤销失败则员工状态不变。
- 员工访问期限保存在 `employees.access_expires_at`；空值表示持续到管理员手动禁用。ControlPlane 每分钟扫描到期员工，并复用相同撤销事务禁用 LiteLLM Key 和未使用激活码；激活接口同时校验期限以阻止到期边界继续签发。
- 激活码请求使用绝对 `expiresAt`，最长 90 天；为旧管理脚本暂时兼容 `expiresInHours`。若员工访问期限更早，激活码实际到期时间自动收紧到员工期限。
- 物理删除仅允许从未签发过虚拟 Key 的员工；已有 Key 历史的员工必须禁用，以保留数据关联。
- 管理密码和各类服务密钥只保存在服务器权限为 `600` 的 `.env` 中。
- 模型目录保存在 `company_harness.model_catalog`。公网只读 `/api/v1/models` 返回已启用的官方模型、默认模型和输入能力；管理端启停模型后同步所有活跃 LiteLLM 虚拟 Key。客户端缓存最后一次有效目录，更新目录不要求重新打包或重新激活。
- LiteLLM 同时保留旧 `company-*` 路由用于 0.1.x 客户端迁移兼容；新版客户端只使用 DeepSeek 官方模型 ID。旧别名在旧客户端退出试点后删除。

## 运行目录

```text
%LOCALAPPDATA%\CompanyHarness\
  runtime\<version>\       # 可替换、只读运行时
  data\                    # DSH_HOME、会话和个人配置
  skills\company\         # 公司签名 Skills
  updates\staging\        # 更新暂存
  logs\                    # 脱敏诊断日志
```

Runtime 与 data 分离，升级只切换 runtime 指针；健康检查失败时回切上一版本。
