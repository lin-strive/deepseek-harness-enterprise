<p align="center">
  <img src="info/deepseek-harness-enterprise-logo.png" alt="DeepSeek Harness Enterprise" width="760">
</p>

# DeepSeek Harness Enterprise

面向各类企业内部部署的 DeepSeek Harness 企业化封装方案。员工在 Windows 电脑本地运行 Harness，所有模型请求统一经过企业自建的 LiteLLM 网关，并通过员工独立虚拟 Key 实现用户隔离、模型权限、月度预算、RPM、TPM 和并发限制。

项目采用“上游 Harness 不侵入修改”的集成方式：Windows 桌面壳负责激活、运行时管理、企业策略和升级入口，服务端 ControlPlane 负责员工、激活码、模型目录及 LiteLLM 虚拟 Key 生命周期，便于企业跟随 DeepSeek Harness 官方版本更新。

当前基线版本为 `0.2.7`，已经完成 Windows x64 自包含安装包、首次启动稳定性、员工管理、激活与模型网关闭环验证。仓库默认使用虚构示例企业“超智能战斗轮椅有限公司”；正式用于其他企业前，应按本文“企业定制项”替换品牌和服务地址。

本项目自有代码采用 [Apache License 2.0](LICENSE) 授权。DeepSeek、DeepSeek Harness 及第三方依赖的名称、商标和代码仍受各自权利人及许可证约束；本项目是独立的企业集成模板，不代表官方认可或隶属关系。

## 主要功能

- Windows 10/11 x64 桌面客户端，员工无需单独安装 .NET、Node.js、npm 或 pnpm；
- 固定版本的 Node.js 与 DeepSeek Harness Runtime，不修改上游 JavaScript 源码；
- 激活码激活，展示员工姓名、工号和部门；
- 员工添加、删除、启用、禁用和 CSV 导入；
- 激活码按小时、天、月、年或长期策略管理；
- LiteLLM 虚拟 Key 自动签发、撤销和轮换；
- 模型目录、月度金额、RPM、TPM 和并发配额管理；
- Windows Credential Manager 保存员工虚拟 Key；
- Harness 浅色/深色主题联动和 Windows 原生窗口行为；
- 安装包版本化 Runtime、启动诊断和更新清单校验能力；
- 管理网页与员工模型入口分端口部署。

## 系统架构

```text
Windows Desktop Shell
  ├─ WPF + WebView2
  ├─ DeepSeek Harness Runtime（仅监听 127.0.0.1）
  └─ Windows Credential Manager（保存员工虚拟 Key）
                 │
                 ▼
Nginx :8765 ──> 激活 API / LiteLLM OpenAI 兼容 API ──> DeepSeek API
                 │
                 ├─ Spring Boot ControlPlane
                 ├─ PostgreSQL
                 └─ Redis

管理员浏览器 ──> Nginx :8766 ──> Vue 管理网页 + ControlPlane 管理 API
```

详细边界和技术选型见 [系统架构](docs/ARCHITECTURE.md) 与 [威胁模型](docs/THREAT_MODEL.md)。

## 仓库结构

| 路径 | 内容 |
|---|---|
| `src/` | Windows 桌面端与核心类库 |
| `tests/` | .NET 自动化测试 |
| `services/control-plane/` | Java 21 + Spring Boot ControlPlane |
| `admin-web/` | Vue 3 管理网页 |
| `infra/litellm/` | Nginx、LiteLLM、PostgreSQL 和 Docker Compose 部署模板 |
| `runtime/` | Harness 版本锁定、企业策略与模块解析器 |
| `installer/` | Inno Setup 安装脚本 |
| `scripts/` | 构建、验证和验收脚本 |
| `docs/` | 架构、API、安全、UI 和部署文档 |

## 部署准备

### 服务端要求

- 一台可运行 Docker 与 Docker Compose v2 的 Linux 服务器；
- Java 21 和 Maven，用于构建 ControlPlane；
- Node.js 与 npm，用于构建管理网页；
- 企业自己的 DeepSeek API Key；
- 员工入口端口，默认 `8765`；
- 管理入口端口，默认 `8766`；
- 正式环境建议配置域名、HTTPS，并限制管理入口来源。

### Windows 构建机要求

- Windows 10/11 x64；
- PowerShell；
- pnpm `11.19.0`；
- Inno Setup 7；
- 项目锁定的 .NET SDK，可由仓库脚本安装到 `.tools/`。

## 服务端部署

### 1. 准备环境变量

进入部署目录并复制环境变量模板：

```bash
cd infra/litellm
cp .env.example .env
```

至少需要修改以下配置：

| 变量 | 用途 |
|---|---|
| `DEEPSEEK_API_KEY` | 企业上游 DeepSeek API Key |
| `LITELLM_MASTER_KEY` | LiteLLM 管理密钥，仅服务端使用 |
| `POSTGRES_PASSWORD` | LiteLLM 数据库密码 |
| `CONTROL_PLANE_POSTGRES_PASSWORD` | ControlPlane 独立数据库密码 |
| `REDIS_HOST` / `REDIS_PASSWORD` | Redis 地址与密码 |
| `ACTIVATION_PEPPER` | 激活码 HMAC Pepper，至少 32 个随机字符 |
| `CONTROL_PLANE_ADMIN_USERNAME` | 管理网页用户名 |
| `CONTROL_PLANE_ADMIN_PASSWORD` | 管理网页强密码 |
| `CNY_PER_USD` | 人民币预算换算汇率 |
| `LITELLM_PORT` | 员工入口端口，默认 `8765` |
| `CONTROL_PLANE_ADMIN_PORT` | 管理入口端口，默认 `8766` |

