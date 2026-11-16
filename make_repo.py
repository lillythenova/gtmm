#!/usr/bin/env python3
"""A small GUI for creating JSON repositories used by GTMM."""

import json
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit


INVALID_FILENAME_CHARACTERS = set('<>:"/\\|?*')


def parse_github_downloads(download_urls):
    repository = None
    assets = []
    for download_url in download_urls:
        parts = urlsplit(download_url)
        segments = [unquote(segment) for segment in parts.path.strip("/").split("/") if segment]
        if (
            parts.scheme != "https"
            or parts.netloc.lower() != "github.com"
            or len(segments) not in (5, 6)
            or segments[2].lower() != "releases"
        ):
            return None

        if len(segments) == 6 and segments[3].lower() == "latest" and segments[4].lower() == "download":
            asset = segments[5]
        elif len(segments) == 5 and segments[3].lower() == "download":
            asset = segments[4]
        else:
            return None

        current_repository = f"https://github.com/{segments[0]}/{segments[1]}"
        if repository is not None and current_repository.casefold() != repository.casefold():
            return None
        repository = current_repository
        assets.append(asset)

    return (repository, assets) if repository and assets else None


def github_asset_urls(repository_url, asset_names, mod_name):
    parts = urlsplit(repository_url.strip())
    segments = [segment for segment in parts.path.strip("/").split("/") if segment]
    if (
        parts.scheme != "https"
        or parts.netloc.lower() != "github.com"
        or parts.username is not None
        or parts.query
        or parts.fragment
        or len(segments) != 2
        or any(segment in {".", ".."} for segment in segments)
    ):
        raise ValueError(f"Enter a GitHub repository URL for {mod_name}, such as https://github.com/owner/repository.")

    assets = list(dict.fromkeys(asset.strip() for asset in asset_names if asset.strip()))
    if not assets:
        raise ValueError(f"Enter at least one release asset name for {mod_name}.")
    for asset in assets:
        if (
            asset in {".", ".."}
            or "/" in asset
            or "\\" in asset
            or any(ord(character) < 32 for character in asset)
        ):
            raise ValueError(f"Enter a file name only for each release asset in {mod_name}.")

    repository_url = f"https://github.com/{segments[0]}/{segments[1].removesuffix('.git')}"
    return repository_url, assets, [
        f"{repository_url}/releases/latest/download/{quote(asset, safe='')}"
        for asset in assets
    ]


def validate_mod(mod):
    name = str(mod.get("name", "")).strip()
    version = str(mod.get("version", "")).strip()
    download_mode = mod.get("download_mode", "manual")
    if download_mode not in {"manual", "github"}:
        raise ValueError(f"Choose manual links or a GitHub release for {name or 'this mod'}.")
    if download_mode == "github":
        github_repository, github_assets, download_urls = github_asset_urls(
            str(mod.get("github_repository", "")),
            mod.get("github_assets", []),
            name or "this mod",
        )
    else:
        github_repository = ""
        github_assets = []
        download_urls = mod.get("download_urls")
        if download_urls is None:
            download_urls = [mod.get("download_url", "")]
    if not isinstance(download_urls, list):
        raise ValueError(f"Download links for {name or 'this mod'} must be a list.")
    download_urls = list(dict.fromkeys(str(url).strip() for url in download_urls if str(url).strip()))
    dependencies = mod.get("dependencies", [])
    if isinstance(dependencies, str):
        dependencies = [dependency.strip() for dependency in dependencies.split(",") if dependency.strip()]
    if not isinstance(dependencies, list) or any(
        not isinstance(dependency, str) or not dependency.strip()
        for dependency in dependencies
    ):
        raise ValueError(f"Dependencies for {name or 'this mod'} must be mod names.")
    dependencies = list(dict.fromkeys(dependency.strip() for dependency in dependencies))

    if not name:
        raise ValueError("Every mod needs a name.")
    if any(character in INVALID_FILENAME_CHARACTERS or ord(character) < 32 for character in name):
        raise ValueError(f"{name!r} cannot be used as a mod file name.")
    if name in {".", ".."}:
        raise ValueError("A mod name cannot be . or ..")
    if not version:
        raise ValueError(f"Enter a version for {name}.")
    if any(dependency.casefold() == name.casefold() for dependency in dependencies):
        raise ValueError(f"{name} cannot depend on itself.")
    if not download_urls:
        raise ValueError(f"Add at least one download link for {name}.")
    for download_url in download_urls:
        parts = urlsplit(download_url)
        if parts.scheme != "https" or not parts.netloc:
            raise ValueError(f"Enter a complete HTTPS download link for {name}.")

    validated = {
        "name": name,
        "version": version,
        "description": str(mod.get("description", "")).strip(),
        "author": str(mod.get("author", "")).strip(),
        "dependencies": dependencies,
        "download_mode": download_mode,
        "github_repository": github_repository,
        "github_assets": github_assets,
    }
    if len(download_urls) == 1:
        validated["download_url"] = download_urls[0]
    else:
        validated["download_urls"] = download_urls
    return validated


