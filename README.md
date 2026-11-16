# GTMM

GTMM is a desktop app for managing Gorilla Tag BepInEx plugins. It’s built with Avalonia and .NET.

> **AI disclosure:** AI tools were used to help with this project. The UI was created by me.

**IF YOU WANT YOUR MOD REMOVED FROM THE DEFAULT REPO DM ME ON DISCORD** ```lilly_the_nova```

You can use it to:

- Add, enable, disable, and remove mods
- Browse mod repositories and track GitHub releases for updates
- Install the official BepInEx release directly when it is missing
- Launch Gorilla Tag through Steam

## Get a build

Download the latest build from the Releases section of the GitHub repository where this project is hosted. Releases include a Linux x64 AppImage, Linux x64 and ARM64 executables, and Windows x64 and ARM64 executables.

The AppImage is a single file and does not need installation. It uses a generic launcher icon and still relies on system graphics libraries and OpenSSL 3. Linux builds are produced on Ubuntu 22.04, so older distributions with an older glibc may not run them.

## Build it yourself

You’ll need the .NET 10 SDK.

On Linux or macOS:

```sh
./build.sh
```

On Windows:

```bat
build_windows.bat
```

The finished, self-contained apps are saved in `dist/`. Each executable is a single file; you don’t need to install .NET to run it. Linux builds target glibc.

To package a local x64 build as an AppImage, install `appimagetool` and run:

```sh
./build.py --rid linux-x64
./packaging/package_appimage.sh
```

The AppImage is created at `dist/gtmm-linux-x86_64.AppImage`.

## Mod repositories

To make a repository, run `python3 make_repo.py` for a simple GUI that creates the JSON file. Host the file at a public HTTPS URL, then add that URL in **Settings → Repositories**. See the [repository guide](docs/creating-a-mod-repository.md) for details.
