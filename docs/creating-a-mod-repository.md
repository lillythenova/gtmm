# Creating a mod repository

A repository is a JSON file that lists plugins and links to their downloadable files. Host the file at a URL GTMM can access over HTTPS, then add that URL under **Settings → Repositories**.

## Use the GUI builder

Run `python3 make_repo.py` from the GTMM project folder. Enter a repository name and each mod's details, then choose **Manual links** to enter direct download URLs or **GitHub release** to enter a repository URL and one or more release asset filenames. For example, enter `https://github.com/DecalFree/ComputerInterface` and `ComputerInterface-BepInEx.zip`. Use **+** to add more files to one mod. GitHub links are assembled locally; the builder does not call or poll the GitHub API. Select a row to edit it, or remove it with **Remove selected**. Use **Preview JSON** to check the output, then **Save repository...**.

The builder creates the JSON file; it does not host it. Upload the saved file to a public HTTPS static host, then paste its raw file URL in **GTMM → Settings → Repositories**. Python's Tk GUI module (`tkinter`) must be installed.

## Repository file

Create a file such as `repository.json`:

```json
{
  "repo-name": "My Mod Repository",
  "mods": [
    {
      "name": "ExampleMod",
      "version": "1.2.0",
      "description": "An example mod.",
      "author": "Mod Author",
      "download_url": "https://example.com/releases/ExampleMod-1.2.0.dll",
      "dependencies": ["ExampleLibrary"]
    },
    {
      "name": "ExampleLibrary",
      "version": "2.0.0",
      "description": "A library required by ExampleMod.",
      "download_url": "https://example.com/releases/ExampleLibrary-2.0.0.dll"
    },
    {
      "name": "MultiFileMod",
      "version": "1.0.0",
      "description": "A mod distributed as two separate DLLs.",
      "download_urls": [
        "https://example.com/releases/MultiFileMod.Core.dll",
        "https://example.com/releases/MultiFileMod.UI.dll"
      ]
    }
  ]
}
```

Each mod entry needs:

- `name`: plugin name. For a DLL download, GTMM saves it as `<name>.dll`, so use a safe file name.
- `version`: version string, preferably in `major.minor.patch` format. A newer version is offered as an update.
- `description`: short text shown in the mod list.
- `author`: optional creator name, shown next to the mod name when provided.
- `download_url`: an HTTPS link to a DLL or ZIP file.
- `download_urls`: optional array of direct HTTPS DLL links for a mod distributed as multiple separate files. Use this instead of `download_url`; GTMM installs all listed DLLs together as one mod folder, using each link's file name.
- `dependencies`: optional array of mod names, matched case-insensitively against mods in all enabled GTMM repositories. Dependencies are installed first, and already-installed dependencies are skipped. Missing, ambiguous, or circular dependencies stop the install with an error.

Dependencies can be entered in `make_repo.py` as comma-separated mod names. They can refer to entries in this repository or another repository already added to GTMM.

The JSON can be hosted on any static web host that serves the raw file. Make sure the URL opens the JSON itself, not a webpage that previews it. Update the version and download link in the JSON whenever you publish a new release.

For GitHub release assets, the builder generates direct `releases/latest/download/<asset>` URLs. These work for public assets attached to a GitHub release. GTMM only requests the asset when someone installs or updates the mod; it does not poll GitHub for repository entries.

GTMM can check the latest release tag to detect updates. In **Settings → Updates**, **Check for updates automatically** checks when GTMM starts, while **Check GitHub releases when refreshing** controls checks made by the Refresh button. Each check makes at most one GitHub API request per unique repository, even when several mods or assets come from that repository. GTMM only notifies you; it never installs updates automatically.

## Download a GitHub release

To install a mod directly from a GitHub release, use the **+** button in the Mods view, paste or drag a public GitHub repository/release URL into the field, and choose a DLL or ZIP asset. GTMM checks later releases for an asset with that same name and offers updates.

When BepInEx is missing, choose **Install BepInEx** in the mods view to download the matching x64 package from the official BepInEx GitHub release and install it into the selected game folder.

## Download files

- A DLL link installs that file as `<name>.dll` in Gorilla Tag's `BepInEx/plugins` folder.
- A ZIP is extracted into its own folder under `BepInEx/plugins`, so nested DLLs and support files stay together and appear as one mod in GTMM. ZIP paths under `BepInEx/plugins/` are placed relative to that mod folder; files under `BepInEx/config/` are placed in the game's config folder.
- Keep each download link available while its version is listed in the repository.

Only add mods you have permission to distribute, and make sure users can trust the files they install.