def make_repo(repo_name, mods):
    repo_name = repo_name.strip()
    if not repo_name:
        raise ValueError("Enter a repository name.")
    if not mods:
        raise ValueError("Add at least one mod.")
    validated_mods = []
    for mod in mods:
        validated = validate_mod(mod)
        output = {
            key: validated[key]
            for key in ("name", "version", "description", "author", "dependencies")
        }
        download_urls = validated.get("download_urls", [validated.get("download_url", "")])
        if len(download_urls) == 1:
            output["download_url"] = download_urls[0]
        else:
            output["download_urls"] = download_urls
        validated_mods.append(output)
    return {"repo-name": repo_name, "mods": validated_mods}


def load_repo(path):
    with open(path, "r", encoding="utf-8") as file:
        data = json.load(file)
    if not isinstance(data, dict):
        raise ValueError("The JSON root must be an object.")

    repo_name = str(data.get("repo-name", "")).strip()
    raw_mods = data.get("mods")
    if isinstance(raw_mods, list):
        mods = raw_mods
    elif raw_mods is None:
        # Also import the older format where mods are top-level named entries.
        mods = [
            dict(value, name=value.get("name") or key)
            for key, value in data.items()
            if key not in {"repo-name", "format"} and isinstance(value, dict)
        ]
    else:
        raise ValueError("The 'mods' field must be a list.")

    normalized = []
    for mod in mods:
        if not isinstance(mod, dict):
            raise ValueError("Each mod entry must be an object.")
        normalized_mod = {
            "name": str(mod.get("name", mod.get("mod-name", ""))),
            "version": str(mod.get("version", mod.get("mod-ver", "1.0.0"))),
            "description": str(mod.get("description", mod.get("mod-desc", ""))),
            "author": str(mod.get("author", "")),
            "download_urls": mod.get(
                "download_urls",
                [mod.get("download_url", mod.get("mod-link", mod.get("url", "")))],
            ),
            "dependencies": mod.get("dependencies", []),
        }
        source = parse_github_downloads(normalized_mod["download_urls"])
        if source:
            normalized_mod.update({
                "download_mode": "github",
                "github_repository": source[0],
                "github_assets": source[1],
            })
        normalized.append(normalized_mod)
    return repo_name, normalized


