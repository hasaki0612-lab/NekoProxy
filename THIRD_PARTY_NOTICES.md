# Third-party components

NekoProxy source is MIT licensed. Dependencies retain their own notices and licenses; the Windows bundle includes a `licenses` folder and the .NET runtime's license/notices.

| Component | Version | License / source |
|---|---|---|
| Avalonia | 11.3.22 | MIT · https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm | 8.4.2 | MIT · https://github.com/CommunityToolkit/dotnet |
| SSH.NET | 2026.0.0 | MIT · https://github.com/sshnet/SSH.NET |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT · https://github.com/dotnet/runtime |
| BouncyCastle.Cryptography | 2.7.0 | MIT · https://github.com/bcgit/bc-csharp |
| QRCoder | 1.7.0 | MIT · https://github.com/codebude/QRCoder |
| SkiaSharp / HarfBuzzSharp | Transitive Avalonia dependencies | MIT; native libraries include upstream notices · https://github.com/mono/SkiaSharp |

sing-box and Mihomo were used as external validation tools in the workspace. They are **not bundled** into the desktop package or source ZIP. Their licenses remain with their upstream projects. Future server core distribution and installation must preserve applicable upstream licensing.
