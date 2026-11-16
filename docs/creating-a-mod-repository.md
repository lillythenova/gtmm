# Creating a mod repository

A repository is a JSON file that lists plugins and links to their downloadable files. Host the file at a URL GTMM can access over HTTPS, then add that URL under **Settings → Repositories**.

## Use the GUI builder

Run `python3 make_repo.py` from the GTMM project folder. Enter a repository name, fill in each mod's name, version, optional description, and direct HTTPS download URL, then choose **Add Mod**. Select a row to edit it, or remove it with **Remove selected**. Use **Preview JSON** to check the output, then **Save repository...**.

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
      "download_url": "https://example.com/releases/ExampleMod-1.2.0.dll",
      "dependencies": ["ExampleLibrary"]
    },
    {
      "name": "ExampleLibrary",
      "version": "2.0.0",
      "description": "A library required by ExampleMod.",
      "download_url": "https://example.com/releases/ExampleLibrary-2.0.0.dll"
    }
  ]
}
```

Each mod entry needs:

- `name`: plugin name. For a DLL download, GTMM saves it as `<name>.dll`, so use a safe file name.
- `version`: version string, preferably in `major.minor.patch` format. A newer version is offered as an update.
- `description`: short text shown in the mod list.
- `download_url`: an HTTPS link to a DLL or ZIP file.
- `dependencies`: optional array of mod names, matched case-insensitively against mods in all enabled GTMM repositories. Dependencies are installed first, and already-installed dependencies are skipped. Missing, ambiguous, or circular dependencies stop the install with an error.

Dependencies can be entered in `make_repo.py` as comma-separated mod names. They can refer to entries in this repository or another repository already added to GTMM.

The JSON can be hosted on any static web host that serves the raw file. Make sure the URL opens the JSON itself, not a webpage that previews it. Update the version and download link in the JSON whenever you publish a new release.

## Track a GitHub release

To install a mod directly from a GitHub release, use the **+** button in the Mods view, paste or drag a public GitHub repository/release URL into the field, and choose a DLL or ZIP asset. GTMM checks later releases for an asset with that same name and offers updates.

When BepInEx is missing, choose **Install BepInEx** in the mods view to download the matching x64 package from the official BepInEx GitHub release and install it into the selected game folder.

## Download files

- A DLL link installs that file as `<name>.dll` in Gorilla Tag's `BepInEx/plugins` folder.
- A ZIP is extracted into its own folder under `BepInEx/plugins`, so nested DLLs and support files stay together and appear as one mod in GTMM. ZIP paths under `BepInEx/plugins/` are placed relative to that mod folder; files under `BepInEx/config/` are placed in the game's config folder.
- Keep each download link available while its version is listed in the repository.

Only add mods you have permission to distribute, and make sure users can trust the files they install.
