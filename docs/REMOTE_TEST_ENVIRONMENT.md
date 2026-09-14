# 远端 Docker 部署模板

> 本文提供通用部署样例，不记录任何真实企业服务器、账号、地址或备份标识。生产部署前应完成网络、安全和恢复方案评审。

## 示例拓扑

| 项目 | 示例值 |
|---|---|
| SSH 目标 | `deploy@harness.example.com:22` |
| Compose 目录 | `/opt/company-harness/litellm` |
| 员工入口 | `https://harness.example.com` |
| 管理入口 | `https://admin.harness.example.com` |
| LiteLLM 容器端口 | `4000`，不映射宿主机 |
| ControlPlane | Java 21 + Spring Boot，容器端口 `8080` |
| 管理网页 | Vue 3 静态构建，由 Nginx 提供 |
| ControlPlane 数据库 | 独立账号、独立数据库和 schema |

`harness.example.com` 属于文档示例域名。部署时必须在 `.env` 中将 `LITELLM_PUBLIC_BASE_URL` 替换为员工设备能够访问的 HTTPS `/v1` 地址。

## 安全边界

- 服务器只保留部署文件和运维验收脚本，不同步仓库源码；
- `.env` 权限限制为部署账号可读，不回传本机、不提交版本库；
- LiteLLM 和 ControlPlane 使用不同的 PostgreSQL 账号与数据库；
- DeepSeek 上游 Key、LiteLLM Master Key 和管理密码不得进入源码、日志或测试输出；
- 员工入口只转发激活 API 和 LiteLLM `/v1/*`，管理 API 与 LiteLLM `/key/*` 不公开；
- 管理入口必须启用认证、HTTPS 和来源访问控制；
- 使用低权限部署账号，并限制其 Docker、文件和网络权限；
- 数据库与配置备份必须加密、异机保存并定期执行恢复演练。

## 首次部署

```powershell
Copy-Item infra/litellm/.env.example infra/litellm/.env
```

编辑 `.env`，为所有 `replace-with-*` 项设置独立随机值，并填写企业自己的公网地址。随后在服务器运行：

```bash
cd /opt/company-harness/litellm
docker compose --env-file .env config
docker compose --env-file .env up -d --build
docker compose --env-file .env ps
```

查看日志时不要把包含凭据或员工数据的输出复制到公开问题单：

```bash
docker compose --env-file .env logs --tail=100
```

## 发布前验收

- 员工健康检查、激活和 `/v1/models` 通过 HTTPS；
- 未认证管理请求返回 401；员工入口访问管理路径和 `/key/*` 返回 404；
- Preview → 姓名确认 → Confirm 只能兑换一次；
- 禁用员工后，其虚拟 Key 立即失效；
- 月度金额、RPM、TPM、并发和模型白名单限制生效；
- 激活码和员工有效期按预期生效；
- 日志和数据库中没有提示词、代码、回复或明文激活码；
- 临时员工、激活码和虚拟 Key 已清理；
- 数据库备份已生成，并在隔离环境完成恢复验证。

## 服务器文件边界

- `compose.yml`：容器、端口和依赖；
- `config.yaml`：模型别名与 LiteLLM 策略；
- `.env`：服务密钥、数据库凭据和管理账号；
- `nginx.conf`：员工入口与独立管理入口；
- `control-plane/`：Dockerfile 和 Spring Boot `app.jar`；
- `admin-web/`：Vue 生产静态资源；
- `postgres-init/`：新数据卷初始化脚本；
- 部署目录外的受控位置：加密数据库与配置备份；
- `gateway-acceptance.sh`、`web-search-compatibility.sh`：可自动清理测试 Key 的验收脚本；
- `employee-management-acceptance.sh`：员工增删、状态切换、激活签发和禁用联动验收；
- `validity-policy-acceptance.sh`：激活码和员工访问期限验收。

项目源码、解决方案、测试、文档和本机构建缓存不应同步到生产服务器。
