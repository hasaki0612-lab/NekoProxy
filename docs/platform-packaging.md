# 跨平台构建与验证边界

需要 .NET 10 SDK（版本见 global.json）、Python 3 和对应平台原生依赖。源码沿用 Avalonia / C# / MVVM；生产凭据使用 Windows DPAPI、Linux Secret Service 或 macOS Keychain，不提供明文降级。

运行 `pwsh -File scripts/build.ps1` 进行本地构建和测试。需要真实客户端核心和本机隔离 SSH 夹具的验证见 `scripts/Test-CI.ps1`；不要让测试连接私人服务器。完整测试需要对应原生核心、桌面用户上下文和已解锁的系统密钥环。

## 自包含候选

```sh
python scripts/package.py --rid win-x64 --output artifacts/win-candidate
python scripts/package.py --rid linux-x64 --output artifacts/linux-x64-candidate
python scripts/package.py --rid linux-arm64 --output artifacts/linux-arm64-candidate
python scripts/package.py --rid osx-x64 --output artifacts/mac-x64-candidate
python scripts/package.py --rid osx-arm64 --output artifacts/mac-arm64-candidate
```

每次使用新目录。脚本默认自包含 .NET；仍需系统原生桌面库。`--publish-dir` 可包装已有对应架构的真实发布目录。

## 安装器

具体命令见 [packaging/INSTALLERS.md](../packaging/INSTALLERS.md)。Windows 使用官方 Inno Setup 6；DEB 使用标准 ar/control/data 结构；Arch 使用标准 PKGBUILD 与非 root makepkg；DMG 必须在 macOS 由 hdiutil 生成。

`packaging/macos/build-dmg.sh` 在输出目录内复制 .app，再运行 `seal-bundle.py`。该步骤将托管资源和原生库放入适当目录、保留内部相对查找链接，检查代码/图标不变并补齐 ad-hoc 资源签名。不会修改输入候选、购买开发者身份或关闭系统安全保护。ad-hoc 不提供开发者身份，不能替代 Developer ID、公证或 Gatekeeper 验收。

## 数据目录

Windows 保持 `%LOCALAPPDATA%/NekoProxy`；Linux 遵守 XDG 数据/配置/状态/缓存目录；macOS 使用用户 Library 中的应用支持和缓存目录。`--data-dir` 用于隔离测试，不要覆盖真实用户资料。卸载不应清除用户配置、凭据和导出。

## v0.6.0 已有验证边界

Windows 安装、启动、卸载及数据保留已有本机验证；DEB 已完成结构检查，未原生安装验收；Arch 已在官方 Arch userspace 容器内完成安装、Keyring、X11/Xvfb 窗口启动和卸载，容器内核来自宿主。两种 macOS 架构已完成原生 DMG、资源签名、Keychain 与窗口初始化；没有完整人工 GUI 验收，没有 Developer ID 或 Apple 公证，Gatekeeper 拒绝。

本仓库的 desktop-candidates 工作流仅手动运行，不自动发布 Release。历史安装包的验证不等于该完整源码工作流已执行过。窗口启动 smoke 产生的是附着视觉树 PNG，不是桌面录屏或人工 GUI 验收。不要公开上传测试密钥、临时密钥环、真实节点配置或原始私人验收记录。