def main():
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    class RepoBuilder:
        def __init__(self, root):
            self.root = root
            self.root.title("GTMM Repository Builder")
            self.root.geometry("940x680")
            self.root.minsize(760, 560)
            self.mods = []
            self.loaded_path = None

            self.repo_name = tk.StringVar()
            self.mod_name = tk.StringVar()
            self.version = tk.StringVar(value="1.0.0")
            self.description = tk.StringVar()
            self.author = tk.StringVar()
            self.download_mode = tk.StringVar(value="Manual links")
            self.download_urls = []
            self.github_repository = tk.StringVar()
            self.github_assets = []
            self.dependencies = tk.StringVar()
            self.form_mode = tk.StringVar(value="Add Mod")

            self.build_menu()
            self.build_ui()

        def build_menu(self):
            menu = tk.Menu(self.root)
            file_menu = tk.Menu(menu, tearoff=False)
            file_menu.add_command(label="New", command=self.new_repo)
            file_menu.add_command(label="Open JSON...", command=self.open_repo)
            file_menu.add_command(label="Save JSON...", command=self.save_repo)
            file_menu.add_separator()
            file_menu.add_command(label="Exit", command=self.root.destroy)
            menu.add_cascade(label="File", menu=file_menu)
            self.root.config(menu=menu)

        def build_ui(self):
            outer = ttk.Frame(self.root, padding=18)
            outer.pack(fill="both", expand=True)
            outer.columnconfigure(0, weight=1)
            outer.rowconfigure(3, weight=1)

            ttk.Label(
                outer,
                text="GTMM Repository Builder",
                font=("TkDefaultFont", 17, "bold"),
            ).grid(row=0, column=0, sticky="w", pady=(0, 14))

            repo_frame = ttk.LabelFrame(outer, text="Repository", padding=12)
            repo_frame.grid(row=1, column=0, sticky="ew", pady=(0, 12))
            repo_frame.columnconfigure(1, weight=1)
            ttk.Label(repo_frame, text="Name").grid(row=0, column=0, sticky="w", padx=(0, 10))
            ttk.Entry(repo_frame, textvariable=self.repo_name).grid(row=0, column=1, sticky="ew")

            mod_frame = ttk.LabelFrame(outer, text="Mod details", padding=12)
            mod_frame.grid(row=2, column=0, sticky="ew", pady=(0, 12))
            mod_frame.columnconfigure(1, weight=1)
            mod_frame.columnconfigure(3, weight=1)

            ttk.Label(mod_frame, text="Mod name").grid(row=0, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.mod_name).grid(row=0, column=1, sticky="ew", padx=(0, 12), pady=4)
            ttk.Label(mod_frame, text="Version").grid(row=0, column=2, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.version, width=18).grid(row=0, column=3, sticky="ew", pady=4)

            ttk.Label(mod_frame, text="Description").grid(row=1, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.description).grid(
                row=1, column=1, columnspan=3, sticky="ew", pady=4
            )

            ttk.Label(mod_frame, text="Author").grid(row=2, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.author).grid(
                row=2, column=1, columnspan=3, sticky="ew", pady=4
            )

            ttk.Label(mod_frame, text="Download method").grid(row=3, column=0, sticky="w", padx=(0, 8), pady=4)
            self.download_mode_box = ttk.Combobox(
                mod_frame,
                textvariable=self.download_mode,
                values=("Manual links", "GitHub release"),
                state="readonly",
                width=18,
            )
            self.download_mode_box.grid(row=3, column=1, sticky="w", pady=4)
            self.download_mode_box.bind("<<ComboboxSelected>>", self.on_download_mode_changed)

            ttk.Label(mod_frame, text="Download links").grid(row=4, column=0, sticky="nw", padx=(0, 8), pady=4)
            self.source_fields = ttk.Frame(mod_frame)
            self.source_fields.grid(row=4, column=1, columnspan=3, sticky="ew", pady=4)
            self.source_fields.columnconfigure(0, weight=1)
            self.manual_fields = ttk.Frame(self.source_fields)
            self.manual_fields.grid(row=0, column=0, sticky="ew")
            self.manual_fields.columnconfigure(0, weight=1)
            self.github_fields = ttk.Frame(self.source_fields)
            self.github_fields.grid(row=0, column=0, sticky="ew")
            self.github_fields.columnconfigure(1, weight=1)
            ttk.Label(self.github_fields, text="Repository URL").grid(
                row=0, column=0, sticky="w", padx=(0, 8), pady=(0, 4)
            )
            ttk.Entry(self.github_fields, textvariable=self.github_repository).grid(
                row=0, column=1, columnspan=2, sticky="ew", pady=(0, 4)
            )
            ttk.Label(self.github_fields, text="Asset names").grid(
                row=1, column=0, sticky="nw", padx=(0, 8), pady=2
            )
            self.github_assets_frame = ttk.Frame(self.github_fields)
            self.github_assets_frame.grid(row=1, column=1, columnspan=2, sticky="ew")
            self.github_assets_frame.columnconfigure(0, weight=1)
            self.download_links_frame = ttk.Frame(self.manual_fields)
            self.download_links_frame.grid(row=0, column=0, sticky="ew")
            self.download_links_frame.columnconfigure(0, weight=1)
            self.add_download_link_field()
            self.add_github_asset_field()
            self.show_download_mode()
            ttk.Label(mod_frame, text="Dependencies").grid(row=5, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.dependencies).grid(
                row=5, column=1, columnspan=3, sticky="ew", pady=4
            )
            ttk.Label(
                mod_frame,
                text="GitHub links are generated locally; no API calls or polling. Dependencies: comma-separated mod names.",
                foreground="#666666",
            ).grid(row=6, column=1, columnspan=3, sticky="w", pady=(0, 6))

            actions = ttk.Frame(mod_frame)
            actions.grid(row=7, column=0, columnspan=4, sticky="e", pady=(4, 0))
            ttk.Button(actions, text="Clear", command=self.clear_form).pack(side="left", padx=(0, 8))
            ttk.Button(actions, textvariable=self.form_mode, command=self.save_mod).pack(side="left")

            list_frame = ttk.LabelFrame(outer, text="Mods in this repository", padding=8)
            list_frame.grid(row=3, column=0, sticky="nsew", pady=(0, 12))
            list_frame.columnconfigure(0, weight=1)
            list_frame.rowconfigure(0, weight=1)
            columns = ("name", "version", "author", "dependencies", "description", "downloads")
            self.tree = ttk.Treeview(list_frame, columns=columns, show="headings", selectmode="browse")
            self.tree.heading("name", text="Name")
            self.tree.heading("version", text="Version")
            self.tree.heading("author", text="Author")
            self.tree.heading("dependencies", text="Dependencies")
            self.tree.heading("description", text="Description")
            self.tree.heading("downloads", text="Download links")
            self.tree.column("name", width=150, minwidth=100)
            self.tree.column("version", width=90, minwidth=70)
            self.tree.column("author", width=130, minwidth=90)
            self.tree.column("dependencies", width=150, minwidth=100)
            self.tree.column("description", width=200, minwidth=120)
            self.tree.column("downloads", width=360, minwidth=160)
            y_scroll = ttk.Scrollbar(list_frame, orient="vertical", command=self.tree.yview)
            x_scroll = ttk.Scrollbar(list_frame, orient="horizontal", command=self.tree.xview)
            self.tree.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)
            self.tree.grid(row=0, column=0, sticky="nsew")
            y_scroll.grid(row=0, column=1, sticky="ns")
            x_scroll.grid(row=1, column=0, sticky="ew")
            self.tree.bind("<<TreeviewSelect>>", self.select_mod)

            bottom = ttk.Frame(outer)
            bottom.grid(row=4, column=0, sticky="ew")
            ttk.Button(bottom, text="Remove selected", command=self.remove_mod).pack(side="left")
            ttk.Button(bottom, text="Preview JSON", command=self.preview_json).pack(side="left", padx=8)
            ttk.Button(bottom, text="Save repository...", command=self.save_repo).pack(side="right")
            self.status = tk.StringVar(value="Add a mod, then save the repository JSON.")
            ttk.Label(outer, textvariable=self.status, foreground="#555555").grid(
                row=5, column=0, sticky="w", pady=(10, 0)
            )

        def refresh_list(self):
            self.tree.delete(*self.tree.get_children())
            for index, mod in enumerate(self.mods):
                self.tree.insert(
                    "", "end", iid=str(index),
                    values=(
                        mod["name"], mod["version"], mod["author"], ", ".join(mod["dependencies"]),
                        mod["description"], ", ".join(mod.get("download_urls", [mod.get("download_url", "")])),
                    ),
                )

        def clear_form(self):
            self.tree.selection_remove(self.tree.selection())
            self.mod_name.set("")
            self.version.set("1.0.0")
            self.description.set("")
            self.author.set("")
            self.download_mode.set("manual")
            self.github_repository.set("")
            self.download_urls.clear()
            self.github_assets.clear()
            self.rebuild_download_link_fields()
            self.rebuild_github_asset_fields()
            self.show_download_mode()
            self.dependencies.set("")
            self.form_mode.set("Add Mod")

        def select_mod(self, _event=None):
            selected = self.tree.selection()
            if not selected:
                return
            mod = self.mods[int(selected[0])]
            self.mod_name.set(mod["name"])
            self.version.set(mod["version"])
            self.description.set(mod["description"])
            self.author.set(mod["author"])
            self.download_mode.set(
                "GitHub release" if mod.get("download_mode") == "github" else "Manual links"
            )
            self.github_repository.set(mod.get("github_repository", ""))
            if self.download_mode.get() == "GitHub release":
                for child in self.github_assets_frame.winfo_children():
                    child.destroy()
                self.github_assets.clear()
                for asset in mod.get("github_assets", []):
                    self.add_github_asset_field(asset)
                if not self.github_assets:
                    self.add_github_asset_field()
            else:
                for child in self.download_links_frame.winfo_children():
                    child.destroy()
                self.download_urls.clear()
                for url in mod.get("download_urls", [mod.get("download_url", "")]):
                    self.add_download_link_field(url)
            self.show_download_mode()
            self.dependencies.set(", ".join(mod["dependencies"]))
            self.form_mode.set("Update Selected")

        def save_mod(self):
            try:
                mod = validate_mod({
                    "name": self.mod_name.get(),
                    "version": self.version.get(),
                    "description": self.description.get(),
                    "author": self.author.get(),
                    "download_mode": (
                        "github" if self.download_mode.get() == "GitHub release" else "manual"
                    ),
                    "download_urls": [variable.get() for variable in self.download_urls],
                    "github_repository": self.github_repository.get(),
                    "github_assets": [variable.get() for variable in self.github_assets],
                    "dependencies": self.dependencies.get(),
                })
            except ValueError as error:
                messagebox.showerror("Check mod details", str(error), parent=self.root)
                return

            selected = self.tree.selection()
            if selected:
                self.mods[int(selected[0])] = mod
            else:
                self.mods.append(mod)
            self.refresh_list()
            self.clear_form()
            self.status.set(f"{len(self.mods)} mod(s) ready.")

        def on_download_mode_changed(self, _event=None):
            self.show_download_mode()

        def show_download_mode(self):
            if self.download_mode.get() == "GitHub release":
                self.manual_fields.grid_remove()
                self.github_fields.grid()
            else:
                self.github_fields.grid_remove()
                self.manual_fields.grid()

        def add_download_link_field(self, value=""):
            row = len(self.download_urls)
            variable = tk.StringVar(value=value)
            self.download_urls.append(variable)
            ttk.Entry(self.download_links_frame, textvariable=variable).grid(
                row=row, column=0, sticky="ew", pady=2, padx=(0, 6)
            )
            ttk.Button(
                self.download_links_frame, text="+", width=3,
                command=self.add_download_link_field,
            ).grid(row=row, column=1, padx=(0, 4))
            if row:
                ttk.Button(
                    self.download_links_frame, text="−", width=3,
                    command=lambda item=variable: self.remove_download_link_field(item),
                ).grid(row=row, column=2)

        def add_github_asset_field(self, value=""):
            row = len(self.github_assets)
            variable = tk.StringVar(value=value)
            self.github_assets.append(variable)
            ttk.Entry(self.github_assets_frame, textvariable=variable).grid(
                row=row, column=0, sticky="ew", pady=2, padx=(0, 6)
            )
            ttk.Button(
                self.github_assets_frame, text="+", width=3,
                command=self.add_github_asset_field,
            ).grid(row=row, column=1, padx=(0, 4))
            if row:
                ttk.Button(
                    self.github_assets_frame, text="−", width=3,
                    command=lambda item=variable: self.remove_github_asset_field(item),
                ).grid(row=row, column=2)

        def remove_download_link_field(self, variable):
            self.download_urls.remove(variable)
            self.rebuild_download_link_fields()

        def remove_github_asset_field(self, variable):
            self.github_assets.remove(variable)
            self.rebuild_github_asset_fields()

        def rebuild_download_link_fields(self):
            values = [variable.get() for variable in self.download_urls] or [""]
            for child in self.download_links_frame.winfo_children():
                child.destroy()
            self.download_urls.clear()
            for value in values:
                self.add_download_link_field(value)

        def rebuild_github_asset_fields(self):
            values = [variable.get() for variable in self.github_assets] or [""]
            for child in self.github_assets_frame.winfo_children():
                child.destroy()
            self.github_assets.clear()
            for value in values:
                self.add_github_asset_field(value)

        def remove_mod(self):
            selected = self.tree.selection()
            if not selected:
                messagebox.showinfo("Remove mod", "Select a mod to remove first.", parent=self.root)
                return
            del self.mods[int(selected[0])]
            self.refresh_list()
            self.clear_form()
            self.status.set(f"{len(self.mods)} mod(s) ready.")

        def repo_json(self):
            return make_repo(self.repo_name.get(), self.mods)

        def preview_json(self):
            try:
                content = json.dumps(self.repo_json(), indent=2, ensure_ascii=False) + "\n"
            except ValueError as error:
                messagebox.showerror("Cannot preview yet", str(error), parent=self.root)
                return
            preview = tk.Toplevel(self.root)
            preview.title("Repository JSON preview")
            preview.geometry("720x520")
            text = tk.Text(preview, wrap="none", undo=False)
            y_scroll = ttk.Scrollbar(preview, orient="vertical", command=text.yview)
            x_scroll = ttk.Scrollbar(preview, orient="horizontal", command=text.xview)
            text.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)
            text.grid(row=0, column=0, sticky="nsew")
            y_scroll.grid(row=0, column=1, sticky="ns")
            x_scroll.grid(row=1, column=0, sticky="ew")
            preview.rowconfigure(0, weight=1)
            preview.columnconfigure(0, weight=1)
            text.insert("1.0", content)
            text.configure(state="disabled")

        def open_repo(self):
            path = filedialog.askopenfilename(
                parent=self.root,
                title="Open repository JSON",
                filetypes=[("JSON files", "*.json"), ("All files", "*.*")],
            )
            if not path:
                return
            try:
                name, mods = load_repo(path)
                mods = [validate_mod(mod) for mod in mods]
            except (OSError, json.JSONDecodeError, ValueError) as error:
                messagebox.showerror("Could not open repository", str(error), parent=self.root)
                return
            self.repo_name.set(name)
            self.mods = mods
            self.loaded_path = Path(path)
            self.refresh_list()
            self.clear_form()
            self.status.set(f"Loaded {self.loaded_path.name}.")

        def save_repo(self):
            try:
                content = json.dumps(self.repo_json(), indent=2, ensure_ascii=False) + "\n"
            except ValueError as error:
                messagebox.showerror("Check repository", str(error), parent=self.root)
                return
            path = filedialog.asksaveasfilename(
                parent=self.root,
                title="Save repository JSON",
                defaultextension=".json",
                initialfile=self.loaded_path.name if self.loaded_path else "repository.json",
                filetypes=[("JSON files", "*.json"), ("All files", "*.*")],
            )
            if not path:
                return
            try:
                Path(path).write_text(content, encoding="utf-8")
            except OSError as error:
                messagebox.showerror("Could not save repository", str(error), parent=self.root)
                return
            self.loaded_path = Path(path)
            self.status.set(f"Saved {self.loaded_path}. Host this file and add its URL in GTMM Settings.")
            messagebox.showinfo(
                "Repository saved",
                "Your repository JSON is ready. Host it at a public HTTPS URL, then add that URL in GTMM Settings → Repositories.",
                parent=self.root,
            )

        def new_repo(self):
            self.repo_name.set("")
            self.mods = []
            self.loaded_path = None
            self.refresh_list()
            self.clear_form()
            self.status.set("New repository.")

    root = tk.Tk()
    RepoBuilder(root)
    root.mainloop()


if __name__ == "__main__":
    main()