`.env`、数据库密码、DeepSeek Key、LiteLLM Master Key、签名私钥和证书密码不得提交到 Git。

### 2. 检查模型配置

根据企业 DeepSeek 账号实际拥有的模型权限修改：

```text
infra/litellm/config.yaml
```

ControlPlane 的模型目录保存在 PostgreSQL 中，管理员可以在管理网页启停员工可见模型。修改价格、预算上限或汇率时，需要同步检查 ControlPlane 校验规则与管理网页提示。

### 3. 构建 ControlPlane

在仓库根目录执行：

```bash
mvn -f services/control-plane/pom.xml clean test package
cp services/control-plane/target/company-harness-control-plane-*.jar \
  infra/litellm/control-plane/app.jar
```

### 4. 构建管理网页

```bash
npm --prefix admin-web ci
npm --prefix admin-web run test
npm --prefix admin-web run build
mkdir -p infra/litellm/admin-web
cp -R admin-web/dist/. infra/litellm/admin-web/
```

生成的 JAR 和前端 `dist` 只用于部署，均已通过 `.gitignore` 排除。

### 5. 启动 Docker 服务

```bash
docker compose \
  -f infra/litellm/compose.yml \
  --env-file infra/litellm/.env \
  up -d --build
```

查看状态和日志：

```bash
docker compose -f infra/litellm/compose.yml ps
docker compose -f infra/litellm/compose.yml logs --tail=200
```

默认入口：

- `http://<server>:8765/health/ready`：服务就绪检查；
- `http://<server>:8765/api/v1/activation/*`：员工激活接口；
- `http://<server>:8765/v1/*`：LiteLLM 模型接口；
- `http://<server>:8766/admin/`：管理网页。

首次部署后，在管理网页添加或导入员工，设置配额并生成一次性激活码，再由 Windows 客户端完成激活。

> 当前 Docker 模板便于验证完整链路。正式生产环境必须更换所有示例密码、启用 HTTPS、限制 `8766` 的访问来源，并建立 PostgreSQL 数据卷备份。

## 企业定制项

制作企业自己的安装包前，至少检查以下内容：

1. 在 `Directory.Build.props` 修改公司名、产品名、版本和程序集信息；
2. 以 `branding/brand-template.json` 为替换清单，并为新企业生成独立安装 `AppId`、技术标识和数据目录；
3. 替换 `info/logo-generated-source.png`，运行 `scripts/generate-brand-assets.ps1` 重建 Logo 与图标；
4. 检查 `admin-web/src/App.vue` 与 `installer/CompanyHarness.iss` 中的品牌文案；
5. 设置 ControlPlane 对员工返回的 LiteLLM 公网地址；
6. 设置 Windows 客户端激活服务地址 `COMPANY_HARNESS_CONTROL_PLANE_URL`；
7. 检查模型目录、预算上限、人民币汇率、联网搜索和遥测策略；
8. 正式分发前为安装包配置 Authenticode 代码签名。

当前参考实现的可见品牌、技术命名空间和测试示例均使用同一套虚构企业模板。它们不会影响架构复用，但为另一家企业制作安装包时仍须完整替换，不能只更换 Logo。

## Windows 客户端构建

### 1. 安装项目锁定的 .NET SDK

```powershell
.\scripts\bootstrap-dotnet.ps1
```

### 2. 构建并测试桌面端

```powershell
$dotnet = Join-Path $PWD '.tools\dotnet\dotnet.exe'
& $dotnet restore CompanyHarness.sln
& $dotnet build CompanyHarness.sln --no-restore
& $dotnet test CompanyHarness.sln --no-build
```

### 3. 准备固定 Harness Runtime

```powershell
.\scripts\prepare-harness-runtime.ps1
```

脚本会校验 Node.js 下载哈希，按锁文件安装指定版本的 DeepSeek Harness，并限制依赖安装脚本。Runtime 生成在 `artifacts/windows-harness-runtime`，不进入源码仓库。

### 4. 生成 Windows 安装包

```powershell
.\scripts\build-windows-installer.ps1 -StageOnly
.\scripts\build-windows-installer.ps1 `
  -InnoCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

输出文件：

```text
artifacts/installer/CompanyHarness-Setup-<version>-win-x64.exe
artifacts/installer/CompanyHarness-Setup-<version>-win-x64.json
```

JSON 文件包含安装包大小、SHA-256 和签名状态。详细构建和验收记录见 [Windows 安装包开发说明](docs/WINDOWS_INSTALLER.md)。

## 文档

- [项目生命周期](PROJECT_LIFECYCLE.md)
- [系统架构与技术选型](docs/ARCHITECTURE.md)
- [远端 Docker 部署模板](docs/REMOTE_TEST_ENVIRONMENT.md)
- [开源发布检查清单](docs/OPEN_SOURCE_RELEASE_CHECKLIST.md)
- [安全策略](SECURITY.md)
- [参与贡献](CONTRIBUTING.md)
- [威胁模型](docs/THREAT_MODEL.md)
- [API 契约](docs/openapi.yaml)
- [UI 页面与状态](docs/UI_STATES.md)
- [品牌与 Logo 资产](docs/BRAND_ASSETS.md)
- [LiteLLM 部署环境](infra/litellm/README.md)
- [Windows Runtime 集成](docs/WINDOWS_RUNTIME.md)
- [Windows 桌面壳开发说明](docs/WINDOWS_DESKTOP.md)
- [Windows 安装包开发说明](docs/WINDOWS_INSTALLER.md)
