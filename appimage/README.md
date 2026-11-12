# Ultimate Doom Builder AppImage

This x86_64 Linux package bundles Ultimate Doom Builder, Mono's runtime and
class libraries, libgdiplus and their redistributable native dependencies.
Mono does not need to be installed on the target desktop.

## Build

From the repository root, build the editor with `make linux`, then run
`appimage/build-appimage.sh <version-label> [output-directory]`. The default
output directory is `Release/`. To stamp the displayed version too, set
`UdbVersionLabel` to that label when running `make linux`.

The build host needs x86_64 Mono with `mono-sgen`, the `4.5` profile and GAC,
libgdiplus, Bash, curl, GNU coreutils, tar, findutils, sed, awk, file and binutils
(`objdump`). Mono's native helpers must be installed alongside its runtime.
Build on the oldest distribution intended to be supported. Bundling does not
remove the build distribution's minimum glibc requirement.

`TMPDIR` must name a writable directory outside the checkout. It defaults to
`/tmp`. The AppDir, downloaded tools and intermediate image are created in a
unique temporary directory. Its path is printed on exit and retained for
inspection and cleanup by the caller. Existing output images are not overwritten.

The script downloads one pinned linuxdeploy release and verifies its SHA-256
before execution. Its bundled AppImage output plugin includes appimagetool,
which normally downloads a rolling AppImage runtime. Instead, the script
supplies the runtime from the verified linuxdeploy image, with its old update,
signature and digest fields cleared. It disables update metadata and AppStream
generation. No separate plugin or runtime download is needed. Distro library
versions are not pinned, so this is not a bit-for-bit reproducible build.

## Run

Make the downloaded image executable and launch it on an x86_64 Linux desktop.
On systems without FUSE, set `APPIMAGE_EXTRACT_AND_RUN=1` when launching it.
The host must provide glibc, an X11 display (or XWayland), core X11 libraries,
fontconfig configuration and working OpenGL libraries and drivers. These are
not replaced with a bundled graphics stack. zenity is optional and is not
bundled; without it, file dialogs fall back to Mono's built-in dialogs.

Settings, logs and the default screenshot directory use `$HOME/.local/share/Doom Builder`.
An explicitly configured screenshot destination is preserved. The launcher changes
into the bundled editor directory, so command-line file paths should be absolute.
The bundled `usr/lib/Fonts/fonts-native.conf` supplies the Arimo fallback used
by Mono dialogs. An explicitly set `FONTCONFIG_FILE` overrides that fallback.

## Licenses And Sources

The packaging README and UDB's `LICENSE.txt` are included in
`usr/share/doc/ultimatedoombuilder/` inside the AppImage. Arimo's license is in
`usr/lib/Fonts/Arimo-LICENSE.txt`. Distro copyright notices for Mono and
libgdiplus are copied when installed; linuxdeploy also collects native-library
copyright notices where supported. Preserve all notices when redistributing.

| Component | License | Source |
| --- | --- | --- |
| Ultimate Doom Builder and bundled editor components | GPL-3.0; additional notices accompany individual components | The matching release tag at https://github.com/Combinebobnt/UltimateDoomBuilder |
| Mono runtime and class libraries | MIT, with third-party notices included by the Mono distribution | https://github.com/mono/mono and the build distribution's Mono source packages |
| libgdiplus | MIT | https://gitlab.winehq.org/mono/libgdiplus and the build distribution's libgdiplus source package |
| Arimo fonts | Apache-2.0 | https://fonts.google.com/specimen/Arimo and the bundled font license |
| GLib, Pango and related dependencies, when deployed | LGPL-2.1-or-later and component-specific licenses | The build distribution's corresponding source packages |
| Cairo and Pixman, when deployed | Cairo: LGPL-2.1-or-later or MPL-1.1; Pixman: MIT | The build distribution's cairo and pixman source packages |
| libstdc++ and libgcc, when deployed | GPL-3.0 with GCC Runtime Library Exception | The build distribution's GCC source packages |
| libpng, libjpeg, libtiff, FreeType, fontconfig and other deployed dependencies | Respective permissive or free-software licenses; see distro notices | The build distribution's corresponding source packages |
| AppImage runtime | MIT, with its bundled third-party notices and licenses | https://github.com/AppImage/type2-runtime; the exact binary is taken from the pinned linuxdeploy image |

For Debian/Ubuntu builds, dependency sources are available through
https://sources.debian.org/ and https://launchpad.net/ubuntu/+source, or through
`apt-get source` using the same package repository and version as the build.
Mono packages from the Mono project use https://download.mono-project.com/repo/
and the corresponding Mono upstream source. Other distributions provide sources
through their source-package repositories. Retain the exact build package
versions and make matching sources, patches and required license notices
available when distributing a release. A source link alone is not a substitute
for any applicable GPL/LGPL source-distribution obligation.

linuxdeploy and appimagetool are build tools, not editor runtime dependencies.
Their source and license notices are at https://github.com/linuxdeploy/linuxdeploy,
https://github.com/linuxdeploy/linuxdeploy-plugin-appimage and
https://github.com/AppImage/appimagetool. Only the AppImage runtime is reused
in the generated image.
