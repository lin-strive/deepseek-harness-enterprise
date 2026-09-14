# 企业品牌模板

当前仓库使用虚构示例企业“超智能战斗轮椅有限公司”，用于演示开源项目如何与真实公司品牌解耦。所有可替换的品牌值汇总在 `brand-template.json`，代码中的对应值保持一致。

制作另一家企业版本时，至少需要同步替换：

1. 公司全称、产品名和安装程序 `AppId`；
2. Windows 数据目录、Credential Manager 目标名和单实例名称；
3. Java 基础包名与源码目录；
4. 激活码前缀、CSV 示例工号和测试数据；
5. Logo、应用图标、UI 强调色和无障碍名称；
6. Linux 部署目录、服务地址和发布文档。

Logo 图形源文件为 `info/logo-generated-source.png`。修改图形源文件后，在仓库根目录运行：

```powershell
.\scripts\generate-brand-assets.ps1 -CompanyName '你的公司全称'
```

脚本会生成方形图形标、横向组合标、兼容 Logo、管理网页 Logo 与 favicon。中文名称由 Windows 系统字体精确排版，不依赖图片生成模型生成文字。

不同企业版本必须使用不同的安装程序 `AppId` 和 Windows 数据目录，避免覆盖安装、凭据或历史会话互相污染。
