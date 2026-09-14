# Windows 单层标题栏设计 QA

## 本轮范围

- 品牌证据：`info/logo-v2-mark.png`、`info/logo-v2-horizontal.png` 与 `branding/brand-template.json`；
- 实现证据：`MainWindow.xaml`、`MainWindow.xaml.cs`、`App.xaml` 与外观偏好存储；
- 自动检查：`scripts/verify-brand-template.ps1`、WPF Release 构建和核心测试；

## 验收结果

| 检查项 | 结果 | 证据 |
|---|---|---|
| 去除系统标题栏 + 64px 公司页头的双层结构 | 通过 | 主内容仅保留 48px `TitleBarRoot`，`HarnessRoot` 直接位于第二行 |
| 深色视觉连续性 | 通过 | 深色状态使用石墨标题栏；运行时资源与 Harness 深色状态联动 |
| 浅色视觉连续性 | 通过 | 浅色状态使用 `#F7F9FB`；支持 Harness、Windows 与固定浅色策略 |
| 身份与操作层级 | 通过 | 姓名是菜单入口，部门/工号、重启与退出收纳于菜单；运行状态独立显示 |
| Windows 窗口语义 | 通过 | 拖动区、边缘缩放、最大化/还原、最小化、关闭、贴靠命中、系统菜单均已实现 |
| 可访问性 | 通过 | 窗口按钮、员工菜单、外观单选均有可访问名称；状态使用 LiveSetting，非仅颜色表达 |
| 上游零侵入 | 通过 | 未修改 Runtime；主题桥只上报 `light`/`dark` |
| 品牌模板隔离 | 通过 | 独立 AppId、安装目录、应用数据目录、凭据目标和单实例名称均与旧企业版本分离 |
| 回归验证 | 通过 | `CompanyHarness.Desktop` Release 构建 0 警告/0 错误；核心测试 27/27 通过；安装器预装配成功 |

## 已知边界

- “检查更新”仍属于后续更新中心阶段；本轮只为其保留员工菜单的信息架构位置，不提供无效按钮。
- 模板化时已移除包含旧企业名称和 Logo 的历史截图，避免开源仓库泄露已退役品牌。正式发布新的企业版本前，仍需在 Windows 10/11 与 100%/125%/150% DPI 上重新生成深浅主题截图矩阵。

final result: passed
