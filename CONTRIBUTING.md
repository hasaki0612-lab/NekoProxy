# 参与开发

使用 .NET 10 SDK，运行 `scripts/build.ps1`；平台依赖和打包命令见 [跨平台构建说明](docs/platform-packaging.md)。服务接口置于 Core，实现放到 Infrastructure / Deployment / Exporters；Desktop 只负责 UI 编排。

提交前运行 Release 构建和相关测试。界面改动必须运行真实 Avalonia 窗口并检查普通与较小窗口。新协议必须提供独立导出器与有效配置验证，不能在 ViewModel 中拼接 JSON。

第三阶段需使用明确授权的隔离测试 VPS。先实现主机密钥验证与只读预检，再加入带所有权标记、备份、原子写入和回滚的部署。不得覆盖其他工具配置，不得在普通日志记录认证材料。
