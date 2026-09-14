# Windows Harness Runtime 集成

## 固定版本

- Node.js：`24.20.0`（Windows x64）；
- DeepSeek Harness：`@deepseek-ai/dsh@0.1.1-rc.2`；
- pnpm：`11.19.0`（仅用于构建运行时）；
- 完整依赖树：`runtime/harness.pnpm-lock.yaml`。

版本、下载地址和哈希记录在 `runtime/runtime-manifest.json`。客户端运行时不依赖员工电脑预装 Node.js、npm 或 pnpm。

构建使用 pnpm 的 `hoisted` 布局，避免 Windows 默认绝对 Junction 在安装目录变化后失效。已通过将完整 Runtime 重命名到另一目录再启动的迁移测试。

## 公司策略覆盖

桌面壳启动时从 ControlPlane 的 `/api/v1/models` 获取模型目录，并在本机应用数据目录原子生成 Cordis patch；服务器不可达时使用上次成功缓存。`runtime/company.cordis.yml` 只作为安装包内的安全后备模板：

- 默认模型由服务端目录指定，当前为 `deepseek-v4-flash`；
- 当前展示 `deepseek-v4-flash`、`deepseek-v4-pro`、`deepseek-v4-flash-vision-exp`；
- Vision 实验模型声明文本和图片输入能力；
- 普通聊天读取 `DEEPSEEK_BASE_URL`；
- 原生联网搜索读取 `DEEPSEEK_SEARCH_BASE_URL`，优先使用 `deepseek-v4-flash`；
- 两个地址均由桌面壳设置为同一个公司 LiteLLM `/v1`；
- 虚拟 Key 只从 `DEEPSEEK_API_KEY` 环境变量读取；
- 禁用个人 Provider、客户端模型配置页、插件配置页和遥测插件；模型启停和默认项由管理网页统一配置。

LiteLLM 暂时保留 `company-fast`、`company-coder`、`company-pro` 作为旧客户端兼容路由，新客户端不展示这些别名。管理员更改目录时，ControlPlane 通过 LiteLLM `/key/update` 同步全部有效虚拟 Key；员工无需重新激活，重启 Harness 即可刷新目录。

配置文件不包含任何 Key。即使员工把公司虚拟 Key 用到 DeepSeek 官方地址，官方也不会接受 LiteLLM 虚拟 Key。

## 启动边界

`HarnessProcessManager` 执行以下约束：

- Harness 仅监听 `127.0.0.1` 和随机空闲端口；
- 网关必须使用 HTTP 或 HTTPS；当前 HTTP 仅用于内部测试；
- 网关 API Base 必须以 `/v1` 结尾；
- 启动参数带公司 Cordis patch；
- 清除继承的常见个人模型 Provider Key 和 Base URL；
- 聊天与搜索注入同一公司网关地址；
- 同时使用配置覆盖和环境变量关闭遥测；
- 启动失败或超时后终止整个 Harness 进程树。

本地管理员仍能修改本机文件或运行其他客户端，因此桌面壳不是强安全边界。生产环境需要配合网络出口策略；服务端虚拟 Key 的模型白名单、预算和限流始终是最终控制点。

## 已完成验证

2026-09-04 已完成：

- 固定依赖构建成功，pnpm 冻结锁文件校验通过；
- 随包 Node.js 24.20.0 能启动随包 Harness；
- Harness Web 在回环地址返回 HTTP 200；
- 真实 Harness 对话经 SSH 隧道和公司 LiteLLM 返回预期结果；
- Harness 内置 `web_search` 经 LiteLLM `/v1/messages` 返回官方仓库地址；
- 临时 LiteLLM Key 已撤销，测试端口和进程无残留；
- .NET 构建零警告，19 项自动化测试通过。

生产客户端不得使用当前公网 HTTP 地址。员工试点前必须为网关配置 HTTPS，并限制 `8765` 的公网来源或关闭公网直连。
