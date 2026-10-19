![NekoProxy Logo](packaging/icons/source.png)

# NekoProxy

**让网络部署更简单，让服务器管理更直观。** (｡･ω･｡)ﾉ♡

NekoProxy 是一款开源、跨平台的**图形化网络协议部署与服务器管理桌面应用**。

基于 .NET 10、C# 与 Avalonia UI 构建，采用轻盈的 Liquid Glass 设计语言。无需记忆复杂的 Linux 命令，也能通过直观的图形界面部署网络协议、管理自己的 VPS。

项目正在持续开发与维护中.....

## ✨ 核心功能

- **一键部署**：通过 SSH 自动完成环境检查、网络协议配置与服务部署。
- **四种代理协议**：支持 VLESS + REALITY、Hysteria 2、Trojan TLS 和 Shadowsocks 2022。
- **图形化管理**：管理 VPS、代理节点、服务状态、SSH 终端与实时资源监测。
- **客户端配置**：支持生成 sing-box、Mihomo / Clash.Meta 配置及兼容的 Shadowrocket 节点链接。
- **隐私与安全**：本地凭据保护、DNS / IPv6 策略、智能分流、加密备份及脱敏诊断。
- **个性化界面**：Liquid Glass 视觉风格，支持深浅色主题及多语言界面。

未来还计划加入**异地组网**功能，暂定支持两种组网协议，目前尚未正式实现。

## 🖥️ 平台支持

公开版本与安装附件以 [GitHub Releases](https://github.com/hasaki0612-lab/NekoProxy/releases) 为准。

| 系统 | 架构 | 安装包 |
|---|---|---|
| Windows | x64 | `NekoProxy-Setup.exe` |
| Debian / Ubuntu | x64 / ARM64 | `.deb` |
| Arch Linux | x64 | `.pkg.tar.zst` |
| macOS | Intel / Apple Silicon | 适配中，暂不提供公开下载 |

Windows 版本已包含 .NET 运行时，下载安装即可使用。

目前 Windows 版已完成本机安装测试，Linux 和 macOS 版本还没有经过完整的实机测试。如果遇到兼容性问题，欢迎在 [Issues](https://github.com/hasaki0612-lab/NekoProxy/issues) 里反馈喵！(｡･ω･｡)ﾉ♡

macOS 候选尚未完成 Developer ID 签名和 Apple 公证，并受 Gatekeeper 阻断，暂不公开分发。

## 🔐 隐私与安全

**Local-first · 本地优先**

我们希望用户始终掌握自己的数据。

- VPS 信息、代理节点及认证凭据保存在本地，不上传至 NekoProxy 云端。
- 使用系统安全存储保护敏感凭据：Windows DPAPI、macOS Keychain 与 Linux Secret Service。
- 通过 SSH 与用户自己的服务器通信，保留主机指纹及证书验证机制。
- 提供 DNS、IPv6 与分流策略，帮助减少错误配置与意外泄漏风险。

部分真实网络场景仍在验证中，不能保证所有环境均不存在泄漏。

##  快速开始

Windows 用户在正式安装包发布后，只需：

**下载安装包 → 双击安装 → 从桌面启动 NekoProxy**

首次使用时，添加自己拥有或获授权的 VPS，核对 SSH 主机指纹，即可根据图形化向导进行部署和管理。

## 📖 开源许可

NekoProxy 基于 [MIT License](LICENSE) 开源。

欢迎自由使用、学习、修改、二次开发及分发，但请遵守许可证要求，保留原始版权与许可声明。

也欢迎通过 GitHub Issues 和 Pull Requests 提交建议、反馈问题或参与贡献！(｀・ω・´)

## 📋 项目声明

**软件说明**

NekoProxy 是一款跨平台的图形化网络协议部署与服务器管理应用，仅提供相关软件工具及功能，不提供代理节点、网络接入、服务器托管或其他额外网络服务。

如对网络连接、服务可用性或网络服务质量存在疑问，请联系您所使用的网络服务提供商。软件自身的问题可通过 GitHub Issues 反馈。

**使用提醒**

使用本工具时，请遵守所在国家或地区适用的法律法规。禁止利用本工具所部署的网络协议、代理服务或隧道，绕过当地网络审查、未经授权访问网络系统或数据库，以及从事其他违法违规活动。

本声明不构成对 MIT 开源许可证的附加许可限制。

## 💙 鸣谢

<!-- 在这里添加项目贡献者、AI 开发助手及特别鸣谢 (｡･ω･｡)♡ -->
特别感谢 **[SepMiao](https://github.com/SepMiao)** 为NekoProxy的验收工作提供了测试VPS，帮大忙了喵 o((>ω< ))o ♡ 