# LiteLLM + Spring Boot ControlPlane 技术验证环境

本目录由 Docker Compose 运行 PostgreSQL、LiteLLM、Spring Boot ControlPlane 和 Nginx。Nginx 提供两个入口：

- 公网 `8765`：员工激活接口与 OpenAI 兼容模型接口；
- 公网 `8766`：公司管理网页和管理 API（当前仅供技术验证，使用 HTTP Basic 认证）。

## 构建与部署

1. 将 `.env.example` 复制为 `.env`，替换全部示例口令；LiteLLM 与 ControlPlane 使用不同的 PostgreSQL 用户和数据库。
2. 检查 `config.yaml` 中的 DeepSeek 模型名是否符合公司账号的实际权限。
3. 在 `services/control-plane` 运行 `mvn package`，把生成的可执行 JAR 复制为 `control-plane/app.jar`。
4. 在 `admin-web` 运行 `npm run build`，把 `dist` 的内容复制到本目录的 `admin-web`。
5. 执行 `docker compose --env-file .env up -d --build`。
6. 打开 `http://<host>:8766/admin/` 访问管理网页。
7. 在管理网页单人添加或 CSV 导入员工，设置启用状态并生成一次性激活码，再由员工客户端完成激活与模型调用。

员工禁用会同步删除该员工的活跃 LiteLLM Key，并撤销未使用激活码。只有从未签发过模型 Key 的员工可以物理删除；已有 Key 历史的员工应保留记录并使用禁用。

当前 `config.yaml` 的 Key 生成上限对应管理表单上限：月度金额 72,000 元（按 7.2 CNY/USD 和 10,000 USD 上限）、RPM 600、TPM 2,000,000、并发 20。调整 LiteLLM 上限或汇率时必须同步更新 ControlPlane 校验和管理表单提示。

## 路径边界

- `http://<host>:8765/api/v1/activation/*`：ControlPlane 激活接口；
- `http://<host>:8765/v1/*`：LiteLLM 模型接口；
- `http://<host>:8765/health/ready`：ControlPlane 和 PostgreSQL 就绪状态；
- `http://<host>:8766/admin/`：当前测试环境公网管理网页；
- `/key/*`、`/api/v1/admin/*` 等管理路径不会从公网入口转发。

ControlPlane 使用 Java 21 + Spring Boot，Flyway 管理 `company_harness` 数据库 schema。激活码只保存 HMAC-SHA-256，预览会话只保存 SHA-256，数据库不保存激活码、会话令牌或虚拟 Key 明文。兑换事务通过 PostgreSQL 行锁保证并发时只有一个请求成功。

`postgres-init/10-control-plane-database.sh` 只在新数据卷初始化时自动创建独立账号和数据库；已有数据卷升级时需人工执行一次该脚本。

> 当前按测试要求使用 HTTP，`8766` 管理入口也临时公开，Basic Auth 凭据会以可被链路观察的形式传输；正式推广前必须收紧来源并配置域名与受信任的 HTTPS 证书。
