# Third-party notices

The root MIT license covers original PixelForge code and documentation, not ownership of external software or users' assets. No third-party source implementation or third-party NuGet dependency is included in the original source tree.

## Bundled .NET runtime

Self-contained Windows packages contain Microsoft .NET runtime and Windows Desktop/WPF components. Packaging copies their licenses and third-party notices from the restored runtime packages and Windows Desktop SDK into `licenses/` in the ZIP. Keep them with the application.

- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [WPF license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT)

The supplied notices include component-specific terms; PixelForge's MIT license does not replace them. The SDK itself is not distributed.

## External applications — not bundled

- **Blender:** separate GPL-licensed application, invoked through original Python bridge scripts. See [Blender licensing](https://www.blender.org/about/license/).
- **Aseprite:** separate application under its own license/EULA, invoked using original generated Lua scripts. No Aseprite binaries or source are bundled. See [Aseprite licensing/redistribution FAQ](https://aseprite.org/faq/#can-i-redistribute-aseprite).

Imported assets remain subject to their owners' terms. An integration does not grant rights to redistribute those assets.
