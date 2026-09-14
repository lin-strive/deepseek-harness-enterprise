# Windows 桌面壳开发说明

桌面壳使用 .NET 8 + WPF + WebView2，在员工电脑上启动固定版本 Harness，并把本地 Harness 页面嵌入公司窗口。桌面壳不包含 DeepSeek 上游 Key。

## 已实现范围

- 公司品牌首屏和 Windows 窗口图标；
- 一次性激活码验证及姓名、工号、部门确认；
- 员工虚拟 Key 写入 Windows Credential Manager；
- 不含密钥的员工资料写入本地 JSON；
- 固定 Runtime 定位、Harness 子进程启动、健康检查和进程树关闭；
- WebView2 仅允许本机 Harness 地址导航；
- 清除个人 Provider 环境变量并统一注入公司网关；
- 48px 单层公司标题栏，不再在 Harness 上方叠加第二层公司页头；
- 自绘窗口保留拖动、双击最大化、边缘缩放、Windows 11 最大化贴靠入口、右键/Alt+Space 系统菜单和标准关闭行为；
- 员工菜单集中展示姓名、部门、工号、外观设置、重启和退出；
- 默认通过 WebView2 的只读主题桥跟随 Harness 浅色/深色，亦可跟随 Windows 或固定浅色/深色；
- 每个 Windows 用户只运行一个实例，重复启动时恢复并激活已有窗口；
- 支持卸载器调用的受限本地数据清理模式，删除前校验固定数据目录；
- 安装后首次启动会等待安装器完成收尾；依赖桥接完成后只启动一次正式 Harness，并以 HTTP 健康检查作为成功标准，不再自动连续重试或跨进程重启；
- 恢复进程仍失败时显示诊断编号和“打开诊断目录”，脱敏日志只记录版本、异常类型、退出码和启动期输出，不记录激活码、虚拟 Key、提示词、代码或回复；
- 正式 Web 服务通过 Node `--import` 注册公司模块解析器：正常解析优先，仅当用户 Profile 无法解析裸包名时回退到当前版本化 Runtime，并拒绝解析到 Runtime 之外；不再创建或依赖 `profiles/web/node_modules` Junction，不修改 DeepSeek Harness 源码，也无需复制依赖；
- 可使用独立测试数据目录，不污染正式激活信息。

安装源、单实例和卸载数据策略已实现；托盘、工作区选择、客户端更新中心、正式安装包签名仍在后续计划中。

## Visual Studio 启动

```powershell
.\scripts\bootstrap-dotnet.ps1
.\scripts\prepare-harness-runtime.ps1
```

打开 `CompanyHarness.sln`，将 `CompanyHarness.Desktop` 设为启动项目。ControlPlane 已改为服务器上的 Spring Boot 服务，不再是解决方案中的 .NET 启动项目。

也可以直接运行：

```powershell
.\src\CompanyHarness.Desktop\bin\Debug\net8.0-windows\CompanyHarness.exe
```

## 本地数据和密钥

| 内容 | 默认位置 |
|---|---|
| 员工资料、网关地址、额度摘要 | `%LOCALAPPDATA%\SmartWheelchair\CompanyHarness\profile.json` |
| 员工虚拟 Key | Windows Credential Manager，目标名 `SmartWheelchair.CompanyHarness.VirtualKey` |
| Harness 用户数据 | `%LOCALAPPDATA%\SmartWheelchair\CompanyHarness\harness` |
| WebView2 用户数据 | `%LOCALAPPDATA%\SmartWheelchair\CompanyHarness\webview2` |
| 桌面壳外观偏好 | `%LOCALAPPDATA%\SmartWheelchair\CompanyHarness\shell-preferences.json` |

`profile.json` 不保存虚拟 Key。测试时可用绝对路径环境变量 `COMPANY_HARNESS_DATA_ROOT` 隔离非密钥数据，并用 `COMPANY_HARNESS_CREDENTIAL_TARGET` 指定测试凭据目标。

## 标题栏与主题联动

桌面壳不修改 Harness 源码或构建产物。WebView2 在页面创建时注入一段独立、只读的主题检测脚本，只向 WPF 外壳发送 `light` 或 `dark` 两个值；不读取会话、提示词、代码或账号数据。Harness 切换主题后，标题栏和员工菜单同步换色，WebView2 主体仍由 Harness 自己渲染。

员工菜单提供四种外观策略：

- 跟随工作台（默认）：Harness 已加载时跟随 Harness，加载前回退为 Windows 外观；
- 跟随 Windows：只响应系统应用主题；
- 浅色：固定浅色外壳；
- 深色：固定深色外壳。

系统高对比度优先于以上选项。外观偏好不属于敏感信息，保存为独立 JSON，不进入 Credential Manager。

## Runtime 查找规则

1. EXE 同目录下与客户端版本匹配的 `runtime-<版本>`；
2. EXE 同目录下的兼容目录 `runtime`；
3. 从 EXE 目录向上查找 `artifacts/windows-harness-runtime`。

开发时使用第三种布局；制作安装包时将 Runtime 复制到 EXE 同级的版本化目录，例如 `runtime-0.2.6`。升级安装先关闭旧客户端、清理旧 Runtime，再写入全新的版本目录，避免新旧版本共享或覆盖同一路径。

## 激活服务地址

客户端开发环境默认值：

```text
http://127.0.0.1:8765/
```

可通过环境变量覆盖：

```powershell
$env:COMPANY_HARNESS_CONTROL_PLANE_URL = 'https://harness.example.com/'
```

客户端允许 HTTP/HTTPS 以便本机开发；正式员工环境必须使用公司域名、HTTPS 和受信任证书。真实闭环由服务器 Spring Boot ControlPlane 调用 LiteLLM 管理接口签发员工虚拟 Key。

## 已完成验收

截至 2026-09-10，已完成单层标题栏、浅深主题桥、四档外观策略、单实例唤醒、自包含安装源、Runtime 白名单打包、微软 WebView2 引导程序签名校验、卸载数据策略、版本化 Runtime 和受限模块解析器；桌面项目构建为 0 警告、0 错误，核心自动化测试 27/27 通过。0.2.7 已按安装完成页流程完成无 Profile 链接故障注入：只产生一个正式 Harness 子进程，连续三次 HTTP 200，诊断日志无新增失败，窗口关闭后无 Node 残留。此前已完成品牌界面、无效码错误态、员工身份确认、Credential Manager、Harness 冷启动、WebView2 加载和进程树退出验收。远端 ControlPlane 迁移为 Spring Boot 后，又验证了原激活数据兼容、姓名返回、四类配额、公司模型白名单和 HTTP 测试入口。
