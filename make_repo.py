#!/usr/bin/env python3
"""A small GUI for creating JSON repositories used by GTMM."""

import json
from pathlib import Path
from urllib.parse import urlsplit


INVALID_FILENAME_CHARACTERS = set('<>:"/\\|?*')


def validate_mod(mod):
    name = str(mod.get("name", "")).strip()
    version = str(mod.get("version", "")).strip()
    download_url = str(mod.get("download_url", "")).strip()
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
    parts = urlsplit(download_url)
    if parts.scheme != "https" or not parts.netloc:
        raise ValueError(f"Enter a complete HTTPS download link for {name}.")

    return {
        "name": name,
        "version": version,
        "description": str(mod.get("description", "")).strip(),
        "download_url": download_url,
        "dependencies": dependencies,
    }


def make_repo(repo_name, mods):
    repo_name = repo_name.strip()
    if not repo_name:
        raise ValueError("Enter a repository name.")
    if not mods:
        raise ValueError("Add at least one mod.")
    return {
        "repo-name": repo_name,
        "mods": [validate_mod(mod) for mod in mods],
    }


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
        normalized.append({
            "name": str(mod.get("name", mod.get("mod-name", ""))),
            "version": str(mod.get("version", mod.get("mod-ver", "1.0.0"))),
            "description": str(mod.get("description", mod.get("mod-desc", ""))),
            "download_url": str(
                mod.get("download_url", mod.get("mod-link", mod.get("url", "")))
            ),
            "dependencies": mod.get("dependencies", []),
        })
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
            self.download_url = tk.StringVar()
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

            ttk.Label(mod_frame, text="Download URL").grid(row=2, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.download_url).grid(
                row=2, column=1, columnspan=3, sticky="ew", pady=4
            )
            ttk.Label(mod_frame, text="Dependencies").grid(row=3, column=0, sticky="w", padx=(0, 8), pady=4)
            ttk.Entry(mod_frame, textvariable=self.dependencies).grid(
                row=3, column=1, columnspan=3, sticky="ew", pady=4
            )
            ttk.Label(
                mod_frame,
                text="Direct HTTPS download link. Dependencies: comma-separated mod names.",
                foreground="#666666",
            ).grid(row=4, column=1, columnspan=3, sticky="w", pady=(0, 6))

            actions = ttk.Frame(mod_frame)
            actions.grid(row=5, column=0, columnspan=4, sticky="e", pady=(4, 0))
            ttk.Button(actions, text="Clear", command=self.clear_form).pack(side="left", padx=(0, 8))
            ttk.Button(actions, textvariable=self.form_mode, command=self.save_mod).pack(side="left")

            list_frame = ttk.LabelFrame(outer, text="Mods in this repository", padding=8)
            list_frame.grid(row=3, column=0, sticky="nsew", pady=(0, 12))
            list_frame.columnconfigure(0, weight=1)
            list_frame.rowconfigure(0, weight=1)
            columns = ("name", "version", "dependencies", "description", "download_url")
            self.tree = ttk.Treeview(list_frame, columns=columns, show="headings", selectmode="browse")
            self.tree.heading("name", text="Name")
            self.tree.heading("version", text="Version")
            self.tree.heading("dependencies", text="Dependencies")
            self.tree.heading("description", text="Description")
            self.tree.heading("download_url", text="Download URL")
            self.tree.column("name", width=150, minwidth=100)
            self.tree.column("version", width=90, minwidth=70)
            self.tree.column("dependencies", width=150, minwidth=100)
            self.tree.column("description", width=200, minwidth=120)
            self.tree.column("download_url", width=360, minwidth=160)
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
                        mod["name"], mod["version"], ", ".join(mod["dependencies"]),
                        mod["description"], mod["download_url"],
                    ),
                )

        def clear_form(self):
            self.tree.selection_remove(self.tree.selection())
            self.mod_name.set("")
            self.version.set("1.0.0")
            self.description.set("")
            self.download_url.set("")
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
            self.download_url.set(mod["download_url"])
            self.dependencies.set(", ".join(mod["dependencies"]))
            self.form_mode.set("Update Selected")

        def save_mod(self):
            try:
                mod = validate_mod({
                    "name": self.mod_name.get(),
                    "version": self.version.get(),
                    "description": self.description.get(),
                    "download_url": self.download_url.get(),
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
