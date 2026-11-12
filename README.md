# Ultimate Doom Builder

> **About this fork:** a fork of [UltimateDoomBuilder/UltimateDoomBuilder](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder) primarily for Linux fixes, and maybe with some extra spices. Works on my machine 😎. See [CHANGELOG.md](CHANGELOG.md).

## Download

Portable builds are published on the fork's [Releases](https://github.com/Combinebobnt/UltimateDoomBuilder/releases) page as `R<upstream revision>-fork.<commits>`: Windows x64/x86 zips and a Linux x86_64 AppImage that bundles Mono. These builds show the fork label and don't offer upstream updates.

## What is tested

Day-to-day use and testing happen on Linux under Mono, on an X11 desktop. Nothing else is tested, so treat these as unknown:

- **Wayland:** UDB is an X11 program (GLX rendering, Mono's X11 WinForms), so on a Wayland desktop it runs through XWayland. That path hasn't been tried.
- **Windows (.NET Framework):** every push compiles the x64 and x86 builds and runs the unit tests on Windows, but the editor itself isn't run there.
- **Flatpak:** the Flatpak files are unchanged from upstream and haven't been built for this fork.

Reports from any of these are welcome.

## System requirements
- 2.4 GHz CPU or faster (multi-core recommended)
- Graphics card with OpenGL 3.2 support
- **Windows:** 7, 8, 10, or 11, with [Microsoft .Net Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)
- **Linux:** x86_64, an X11 session (or XWayland), and glibc 2.34 or newer for the bundled zdbsp node builder
  - Optional: `zenity` for native file dialogs, zt-bcc installed as `/usr/bin/bcc` to compile BCS scripts, and libc++ for the elfbsp node builder in a source build

## Building on Linux

On Linux the easiest route is the AppImage from [Releases](https://github.com/Combinebobnt/UltimateDoomBuilder/releases), which needs no Mono install. To build from source:

### Manual build

- Install Mono and msbuild
  - **Debian/Ubuntu:** the distro's own `mono-complete` package has no `msbuild`, so add the Mono project's repository first by following https://www.mono-project.com/download/stable/#download-lin, then install from it:
    ```
    sudo apt install mono-complete msbuild
    ```
  - **Arch:** both are in the *extra/* repo, which is enabled by default:
    ```
    sudo pacman -S mono mono-msbuild
    ```
- Install the native build tools and headers
  - **Debian/Ubuntu:**
    ```
    sudo apt install make g++ git libx11-dev libxfixes-dev mesa-common-dev
    ```
  - **Arch:** `sudo pacman -S base-devel`
    - If you're using X11 display manager you may need to install these packages: `libx11 libxfixes`
    - If you are not using the proprietary nvidia driver you may need to install `mesa`
- Clone, build and run (`make` builds the editor and its native library into `Build/`):
  ```
  git clone https://github.com/Combinebobnt/UltimateDoomBuilder.git
  cd UltimateDoomBuilder
  make
  ./builder.sh
  ```
- To compile UDB in debug mode instead, run `make BUILDTYPE=Debug`. This adds a debug output terminal in the bottom panel.

### AppImage build

After `make`, `appimage/build-appimage.sh <version-label>` packages the editor and the installed Mono into `Release/UltimateDoomBuilder-<version-label>-x86_64.AppImage`. It downloads a pinned linuxdeploy release, and needs libc++ installed (`sudo apt install libc++1` on Debian/Ubuntu) because the bundled elfbsp node builder links against it. Other host requirements are in [appimage/README.md](appimage/README.md).

### Flatpak build

The advantage of using Flatpak to build a package is that you do not need to install Mono directly into your system, since everything in the build process will be self-contained. This also means that this works on Linux distributions that do not have `msbuild` in their repository.

- To build UDB using Flatpak you need both **Flatpak** and **Flatpak Builder**. How they are installed depends on your distribution. For example on Debian-based distributions they can be installed using `sudo apt install flatpak flatpak-builder`. Check your distro's documentation for information on how to install them.
- Go to a directory of your choice and clone the repository (it'll automatically create an UltimateDoomBuilder directory in the current directory):
  ```
  git clone https://github.com/Combinebobnt/UltimateDoomBuilder.git
  ``` 
- Go to the cloned directory:
  ```
  cd UltimateDoomBuilder
  ```

- Build the Flatpak. This will also download all required dependencies:
  ```
  ./build_flatpak.sh
  ```

- This will create a file in the format `ultimatedoombuilder-<version>.flatpak` in the `Releases` directory. You can now install and run the Flatpak:

  ```
  flatpak install --user Releases/ultimatedoombuilder-<version>.flatpak
  flatpak run io.github.ultimatedoombuilder.ultimatedoombuilder
  ```
### Flatpak build using WSL2

You can also build the flatpak on Windows using WSL2.

#### Initial setup
- Create the instance. Requires entering a user name and password:
  ```powershell
  wsl --install Ubuntu-24.04 --name udb-flatpak-builder
  ```
- Install required Flatpak packages and clone the repository, then exit the insance. Requires entering the user password from the previous step:
  ```bash
  sudo apt update && sudo apt -y install flatpak flatpak-builder
  cd ~
  git clone https://github.com/Combinebobnt/UltimateDoomBuilder.git
  exit
  ```

#### Building the flatpak
- Update the repository and build the flatpak:
  ```powershell
  wsl --distribution udb-flatpak-builder --cd ~/UltimateDoomBuilder -- git pull `&`& ./build_flatpak.sh
  ```
- Get the flatpak from the UNC path `\\wsl.localhost\udb-flatpak-builder\home\$env:username\UltimateDoomBuilder\Releases`.
- Stop the instance (optional, it should shut itself down after some time):
  ```powershell
  wsl --terminate udb-flatpak-builder
  ```


# Upstream

This fork tracks [UltimateDoomBuilder/UltimateDoomBuilder](https://github.com/UltimateDoomBuilder/UltimateDoomBuilder). Upstream's README, forum thread and builds cover the official editor.

