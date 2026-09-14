# Windows 安装包开发说明

## 交付目标

安装包面向 Windows 10/11 x64 员工电脑，采用当前用户安装模式，默认目录为：

```text
%LOCALAPPDATA%\Programs\CompanyHarness
```

安装过程不请求管理员权限，并包含自包含 .NET 8 桌面程序、固定 Node.js、固定 DeepSeek Harness Runtime、公司策略和品牌资源。Runtime 使用 `runtime-<客户端版本>` 独立目录，避免升级时新旧客户端共享同一路径。员工不需要单独安装 .NET、Node.js、npm 或 pnpm。

打包只保留 Windows x64 所需的 `node-pty` 预编译文件，排除 macOS、Linux、Windows ARM64 文件和原生调试符号；不会修改 Harness JavaScript 代码。

## 构建

先准备固定 Runtime：

```powershell
.\scripts\prepare-harness-runtime.ps1
```

只生成并验证安装源目录：

```powershell
.\scripts\build-windows-installer.ps1 -StageOnly
```

完整生成安装 EXE：

```powershell
.\scripts\build-windows-installer.ps1
```

也可以明确指定已安装的 Inno Setup 编译器：

```powershell
.\scripts\build-windows-installer.ps1 `
  -InnoCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

默认版本读取 `Directory.Build.props` 的 `VersionPrefix`，也可传入 `-Version 0.1.1`。版本必须使用 `major.minor.patch` 数字格式。

## 构建产物

| 位置 | 内容 |
|---|---|
| `artifacts/windows-installer-stage/app` | 可直接运行的自包含发布目录和固定 Runtime |
| `artifacts/windows-installer-stage/package-layout.json` | 版本、文件数量、体积和组件版本清单 |
| `artifacts/installer/CompanyHarness-Setup-<version>-win-x64.exe` | 员工安装包 |
| `artifacts/installer/CompanyHarness-Setup-<version>-win-x64.json` | 安装包大小、SHA-256 和签名状态 |

`artifacts` 属于构建产物，不提交版本库。

### 2026-09-09 首次完整构建验证

- Inno Setup 7.1.0 编译成功且无安装脚本警告；
- `CompanyHarness-Setup-0.1.0-win-x64.exe` 大小为 100,662,025 字节（约 96 MB）；
- SHA-256：`67014a7dc6f4d071b789d919ce9020f6bdead5b56b08f373008e8cd240ad0dd4`；
- 元数据清单中的哈希与安装包实测一致；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-09 0.2.0 构建与安装验证

- `CompanyHarness-Setup-0.2.0-win-x64.exe` 大小为 100,677,579 字节；
- SHA-256：`3c78956b4c872d1c7ecdd47519d19845406eeb22ec5c71cb9977a0a17a4064c9`；
- 在隔离测试目录执行真实静默安装后，确认 `runtime/node/node.exe` 存在，大小为 93,381,448 字节；
- 安装目录内 Node.js 返回 `v24.20.0`，Harness 返回 `0.1.1-rc.2`；
- 静默卸载返回 0 且测试安装目录已清理；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-09 0.2.1 热修复验证

- 修复 0.2.0 动态模型目录生成 YAML 时 `models:` 与首个模型缺少换行、导致 Harness 启动即退出的问题；
- `CompanyHarness-Setup-0.2.1-win-x64.exe` 大小为 100,665,235 字节（约 96 MB）；
- SHA-256：`f84fabc486a0ad539624d6855d6fc6c81a7736556484f43fb3a28aeeb4eb29e5`；
- Release 自动化测试 19/19 通过；
- 使用真实安装目录和修复后生成器配置启动 Harness 成功，随后静默卸载返回 0；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.2 首次启动恢复验证

- 安装器启动客户端时传入安装器进程号；客户端等待安装器退出并额外留出 500ms 文件释放时间，再启动本地 Harness；
- 启动失败最多自动尝试三次，每次均终止旧 Harness 进程、释放旧 WebView2 控件并创建全新实例；最终失败后手动“重新启动”也从干净状态开始；
- `CompanyHarness-Setup-0.2.2-win-x64.exe` 大小为 100,671,340 字节（约 96 MB）；
- SHA-256：`335e6f9bd0357ad91f8e0528607906c9b2ec3777f29ba263dbdac468cd2ead86`；
- Release 自动化测试 23/23 通过，桌面项目构建 0 警告、0 错误；
- 进程协调实测确认：安装器模拟进程退出前不会启动 Node，退出后 Harness 正常启动；隔离目录静默安装确认 Node.js `v24.20.0`，随后卸载成功且目录清理完成；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.3 跨进程恢复验证

