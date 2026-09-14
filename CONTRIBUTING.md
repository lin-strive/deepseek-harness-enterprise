# 参与贡献

感谢参与 DeepSeek Harness Enterprise。

## 开发原则

- 不侵入修改上游 DeepSeek Harness 源码；
- 不允许员工绕过企业网关配置个人模型 Provider；
- 不记录提示词、代码、回复或会话正文；
- Windows 客户端、ControlPlane、管理网页和部署模板保持边界清晰；
- 示例必须使用虚构品牌、保留域名或本机地址，不写入真实企业信息。

## 提交前检查

1. 从 `.env.example` 创建本机 `.env`，不要提交真实配置；
2. 运行 `pwsh ./scripts/verify-public-release.ps1`；
3. 运行 `pwsh ./scripts/verify.ps1`；
4. 对受影响的 Java、Vue 或安装器模块运行对应测试；
5. 更新相关文档和 `PROJECT_LIFECYCLE.md`。

提交应聚焦单一问题，说明行为变化、验证方式和安全影响。请勿在 Issue、PR、截图或日志中包含员工数据、激活码、虚拟 Key、上游 Key、真实服务器地址或管理凭据。
