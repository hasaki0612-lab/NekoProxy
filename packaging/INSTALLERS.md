# 本地安装包构建与验收

本文是独立打包说明，不修改仓库 README。全部输出应选择新的目录；不自动提交、上传、发布，不访问 VPS。

## 正式 Logo

唯一源文件是用户提供的透明 PNG。先核对原图 SHA-256，再运行：

```sh
python scripts/generate-icons.py --source /absolute/formal-logo.png --expected-sha256 <原图SHA256>
```

脚本保留原画布比例和透明通道，只进行等比缩放/格式转换，拒绝旧三圆环图标。Windows 使用 16/20/24/32/48/64/128/256 多尺寸 ICO；Linux PNG 512；macOS ICNS 由 1024 画布转换。`provenance.json` 保存来源和输出哈希。

资源变化后需要重新发布程序，使 Avalonia 内嵌资源和 Windows EXE 同时更新；仅替换外部 PNG/ICNS 不足以更新所有窗口图标。

## Windows x64

```powershell
python scripts/package.py --rid win-x64 --output <新的候选输出目录>
ISCC.exe "/DPublishDir=<绝对 publish-win-x64 路径>" "/DInstallerOutput=<新的安装器输出目录>" installer/NekoProxy.iss
```

结果为 `NekoProxy-Setup.exe`。Inno Setup 6 编译器需先获得用户授权后从官方渠道安装。默认当前用户安装，不要求管理员；桌面快捷方式自动创建，开始菜单可选，完成页可启动应用，静默安装不自动启动。卸载只移除程序、快捷方式和安装记录，不删除独立的用户数据。

安装验收应使用隔离账户/环境。现有 `Test-Installer.ps1` 限定临时 GitHub Runner；不得通过伪造环境变量在用户桌面绕过该保护。如果现有安装或快捷方式可能被覆盖，暂停实际安装。编译成功、安装、启动、卸载及用户数据保留必须分别记录。

## Debian / Ubuntu

先发布包含正式 Logo 的 Linux 自包含候选，再封装：

```sh
python scripts/package.py --rid linux-x64 --output <新目录>
python scripts/build-deb.py --archive <新目录>/NekoProxy-0.6.0-linux-x64.tar.gz --arch amd64 --logo-sha256 <原图SHA256> --output <新的deb输出目录>
python scripts/package.py --rid linux-arm64 --output <另一个新目录>
python scripts/build-deb.py --archive <另一个新目录>/NekoProxy-0.6.0-linux-arm64.tar.gz --arch arm64 --logo-sha256 <原图SHA256> --output <新的arm-deb输出目录>
```

生成标准 Debian 2.0 ar 包，包含 `debian-binary`、`control.tar.xz`、`data.tar.xz`，并生成文件 MD5 和 SHA-256 清单。本机交叉打包不能替代 `dpkg` 实际安装。

程序位于 `/opt/nekoproxy`，`/usr/bin/nekoproxy` 为启动链接，应用菜单与图标位于 `/usr/share/applications` 和 `/usr/share/icons/hicolor/512x512/apps`。包不包含任何 `/home`、用户配置或节点凭据，无安装/卸载脚本，不修改网络。卸载后 XDG 用户资料不在软件包管理范围内。

系统依赖在 `packaging/debian/control.in` 声明，包括 .NET 原生依赖、X11/GL/fontconfig、libsecret、session D-Bus；推荐桌面 Secret Service 提供器和文件选择 portal。候选面向 Debian 12/13、Ubuntu 22.04/24.04 的常见依赖名称，组合尚需各发行版验证。GUI 用普通用户运行；密钥环仍需已解锁，不允许明文降级。维护者为秋山羽咲 <neko.dev1@outlook.com>。

## Arch Linux x86_64

```sh
python scripts/prepare-arch.py --archive <正式Linux-x64候选.tar.gz> --logo-sha256 <原图SHA256> --output <新的Arch输入目录>
# 将输入目录复制到真实 Arch Linux，先准备标准 base-devel 和运行依赖：
cd <Arch输入目录>
bash build-native.sh
```

输入目录包含标准 `PKGBUILD`、原始候选 tar.gz、桌面入口、精确 SHA-256 和原生构建脚本。使用 `makepkg`，不将 DEB 转换/改名，不使用 `SKIP`，不以 root 构建，不自动安装依赖或发布 AUR。`makepkg` 实际完成后才复制为 `NekoProxy-0.6.0-1-x86_64.pkg.tar.zst` 并计算 SHA-256；此前不会声称软件包已构建。

`pkgname` 为 `nekoproxy`，通过 pacman 管理 `/opt`、`/usr/bin`、应用菜单及图标；没有 `.install` 删除钩子，不管理用户目录。v0.6.0 已在官方 Arch userspace 容器中完成非 root makepkg、pacman 安装、Secret Service、X11/Xvfb 窗口启动及卸载检查；尚未在独立 Arch 桌面进行完整人工验收和升级测试。

## macOS Intel / Apple Silicon

先为 osx-x64、osx-arm64 各发布一份独立候选；不要把 Windows 二进制装入 `.app`。在真实 macOS 上：

```sh
bash packaging/macos/build-dmg.sh /absolute/NekoProxy.app x64 /absolute/new-dmg-x64
bash packaging/macos/build-dmg.sh /absolute/NekoProxy.app arm64 /absolute/new-dmg-arm64
```

脚本要求原生 `hdiutil`、`plutil`、`lipo`、`ditto`，核对 plist、图标和可执行架构，生成 `NekoProxy-x64.dmg` / `NekoProxy-arm64.dmg`。DMG 根目录提供 App 与 `/Applications` 链接，可拖入安装；使用 `hdiutil verify` 验证镜像。Windows 上直接拒绝执行，不生成伪 DMG。

没有签名/公证证书时只记录候选状态，不绕过 Gatekeeper，不声称正式发布就绪；Keychain 代码不修改。镜像构建、代码签名、公证、实际安装、启动与卸载分别记录。

实现依据：[Debian 包格式](https://manpages.debian.org/trixie/dpkg-dev/deb.5.en.html)、[Debian control](https://manpages.debian.org/trixie/dpkg-dev/deb-control.5.en.html)、[Arch PKGBUILD](https://man.archlinux.org/man/PKGBUILD.5.en)、[Arch makepkg](https://man.archlinux.org/man/makepkg.8.en)、[Inno Setup 官方下载](https://jrsoftware.org/isdl.php)。


## macOS v0.6.0 资源签名补充

DMG 脚本现在只在输出 staging 副本中调用 `packaging/macos/seal-bundle.py`，修正 .NET 资源/原生库目录并补齐 ad-hoc 完整性签名。该 helper 已在两种架构的 macOS 原生环境执行并通过验证；本次源码脚本的集成只做静态检查，未重新构建镜像。无 Developer ID 或 Apple 公证，Gatekeeper 仍拒绝。首次 v0.6.0 发行暂缓公开 macOS，保留本地候选，不提供移除隔离属性或关闭 Gatekeeper 的步骤。历史候选及输入 .app 保持原样。