- 现场确认 0.2.2 安装目录中的 Node.js、Harness 入口和动态模型配置完整；相同 Runtime 独立启动返回 HTTP 200，残余故障定位到首次安装宿主进程中的 WebView2 初始化；
- 同进程三次恢复仍失败时，客户端自动启动恢复进程；恢复进程先等待旧实例完全退出，再取得单实例所有权并继续启动；
- 恢复启动只自动执行一次，故障注入验证旧进程退出、恢复进程接管且没有第三个进程，避免无限重启；
- 恢复进程仍失败时生成 10 位诊断编号，并提供脱敏 `logs/startup.log`；日志不记录激活码、虚拟 Key、提示词、代码或回复；
- `CompanyHarness-Setup-0.2.3-win-x64.exe` 大小为 100,688,261 字节（约 96 MB）；
- SHA-256：`d75fc7e07c33fb67664389833f9d473d053c6a7f6b7591909216d4a2bdf49be0`；
- Release 自动化测试 25/25 通过，桌面项目构建 0 警告、0 错误；覆盖安装到 `D:\app\CompanyHarness` 后验证应用 0.2.3、Harness Node 子进程和 6 个公司 WebView2 进程均正常运行；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.4 Profile 模块链接修复

- 诊断编号 `F6F678BE8E` 确认 Harness 退出码 1 的原因是用户 Profile 加载 `@deepseek-ai/dsh-client-ui-permission-presets` 时出现 `ERR_MODULE_NOT_FOUND`；
- 正式 Web 服务前新增独立官方 `--profile web --dump-default-config` 预检进程，先生成或重新指向模块回退链接，再由全新 Node 进程加载 Harness；不修改 DeepSeek Harness 源码；
- 修复 WPF 异步取消 `Closing` 导致窗口停留：关闭时同步终止本地 Harness 进程树，随后由 Windows 原生窗口流程退出；预检期间关闭也会终止预检子进程；
- 故意将 400 多个模块链接指向旧 Runtime 后，0.2.4 能自动修正并启动；连续 3 轮启动、WebView2 加载、关闭全部成功，Node 子进程无残留；
- 使用最终安装包覆盖 `D:\app\CompanyHarness` 后再次注入旧 Runtime 链接，安装态成功修正到 `D:\app\CompanyHarness\runtime`、启动真正带 `--patch` 的 Web 服务，并正常关闭窗口及 Node 进程树；
- `CompanyHarness-Setup-0.2.4-win-x64.exe` 大小为 100,690,353 字节（约 96 MB）；
- SHA-256：`eb88cd2d25a4ab73dd60e1a11e31ae3c57b62562d0bbdaa905dbe29d372cd5a9`；
- Release 自动化测试 26/26 通过，桌面项目构建 0 警告、0 错误；当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.5 Web Profile 固定 Runtime 桥接

- 诊断编号 `D02B1E9A4C` 再次确认 0.2.4 的官方 Profile 预检不足以保证当前 Node 进程解析到新 Runtime，仍会因 `@deepseek-ai/dsh-client-ui-permission-presets` 无法解析而以代码 1 退出；
- 在官方 Profile 初始化后，为当前用户建立 `profiles/web/node_modules` Junction，直接指向软件自带固定 Runtime 的 `harness/node_modules`；每次启动自动校验并在升级后重新指向当前 Runtime，不复制依赖、不修改 DeepSeek Harness 源码；
- 正式 Web 进程启动前由独立 Node 进程执行 ESM 导入探针，关键依赖不可用时立即记录脱敏诊断，不再让员工进入无法恢复的 WebView2 重试循环；辅助进程不接收员工虚拟 Key；
- Release 自动化测试 28/28 通过，桌面项目构建 0 警告、0 错误；开发态连续三轮启动均取得 HTTP 200，保持运行稳定，窗口关闭后 Node 子进程均无残留；
- 将桥接故意指向旧 Runtime 后重新启动，程序成功自动改指向当前 Runtime 并取得 HTTP 200；0.2.5 覆盖安装到 `D:\app\CompanyHarness` 后，又从开发 Runtime 自动切换到安装 Runtime，连续两次返回 HTTP 200；
- 安装器完成页的 `--post-install --wait-for-process` 路径已复测：安装进程结束前不启动 Node，结束后正常返回 HTTP 200，窗口关闭后子进程无残留；
- `CompanyHarness-Setup-0.2.5-win-x64.exe` 大小为 100,678,661 字节（约 96 MB）；
- SHA-256：`dd46f9df053c08996fe1c932d01628d88b588c4a236fbeed7379fef785849950`；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.6 单次启动与版本化 Runtime

- 诊断编号 `AD45EB0FBD` 证明 0.2.5 的失败发生在额外 Node ESM `eval` 探针，而不是正式 Harness：相同 Node、工作目录、桥接和目标包稍后手工导入成功，属于保护性检查瞬时误判；
- 删除 ESM 导入探针和独立官方预检，桌面程序直接确认固定 Runtime 的必要模块文件存在且可读，建立桥接后只启动一次正式 Harness，并以正式 HTTP 健康检查作为唯一成功标准；
- 删除三次自动启动循环和受控跨进程恢复；失败时清理本次进程并留在当前窗口，由员工明确点击后才重试；
- 安装包将 Runtime 写入 `runtime-0.2.6`，升级时清理旧 `runtime`/`runtime-*` 后写入全新版本目录，避免覆盖仍被 Profile 链接引用的同一路径；
- Release 自动化测试 27/27 通过，桌面项目构建 0 警告、0 错误；真实安装完成页流程中，旧桥接从已删除的旧 Runtime 一次性改指向 `D:\app\CompanyHarness\runtime-0.2.6`，只产生一个正式 Harness 子进程并连续三次返回 HTTP 200，诊断日志无新增失败，关闭后无 Node 残留；
- `CompanyHarness-Setup-0.2.6-win-x64.exe` 大小为 100,688,729 字节（约 96 MB）；
- SHA-256：`8ca0aac1319ba0bfbf147abb5d6e2e192b9e1e96e147b14d0d3c4265622ed3e6`；
- 当前安装包尚未进行公司 Authenticode 签名。

### 2026-09-10 0.2.7 固定 Runtime 模块解析器

- 0.2.6 安装完成页启动日志确认正式 Harness 仍可能在 Windows 刚完成安装时无法通过 Profile Junction 解析 `@deepseek-ai/dsh-client-ui-permission-presets`；Junction 和目标文件随后均存在，同一命令手工运行又成功，说明该链接方案存在首进程可见性问题；
- 移除桌面端创建的 `profiles/web/node_modules` Junction。正式 Node 使用 `--import company-module-resolver.mjs` 注册同步解析钩子：默认解析失败时才从当前版本化 Runtime 解析裸包名，并通过真实路径校验禁止越出 Runtime；
- 解析器随 Runtime 打包，Runtime 定位器将其列为必需文件；不修改 DeepSeek Harness 源码、不复制用户依赖、不读取提示词、代码、会话或虚拟 Key；
- 先在完全没有 Profile `node_modules` 的隔离目录中验证目标依赖导入和完整 Harness HTTP 200，再在真实安装完成页流程中删除旧 `web/node_modules` 及目标包链接进行故障注入；0.2.7 只启动一个正式 Harness，连续三次 HTTP 200，且 `web/node_modules` 始终不存在；
- Release 自动化测试 27/27 通过，桌面项目构建 0 警告、0 错误；诊断日志无新增失败，关闭窗口后 Node 子进程无残留；
- `CompanyHarness-Setup-0.2.7-win-x64.exe` 大小为 100,679,426 字节（约 96 MB）；
- SHA-256：`bac63d03759a7c264381b0eb4a31c997680a262d96a94e15c0162b7deec15546`；
- 当前安装包尚未进行公司 Authenticode 签名。

## 安装行为

- 仅允许 x64 兼容系统，最低 Windows 10 1809；
- Inno Setup 根据实际文件自动计算磁盘需求，并额外预留 200MB 给 WebView2 数据、日志和更新暂存；
- 创建开始菜单快捷方式，桌面快捷方式默认不选；
- 升级安装会请求关闭正在运行的 `CompanyHarness.exe`；
- 安装完成后可以直接启动超智能 Harness；勾选完成页启动时，客户端会等待安装器收尾后再启动固定 Runtime；
- 重复启动只保留一个进程，并唤醒已有窗口。

## WebView2

构建脚本从微软官方固定跳转地址取得 Evergreen Bootstrapper，并强制验证 Authenticode 状态为 `Valid`、签发者包含 `Microsoft Corporation`，之后才放入安装源。

安装器检查微软公布的 WebView2 Runtime 注册表项。系统已安装时不会运行 Bootstrapper；缺失时执行：

```text
MicrosoftEdgeWebview2Setup.exe /silent /install
```

参考：[微软 WebView2 分发说明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。

## 卸载与数据

普通卸载默认保留：

```text
%LOCALAPPDATA%\SmartWheelchair\CompanyHarness
```

以及 Windows Credential Manager 中的员工虚拟 Key。这样重新安装后可以继续使用。

非静默卸载时会询问是否同时清除本地激活信息、Harness 会话、WebView2 数据和外观设置，默认选择“否”。只有员工明确选择“是”时，卸载器才调用 `CompanyHarness.exe --clear-user-data`。客户端会先校验目标目录必须是预期公司数据目录，再删除数据和凭据；静默卸载永远保留用户数据。

## 签名和许可

- 当前脚本生成 SHA-256 和签名状态，但公司代码签名证书尚未接入；正式试点安装包必须完成 Authenticode 签名。
- 项目是否开源与 Windows 代码签名相互独立：开源本身不强制要求证书，但公开分发 Windows EXE 时仍建议签名；签名私钥和证书密码不得进入公开仓库。
- Inno Setup 官方说明，公司内部使用同样属于商业使用场景，生产使用前应完成相应许可。开发阶段可以先完成安装包验证。参考：[Inno Setup 商业许可说明](https://jrsoftware.org/isorder.php)。
