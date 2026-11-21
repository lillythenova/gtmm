#!/usr/bin/env python3
"""A GUI for creating and updating JSON repositories used by GTMM."""

import json
import os
import re
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

INVALID_FILENAME_CHARACTERS = set('<>:"/\\|?*')


# ==============================================================================
# Helpers & GitHub URL Parsing
# ==============================================================================

def parse_github_release_url(url: str):
    """Parse any GitHub release or repository URL into owner/repo, asset, and tag."""
    if not url or not isinstance(url, str):
        return None
    parts = urlsplit(url.strip())
    if parts.scheme != "https" or parts.netloc.lower() != "github.com":
        return None

    segments = [unquote(s) for s in parts.path.strip("/").split("/") if s]
    if len(segments) < 2:
        return None

    owner, repo = segments[0], segments[1].removesuffix(".git")
    if not owner or not repo or any(s in {".", ".."} for s in (owner, repo)):
        return None

    repo_url = f"https://github.com/{owner}/{repo}"
    owner_repo = f"{owner}/{repo}"

    if len(segments) >= 6 and segments[2].lower() == "releases":
        if segments[3].lower() == "latest" and segments[4].lower() == "download":
            return {"owner_repo": owner_repo, "repo_url": repo_url, "asset": segments[5], "tag": "latest", "is_latest": True}
        elif segments[3].lower() == "download":
            return {"owner_repo": owner_repo, "repo_url": repo_url, "asset": segments[5], "tag": segments[4], "is_latest": False}

    if len(segments) == 5 and segments[2].lower() == "releases" and segments[3].lower() == "tag":
        return {"owner_repo": owner_repo, "repo_url": repo_url, "asset": None, "tag": segments[4], "is_latest": False}

    return {"owner_repo": owner_repo, "repo_url": repo_url, "asset": None, "tag": None, "is_latest": True}


def parse_github_downloads(download_urls):
    """Check if download URLs belong to a GitHub repo. Returns (repo_url, assets) or None."""
    if not download_urls:
        return None
    repository = None
    assets = []
    for download_url in download_urls:
        parsed = parse_github_release_url(download_url)
        if not parsed or not parsed.get("asset"):
            return None
        if repository is not None and parsed["repo_url"].casefold() != repository.casefold():
            return None
        repository = parsed["repo_url"]
        assets.append(parsed["asset"])
    return (repository, assets) if repository and assets else None


def github_asset_urls(repository_url, asset_names, mod_name):
    """Generate GitHub latest release download links for a mod."""
    parsed = parse_github_release_url(repository_url.strip())
    if not parsed:
        raise ValueError(f"Enter a GitHub repository URL for {mod_name}, such as https://github.com/owner/repository.")

    repository_url = parsed["repo_url"]
    assets = list(dict.fromkeys(a.strip() for a in asset_names if a.strip()))
    if not assets and parsed.get("asset"):
        assets = [parsed["asset"]]
    if not assets:
        raise ValueError(f"Enter at least one release asset name for {mod_name} (e.g. {mod_name}.dll).")
    for asset in assets:
        if asset in {".", ".."} or "/" in asset or "\\" in asset or any(ord(c) < 32 for c in asset):
            raise ValueError(f"Enter a file name only for each release asset in {mod_name}.")

    return repository_url, assets, [
        f"{repository_url}/releases/latest/download/{quote(a, safe='')}" for a in assets
    ]


def parse_version_tuple(v_str: str):
    """Parse version string into comparable tuple of (int, suffix)."""
    if not v_str:
        return ()
    clean = str(v_str).strip().lstrip("vV")
    parts = []
    for segment in clean.split("."):
        match = re.match(r"^(\d+)(.*)$", segment)
        parts.append((int(match.group(1)), match.group(2)) if match else (-1, segment))
    return tuple(parts)


def is_newer_version(new_ver: str, current_ver: str) -> bool:
    """Return True if new_ver is strictly newer than current_ver."""
    t_new = parse_version_tuple(new_ver)
    t_curr = parse_version_tuple(current_ver)
    return t_new > t_curr if t_new and t_curr else str(new_ver).strip() != str(current_ver).strip()


def validate_mod(mod):
    """Validate mod dictionary according to GTMM schema."""
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
    download_urls = list(dict.fromkeys(str(u).strip() for u in download_urls if str(u).strip()))

    dependencies = mod.get("dependencies", [])
    if isinstance(dependencies, str):
        dependencies = [d.strip() for d in dependencies.split(",") if d.strip()]
    if not isinstance(dependencies, list) or any(not isinstance(d, str) or not d.strip() for d in dependencies):
        raise ValueError(f"Dependencies for {name or 'this mod'} must be mod names.")
    dependencies = list(dict.fromkeys(d.strip() for d in dependencies))

    if not name:
        raise ValueError("Every mod needs a name.")
    if any(c in INVALID_FILENAME_CHARACTERS or ord(c) < 32 for c in name):
        raise ValueError(f"{name!r} cannot be used as a mod file name.")
    if name in {".", ".."}:
        raise ValueError("A mod name cannot be . or ..")
    if not version:
        raise ValueError(f"Enter a version for {name}.")
    if any(d.casefold() == name.casefold() for d in dependencies):
        raise ValueError(f"{name} cannot depend on itself.")
    if not download_urls:
        raise ValueError(f"Add at least one download link for {name}.")
    for u in download_urls:
        p = urlsplit(u)
        if p.scheme != "https" or not p.netloc:
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
    """Serialize the repository dictionary."""
    repo_name = repo_name.strip()
    if not repo_name:
        raise ValueError("Enter a repository name.")
    if not mods:
        raise ValueError("Add at least one mod.")
    validated_mods = []
    for mod in mods:
        val = validate_mod(mod)
        out = {k: val[k] for k in ("name", "version", "description", "author", "dependencies")}
        urls = val.get("download_urls", [val.get("download_url", "")])
        if len(urls) == 1:
            out["download_url"] = urls[0]
        else:
            out["download_urls"] = urls
        validated_mods.append(out)
    return {"repo-name": repo_name, "mods": validated_mods}


def load_repo(path):
    """Load and normalize a repository JSON file."""
    with open(path, "r", encoding="utf-8") as file:
        data = json.load(file)
    if not isinstance(data, dict):
        raise ValueError("The JSON root must be an object.")

    repo_name = str(data.get("repo-name", "")).strip()
    raw_mods = data.get("mods")
    if isinstance(raw_mods, list):
        mods = raw_mods
    elif raw_mods is None:
        mods = [
            dict(v, name=v.get("name") or k)
            for k, v in data.items()
            if k not in {"repo-name", "format"} and isinstance(v, dict)
        ]
    else:
        raise ValueError("The 'mods' field must be a list.")

    normalized = []
    for mod in mods:
        if not isinstance(mod, dict):
            raise ValueError("Each mod entry must be an object.")
        urls = mod.get("download_urls")
        if urls is None:
            single = mod.get("download_url", mod.get("mod-link", mod.get("url", "")))
            urls = [single] if single else []

        norm = {
            "name": str(mod.get("name", mod.get("mod-name", ""))),
            "version": str(mod.get("version", mod.get("mod-ver", "1.0.0"))),
            "description": str(mod.get("description", mod.get("mod-desc", ""))),
            "author": str(mod.get("author", "")),
            "download_urls": urls,
            "dependencies": mod.get("dependencies", []),
            "download_mode": "manual",
            "github_repository": "",
            "github_assets": [],
        }
        source = parse_github_downloads(norm["download_urls"])
        if source:
            norm.update({"download_mode": "github", "github_repository": source[0], "github_assets": source[1]})
        normalized.append(norm)
    return repo_name, normalized


# ==============================================================================
# GitHub API & Update Checker
# ==============================================================================

def fetch_github_api(url: str, cache: dict = None):
    """GET request to GitHub API with optional auth token and caching."""
    if cache is not None and url in cache:
        return cache[url]
    headers = {"User-Agent": "Mozilla/5.0 (X11; Linux x86_64)", "Accept": "application/vnd.github+json"}
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token.strip()}"
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=8) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        if cache is not None:
            cache[url] = data
        return data


def fetch_github_release_web(owner_repo: str):
    """Fallback: Resolve latest tag and assets via GitHub web page if API is rate limited."""
    latest_url = f"https://github.com/{owner_repo}/releases/latest"
    req = urllib.request.Request(latest_url, headers={"User-Agent": "Mozilla/5.0 (X11; Linux x86_64)"})
    with urllib.request.urlopen(req, timeout=8) as resp:
        final_url = resp.geturl()
    if "/releases/tag/" not in final_url:
        return None
    tag = final_url.split("/releases/tag/")[-1].strip()

    assets_url = f"https://github.com/{owner_repo}/releases/expanded_assets/{tag}"
    req_assets = urllib.request.Request(assets_url, headers={"User-Agent": "Mozilla/5.0 (X11; Linux x86_64)"})
    assets = []
    try:
        with urllib.request.urlopen(req_assets, timeout=8) as resp:
            html = resp.read().decode("utf-8")
            found = list(dict.fromkeys(re.findall(r"/releases/download/[^/]+/([^\"\'\s]+\.(?:dll|zip))", html)))
            for fname in found:
                assets.append({
                    "name": fname,
                    "browser_download_url": f"https://github.com/{owner_repo}/releases/download/{tag}/{fname}",
                })
    except Exception:
        pass
    return {"tag_name": tag, "assets": assets}


def get_github_latest_release(owner_repo: str, cache: dict = None):
    """Fetch latest release info via API, falling back to web scraping if rate limited (HTTP 403)."""
    key = f"release:{owner_repo}"
    if cache is not None and key in cache:
        return cache[key]
    try:
        data = fetch_github_api(f"https://api.github.com/repos/{owner_repo}/releases/latest", cache=cache)
        if cache is not None:
            cache[key] = data
        return data
    except Exception:
        web = fetch_github_release_web(owner_repo)
        if web:
            if cache is not None:
                cache[key] = web
            return web
        raise


def get_github_repo_info(owner_repo: str, cache: dict = None):
    """Fetch repo info (description), falling back to web meta tags if rate limited."""
    key = f"repo:{owner_repo}"
    if cache is not None and key in cache:
        return cache[key]
    try:
        data = fetch_github_api(f"https://api.github.com/repos/{owner_repo}", cache=cache)
        if cache is not None:
            cache[key] = data
        return data
    except Exception:
        desc = ""
        try:
            req = urllib.request.Request(f"https://github.com/{owner_repo}", headers={"User-Agent": "Mozilla/5.0 (X11; Linux x86_64)"})
            with urllib.request.urlopen(req, timeout=8) as resp:
                html = resp.read().decode("utf-8")
            m = re.search(r'<meta property="og:description" content="([^"]+)"', html)
            if m:
                desc = m.group(1).rsplit(" - ", 1)[0].strip()
        except Exception:
            pass
        data = {"description": desc}
        if cache is not None:
            cache[key] = data
        return data


def check_github_update(mod: dict, cache: dict = None):
    """Check if a mod has an update on GitHub. Returns update info or None."""
    urls = mod.get("download_urls") or ([mod.get("download_url")] if mod.get("download_url") else [])
    owner_repo = None

    if mod.get("download_mode") == "github" and mod.get("github_repository"):
        p = parse_github_release_url(mod["github_repository"])
        if p:
            owner_repo = p["owner_repo"]

    if not owner_repo and urls:
        parsed_urls = [parse_github_release_url(u) for u in urls]
        if parsed_urls and all(parsed_urls):
            owner_repo = parsed_urls[0]["owner_repo"]

    if not owner_repo:
        return None

    rel = get_github_latest_release(owner_repo, cache=cache)
    latest_tag = rel.get("tag_name", "").strip()
    norm_latest = latest_tag.lstrip("vV")
    curr_ver = mod.get("version", "").strip().lstrip("vV")

    if not is_newer_version(norm_latest, curr_ver):
        return None

    needed_assets = mod.get("github_assets", [])
    if not needed_assets and urls:
        for u in urls:
            p = parse_github_release_url(u)
            if p and p.get("asset"):
                needed_assets.append(p["asset"])

    rel_assets = {a["name"].lower(): a for a in rel.get("assets", [])}
    new_urls = []
    for i, asset_name in enumerate(needed_assets):
        match = rel_assets.get(asset_name.lower())
        if not match:
            return None  # Asset missing in latest release, do not break links
        if i < len(urls) and "/releases/latest/download/" in urls[i]:
            new_urls.append(f"https://github.com/{owner_repo}/releases/latest/download/{quote(match['name'], safe='')}")
        else:
            new_urls.append(match.get("browser_download_url"))

    return {
        "mod": mod,
        "name": mod.get("name", owner_repo),
        "has_update": True,
        "old_version": mod.get("version", ""),
        "new_version": norm_latest or latest_tag,
        "new_urls": new_urls,
    }


# ==============================================================================
# GUI
# ==============================================================================

class RepoBuilder:
    def __init__(self, root, initial_path=None):
        self.root = root
        self.root.title("GTMM Repository Builder")
        self.root.geometry("960x680")
        self.root.minsize(780, 520)

        self.mods = []
        self.loaded_path = None

        self.repo_name = tk.StringVar(value="My Mod Repository")
        self.mod_name = tk.StringVar()
        self.version = tk.StringVar(value="1.0.0")
        self.description = tk.StringVar()
        self.author = tk.StringVar()
        self.download_mode = tk.StringVar(value="Manual links")
        self.github_repository = tk.StringVar()
        self.dependencies = tk.StringVar()
        self.search_filter = tk.StringVar()
        self.form_mode = tk.StringVar(value="Add Mod")

        self.download_urls = []
        self.github_assets = []
        self.resolved_url_preview = tk.StringVar()
        self.status = tk.StringVar(value="Add a mod, then save the repository JSON.")

        self.github_repository.trace_add("write", lambda *_: self.update_url_preview())

        self.build_menu()
        self.build_ui()

        if initial_path and Path(initial_path).is_file():
            self.load_path(initial_path)
        elif Path("repository.json").is_file():
            self.load_path("repository.json")

    def build_menu(self):
        menu = tk.Menu(self.root)
        file_menu = tk.Menu(menu, tearoff=False)
        file_menu.add_command(label="New", accelerator="Ctrl+N", command=self.new_repo)
        file_menu.add_command(label="Open JSON...", accelerator="Ctrl+O", command=self.open_repo)
        file_menu.add_command(label="Save", accelerator="Ctrl+S", command=self.save_repo)
        file_menu.add_command(label="Save As...", accelerator="Ctrl+Shift+S", command=self.save_repo_as)
        file_menu.add_separator()
        file_menu.add_command(label="Check for Updates", accelerator="Ctrl+U", command=self.check_updates)
        file_menu.add_command(label="Preview JSON", accelerator="Ctrl+P", command=self.preview_json)
        file_menu.add_separator()
        file_menu.add_command(label="Exit", command=self.root.destroy)
        menu.add_cascade(label="File", menu=file_menu)

        edit_menu = tk.Menu(menu, tearoff=False)
        edit_menu.add_command(label="Save / Add Mod", accelerator="Ctrl+Enter", command=self.save_mod)
        edit_menu.add_command(label="Fetch GitHub Info", accelerator="Ctrl+I", command=self.fetch_github_info)
        edit_menu.add_command(label="Focus Search Filter", accelerator="Ctrl+F", command=self._focus_filter)
        edit_menu.add_command(label="Clear Form / Filter", accelerator="Esc", command=self._on_escape)
        edit_menu.add_separator()
        edit_menu.add_command(label="Move Mod Up", accelerator="Alt+Up", command=self.move_up)
        edit_menu.add_command(label="Move Mod Down", accelerator="Alt+Down", command=self.move_down)
        edit_menu.add_command(label="Remove Selected Mod", accelerator="Del", command=self.remove_mod)
        menu.add_cascade(label="Edit", menu=edit_menu)

        self.root.config(menu=menu)

        self.root.bind("<Control-s>", lambda e: self.save_repo())
        self.root.bind("<Control-S>", lambda e: self.save_repo_as())
        self.root.bind("<Control-o>", lambda e: self.open_repo())
        self.root.bind("<Control-n>", lambda e: self.new_repo())
        self.root.bind("<Control-p>", lambda e: self.preview_json())
        self.root.bind("<Control-u>", lambda e: self.check_updates())
        self.root.bind("<Control-Return>", lambda e: self.save_mod())
        self.root.bind("<Control-KP_Enter>", lambda e: self.save_mod())
        self.root.bind("<Control-f>", self._focus_filter)
        self.root.bind("<Control-i>", lambda e: self.fetch_github_info())
        self.root.bind("<Escape>", self._on_escape)
        self.root.bind("<Alt-Up>", lambda e: (self.move_up(), "break")[1])
        self.root.bind("<Alt-Down>", lambda e: (self.move_down(), "break")[1])

    def _focus_filter(self, _event=None):
        self.filter_entry.focus_set()
        self.filter_entry.select_range(0, "end")
        return "break"

    def _on_escape(self, _event=None):
        if self.search_filter.get():
            self.search_filter.set("")
        else:
            self.clear_form()
        return "break"

    def _select_first_if_empty(self):
        if not self.tree.selection():
            children = self.tree.get_children()
            if children:
                self.tree.selection_set(children[0])
                self.tree.focus(children[0])

    def build_ui(self):
        outer = ttk.Frame(self.root, padding=14)
        outer.pack(fill="both", expand=True)
        outer.columnconfigure(0, weight=1)
        outer.rowconfigure(3, weight=1)

        # Header bar
        header = ttk.Frame(outer)
        header.grid(row=0, column=0, sticky="ew", pady=(0, 10))
        header.columnconfigure(1, weight=1)

        ttk.Label(header, text="GTMM Repository Builder", font=("TkDefaultFont", 15, "bold")).grid(row=0, column=0, sticky="w", padx=(0, 16))
        ttk.Label(header, text="Repo Name:").grid(row=0, column=1, sticky="e", padx=(0, 6))
        ttk.Entry(header, textvariable=self.repo_name, width=24).grid(row=0, column=2, sticky="e", padx=(0, 8))
        ttk.Button(header, text="🔄 Check for Updates", command=self.check_updates).grid(row=0, column=3, padx=(0, 4))
        ttk.Button(header, text="Preview JSON", command=self.preview_json).grid(row=0, column=4, padx=(0, 4))
        ttk.Button(header, text="Save JSON...", command=self.save_repo_as).grid(row=0, column=5)

        # Mod details form
        mod_frame = ttk.LabelFrame(outer, text="Mod details", padding=10)
        mod_frame.grid(row=1, column=0, sticky="ew", pady=(0, 10))
        mod_frame.columnconfigure(1, weight=1)
        mod_frame.columnconfigure(3, weight=1)

        ttk.Label(mod_frame, text="Mod name").grid(row=0, column=0, sticky="w", padx=(0, 6), pady=3)
        self.mod_name_entry = ttk.Entry(mod_frame, textvariable=self.mod_name)
        self.mod_name_entry.grid(row=0, column=1, sticky="ew", padx=(0, 10), pady=3)
        ttk.Label(mod_frame, text="Version").grid(row=0, column=2, sticky="w", padx=(0, 6), pady=3)
        ttk.Entry(mod_frame, textvariable=self.version, width=16).grid(row=0, column=3, sticky="ew", pady=3)

        ttk.Label(mod_frame, text="Author").grid(row=1, column=0, sticky="w", padx=(0, 6), pady=3)
        ttk.Entry(mod_frame, textvariable=self.author).grid(row=1, column=1, sticky="ew", padx=(0, 10), pady=3)
        ttk.Label(mod_frame, text="Dependencies").grid(row=1, column=2, sticky="w", padx=(0, 6), pady=3)
        ttk.Entry(mod_frame, textvariable=self.dependencies).grid(row=1, column=3, sticky="ew", pady=3)

        ttk.Label(mod_frame, text="Description").grid(row=2, column=0, sticky="w", padx=(0, 6), pady=3)
        ttk.Entry(mod_frame, textvariable=self.description).grid(row=2, column=1, columnspan=3, sticky="ew", pady=3)

        ttk.Label(mod_frame, text="Download mode").grid(row=3, column=0, sticky="w", padx=(0, 6), pady=3)
        mode_box = ttk.Combobox(
            mod_frame,
            textvariable=self.download_mode,
            values=("Manual links", "GitHub release"),
            state="readonly",
            width=18,
        )
        mode_box.grid(row=3, column=1, sticky="w", pady=3)
        mode_box.bind("<<ComboboxSelected>>", lambda e: self.show_download_mode())

        # Dynamic download fields
        self.source_fields = ttk.Frame(mod_frame)
        self.source_fields.grid(row=4, column=0, columnspan=4, sticky="ew", pady=3)
        self.source_fields.columnconfigure(0, weight=1)

        # GitHub frame
        self.github_fields = ttk.Frame(self.source_fields)
        self.github_fields.columnconfigure(1, weight=1)
        ttk.Label(self.github_fields, text="Repo URL").grid(row=0, column=0, sticky="w", padx=(0, 6), pady=2)
        gh_row = ttk.Frame(self.github_fields)
        gh_row.grid(row=0, column=1, sticky="ew", pady=2)
        gh_row.columnconfigure(0, weight=1)
        ttk.Entry(gh_row, textvariable=self.github_repository).grid(row=0, column=0, sticky="ew")
        ttk.Button(gh_row, text="⚡ Fetch Info", command=self.fetch_github_info).grid(row=0, column=1, padx=(4, 0))

        ttk.Label(self.github_fields, text="Asset names").grid(row=1, column=0, sticky="nw", padx=(0, 6), pady=2)
        self.github_assets_frame = ttk.Frame(self.github_fields)
        self.github_assets_frame.grid(row=1, column=1, sticky="ew", pady=2)
        self.github_assets_frame.columnconfigure(0, weight=1)

        ttk.Label(self.github_fields, text="Download link").grid(row=2, column=0, sticky="w", padx=(0, 6), pady=2)
        preview_entry = ttk.Entry(self.github_fields, textvariable=self.resolved_url_preview, state="readonly")
        preview_entry.grid(row=2, column=1, sticky="ew", pady=2)

        # Manual frame
        self.manual_fields = ttk.Frame(self.source_fields)
        self.manual_fields.columnconfigure(1, weight=1)
        ttk.Label(self.manual_fields, text="Download links").grid(row=0, column=0, sticky="nw", padx=(0, 6), pady=2)
        self.download_links_frame = ttk.Frame(self.manual_fields)
        self.download_links_frame.grid(row=0, column=1, sticky="ew", pady=2)
        self.download_links_frame.columnconfigure(0, weight=1)

        self.add_download_link_field()
        self.add_github_asset_field()
        self.show_download_mode()

        # Form action buttons
        actions = ttk.Frame(mod_frame)
        actions.grid(row=5, column=0, columnspan=4, sticky="e", pady=(4, 0))
        ttk.Button(actions, text="Clear (Esc)", command=self.clear_form).pack(side="left", padx=(0, 6))
        ttk.Button(actions, textvariable=self.form_mode, command=self.save_mod).pack(side="left")

        # Mod list frame
        list_frame = ttk.LabelFrame(outer, text="Mods in this repository", padding=8)
        list_frame.grid(row=3, column=0, sticky="nsew", pady=(0, 8))
        list_frame.columnconfigure(0, weight=1)
        list_frame.rowconfigure(1, weight=1)

        # Filter bar
        filter_bar = ttk.Frame(list_frame)
        filter_bar.grid(row=0, column=0, sticky="ew", pady=(0, 4))
        filter_bar.columnconfigure(1, weight=1)
        ttk.Label(filter_bar, text="Filter:").grid(row=0, column=0, padx=(0, 4))
        self.filter_entry = ttk.Entry(filter_bar, textvariable=self.search_filter)
        self.filter_entry.grid(row=0, column=1, sticky="ew")
        self.filter_entry.bind("<Down>", lambda e: (self.tree.focus_set(), self._select_first_if_empty(), "break")[2])
        self.search_filter.trace_add("write", lambda *_: self.refresh_list())
        ttk.Button(filter_bar, text="✖", width=3, command=lambda: self.search_filter.set("")).grid(row=0, column=2, padx=(4, 0))

        # Table
        columns = ("name", "version", "author", "dependencies", "description", "downloads")
        self.tree = ttk.Treeview(list_frame, columns=columns, show="headings", selectmode="browse")
        for col, label, w, stretch in (
            ("name", "Name", 140, False),
            ("version", "Version", 75, False),
            ("author", "Author", 110, False),
            ("dependencies", "Dependencies", 130, False),
            ("description", "Description", 160, False),
            ("downloads", "Download links (URLs)", 500, True),
        ):
            self.tree.heading(col, text=label)
            self.tree.column(col, width=w, minwidth=60, stretch=stretch)

        y_scroll = ttk.Scrollbar(list_frame, orient="vertical", command=self.tree.yview)
        x_scroll = ttk.Scrollbar(list_frame, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=y_scroll.set, xscrollcommand=x_scroll.set)

        self.tree.grid(row=1, column=0, sticky="nsew")
        y_scroll.grid(row=1, column=1, sticky="ns")
        x_scroll.grid(row=2, column=0, sticky="ew")
        self.tree.bind("<<TreeviewSelect>>", self.select_mod)
        self.tree.bind("<Delete>", lambda e: self.remove_mod())
        self.tree.bind("<BackSpace>", lambda e: self.remove_mod())
        self.tree.bind("<Return>", lambda e: (self.mod_name_entry.focus_set(), "break")[1])
        self.tree.bind("<KP_Enter>", lambda e: (self.mod_name_entry.focus_set(), "break")[1])
        self.tree.bind("<Control-c>", lambda e: self.copy_selected_url())

        # Bottom toolbar
        bottom = ttk.Frame(outer)
        bottom.grid(row=4, column=0, sticky="ew")
        ttk.Button(bottom, text="Remove selected", command=self.remove_mod).pack(side="left", padx=(0, 4))
        ttk.Button(bottom, text="▲ Up", width=5, command=self.move_up).pack(side="left", padx=(0, 2))
        ttk.Button(bottom, text="▼ Down", width=5, command=self.move_down).pack(side="left", padx=(0, 6))
        ttk.Button(bottom, text="📋 Copy URL", command=self.copy_selected_url).pack(side="left", padx=(0, 6))
        ttk.Button(bottom, text="🔄 Check for Updates", command=self.check_updates).pack(side="left")

        # Shortcuts hint & status bar
        status_bar = ttk.Frame(outer)
        status_bar.grid(row=5, column=0, sticky="ew", pady=(6, 0))
        status_bar.columnconfigure(0, weight=1)
        ttk.Label(status_bar, textvariable=self.status, foreground="#555555").grid(row=0, column=0, sticky="w")
        ttk.Label(
            status_bar,
            text="Ctrl+Enter: Save Mod | Ctrl+S: Save Repo | Ctrl+F: Filter | Alt+↑/↓: Move | Del: Remove",
            foreground="#888888",
            font=("TkDefaultFont", 8),
        ).grid(row=0, column=1, sticky="e")

    # --------------------------------------------------------------------------
    # Form & Table Logic
    # --------------------------------------------------------------------------

    def show_download_mode(self):
        if self.download_mode.get() == "GitHub release":
            self.manual_fields.grid_remove()
            self.github_fields.grid(row=0, column=0, sticky="ew")
        else:
            self.github_fields.grid_remove()
            self.manual_fields.grid(row=0, column=0, sticky="ew")

    def add_download_link_field(self, value=""):
        row = len(self.download_urls)
        var = tk.StringVar(value=value)
        self.download_urls.append(var)
        ttk.Entry(self.download_links_frame, textvariable=var).grid(row=row, column=0, sticky="ew", pady=2, padx=(0, 4))
        ttk.Button(self.download_links_frame, text="+", width=3, command=self.add_download_link_field).grid(row=row, column=1, padx=(0, 2))
        if row:
            ttk.Button(self.download_links_frame, text="−", width=3, command=lambda v=var: self.remove_download_link_field(v)).grid(row=row, column=2)

    def remove_download_link_field(self, var):
        self.download_urls.remove(var)
        values = [v.get() for v in self.download_urls] or [""]
        for child in self.download_links_frame.winfo_children():
            child.destroy()
        self.download_urls.clear()
        for v in values:
            self.add_download_link_field(v)

    def add_github_asset_field(self, value=""):
        row = len(self.github_assets)
        var = tk.StringVar(value=value)
        var.trace_add("write", lambda *_: self.update_url_preview())
        self.github_assets.append(var)
        ttk.Entry(self.github_assets_frame, textvariable=var).grid(row=row, column=0, sticky="ew", pady=2, padx=(0, 4))
        ttk.Button(self.github_assets_frame, text="+", width=3, command=self.add_github_asset_field).grid(row=row, column=1, padx=(0, 2))
        if row:
            ttk.Button(self.github_assets_frame, text="−", width=3, command=lambda v=var: self.remove_github_asset_field(v)).grid(row=row, column=2)
        self.update_url_preview()

    def remove_github_asset_field(self, var):
        self.github_assets.remove(var)
        values = [v.get() for v in self.github_assets] or [""]
        for child in self.github_assets_frame.winfo_children():
            child.destroy()
        self.github_assets.clear()
        for v in values:
            self.add_github_asset_field(v)
        self.update_url_preview()

    def update_url_preview(self, *_):
        repo = self.github_repository.get().strip()
        if not repo:
            self.resolved_url_preview.set("")
            return
        parsed = parse_github_release_url(repo)
        if parsed and parsed.get("asset"):
            current_assets = [v.get().strip() for v in self.github_assets if v.get().strip()]
            if not current_assets:
                if self.github_assets:
                    self.github_assets[0].set(parsed["asset"])
                else:
                    self.add_github_asset_field(parsed["asset"])
        assets = [v.get().strip() for v in self.github_assets if v.get().strip()]
        try:
            _, _, urls = github_asset_urls(repo, assets, self.mod_name.get() or "mod")
            self.resolved_url_preview.set(", ".join(urls))
        except Exception:
            self.resolved_url_preview.set(repo)

    def copy_selected_url(self):
        selected = self.tree.selection()
        if not selected:
            return
        mod = self.mods[int(selected[0])]
        urls = mod.get("download_urls") or ([mod.get("download_url")] if mod.get("download_url") else [])
        if urls:
            self.root.clipboard_clear()
            self.root.clipboard_append("\n".join(urls))
            self.status.set(f"Copied URL(s) to clipboard: {urls[0]}")

    def refresh_list(self):
        self.tree.delete(*self.tree.get_children())
        query = self.search_filter.get().strip().casefold()
        for index, mod in enumerate(self.mods):
            text = f"{mod['name']} {mod.get('author','')} {mod.get('description','')} {mod.get('version','')}".casefold()
            if query and query not in text:
                continue
            self.tree.insert(
                "", "end", iid=str(index),
                values=(
                    mod["name"], mod["version"], mod.get("author", ""), ", ".join(mod.get("dependencies", [])),
                    mod.get("description", ""), ", ".join(mod.get("download_urls", [mod.get("download_url", "")])),
                ),
            )

    def clear_form(self):
        self.tree.selection_remove(self.tree.selection())
        self.mod_name.set("")
        self.version.set("1.0.0")
        self.description.set("")
        self.author.set("")
        self.download_mode.set("Manual links")
        self.github_repository.set("")
        self.resolved_url_preview.set("")
        self.dependencies.set("")
        self.form_mode.set("Add Mod")
        for child in self.download_links_frame.winfo_children():
            child.destroy()
        for child in self.github_assets_frame.winfo_children():
            child.destroy()
        self.download_urls.clear()
        self.github_assets.clear()
        self.add_download_link_field()
        self.add_github_asset_field()
        self.show_download_mode()

    def select_mod(self, _event=None):
        selected = self.tree.selection()
        if not selected:
            return
        mod = self.mods[int(selected[0])]
        self.mod_name.set(mod["name"])
        self.version.set(mod["version"])
        self.description.set(mod.get("description", ""))
        self.author.set(mod.get("author", ""))
        self.dependencies.set(", ".join(mod.get("dependencies", [])))
        is_gh = mod.get("download_mode") == "github"
        self.download_mode.set("GitHub release" if is_gh else "Manual links")
        self.github_repository.set(mod.get("github_repository", ""))

        for child in self.download_links_frame.winfo_children():
            child.destroy()
        for child in self.github_assets_frame.winfo_children():
            child.destroy()
        self.download_urls.clear()
        self.github_assets.clear()

        if is_gh:
            for a in mod.get("github_assets", []):
                self.add_github_asset_field(a)
            if not self.github_assets:
                self.add_github_asset_field()
        else:
            for u in mod.get("download_urls", [mod.get("download_url", "")]):
                self.add_download_link_field(u)
            if not self.download_urls:
                self.add_download_link_field()

        self.show_download_mode()
        self.update_url_preview()
        urls = mod.get("download_urls") or ([mod.get("download_url")] if mod.get("download_url") else [])
        if urls:
            self.status.set(f"Selected: {mod['name']} v{mod['version']}  |  {', '.join(urls)}")
        self.form_mode.set("Update Selected")

    def save_mod(self):
        try:
            mod = validate_mod({
                "name": self.mod_name.get(),
                "version": self.version.get(),
                "description": self.description.get(),
                "author": self.author.get(),
                "download_mode": "github" if self.download_mode.get() == "GitHub release" else "manual",
                "download_urls": [v.get() for v in self.download_urls if v.get().strip()],
                "github_repository": self.github_repository.get(),
                "github_assets": [v.get() for v in self.github_assets if v.get().strip()],
                "dependencies": self.dependencies.get(),
            })
        except ValueError as err:
            messagebox.showerror("Check mod details", str(err), parent=self.root)
            return

        selected = self.tree.selection()
        if selected:
            self.mods[int(selected[0])] = mod
        else:
            self.mods.append(mod)
        self.refresh_list()
        self.clear_form()
        self.status.set(f"{len(self.mods)} mod(s) in repository.")

    def remove_mod(self):
        selected = self.tree.selection()
        if not selected:
            messagebox.showinfo("Remove mod", "Select a mod to remove first.", parent=self.root)
            return
        idx = int(selected[0])
        del self.mods[idx]
        self.refresh_list()
        self.clear_form()
        self.status.set(f"{len(self.mods)} mod(s) ready.")

    def move_up(self):
        selected = self.tree.selection()
        if not selected:
            return
        idx = int(selected[0])
        if idx > 0:
            self.mods[idx - 1], self.mods[idx] = self.mods[idx], self.mods[idx - 1]
            self.refresh_list()
            self.tree.selection_set(str(idx - 1))

    def move_down(self):
        selected = self.tree.selection()
        if not selected:
            return
        idx = int(selected[0])
        if idx < len(self.mods) - 1:
            self.mods[idx + 1], self.mods[idx] = self.mods[idx], self.mods[idx + 1]
            self.refresh_list()
            self.tree.selection_set(str(idx + 1))

    # --------------------------------------------------------------------------
    # GitHub Integration & Update Checking
    # --------------------------------------------------------------------------

    def fetch_github_info(self):
        """Fetch latest tag, assets, and repo description from GitHub to auto-fill form."""
        url = self.github_repository.get().strip()
        parsed = parse_github_release_url(url)
        if not parsed:
            messagebox.showerror("Invalid URL", "Enter a GitHub repo URL (e.g. https://github.com/owner/repo)", parent=self.root)
            return

        self.github_repository.set(parsed["repo_url"])
        self.status.set(f"Fetching GitHub info for {parsed['owner_repo']}...")

        def worker():
            try:
                rel = get_github_latest_release(parsed["owner_repo"])
                repo_info = get_github_repo_info(parsed["owner_repo"])
                desc = repo_info.get("description", "")

                def apply():
                    tag = rel.get("tag_name", "").strip().lstrip("vV")
                    if tag:
                        self.version.set(tag)
                    if not self.mod_name.get().strip():
                        self.mod_name.set(parsed["owner_repo"].split("/")[-1])
                    if not self.author.get().strip():
                        self.author.set(parsed["owner_repo"].split("/")[0])
                    if not self.description.get().strip() and desc:
                        self.description.set(desc)

                    assets = [a["name"] for a in rel.get("assets", []) if a["name"].endswith((".dll", ".zip"))]
                    if assets:
                        for child in self.github_assets_frame.winfo_children():
                            child.destroy()
                        self.github_assets.clear()
                        for a in assets:
                            self.add_github_asset_field(a)
                    self.status.set(f"Fetched {parsed['owner_repo']} (tag: {tag}).")

                self.root.after(0, apply)
            except Exception as e:
                self.root.after(0, lambda: messagebox.showerror("Fetch error", str(e), parent=self.root))

        threading.Thread(target=worker, daemon=True).start()

    def check_updates(self):
        """Scan each mod on GitHub, detect newer releases, and update versions and links."""
        if not self.mods:
            messagebox.showinfo("Check updates", "No mods in repository.", parent=self.root)
            return

        self.status.set("Checking GitHub for updates...")
        self.root.config(cursor="watch")

        def worker():
            cache = {}
            updates = []
            errors = []
            for mod in self.mods:
                try:
                    res = check_github_update(mod, cache=cache)
                    if res and res.get("has_update"):
                        updates.append(res)
                except Exception as e:
                    errors.append(f"{mod['name']}: {e}")
            return updates, errors

        def on_done(updates, errors):
            self.root.config(cursor="")
            if errors and not updates:
                messagebox.showerror("Update check errors", "\n".join(errors), parent=self.root)
                self.status.set("Update check encountered errors.")
                return

            if not updates:
                self.status.set("All mods are up to date.")
                messagebox.showinfo("Updates", "All mods are up to date!", parent=self.root)
                return

            msg_lines = [f"Found {len(updates)} update(s):\n"]
            for u in updates:
                msg_lines.append(f"• {u['name']}: {u['old_version']} → {u['new_version']}")
            msg_lines.append("\nUpdate mod versions and download links now?")

            if messagebox.askyesno("Updates available", "\n".join(msg_lines), parent=self.root):
                for u in updates:
                    m = u["mod"]
                    m["version"] = u["new_version"]
                    if len(u["new_urls"]) == 1:
                        m["download_url"] = u["new_urls"][0]
                        m["download_urls"] = [u["new_urls"][0]]
                    else:
                        m["download_urls"] = u["new_urls"]
                        m["download_url"] = u["new_urls"][0]
                self.refresh_list()
                self.clear_form()
                self.status.set(f"Updated {len(updates)} mod(s). Save repository to write changes.")
                messagebox.showinfo("Updated", f"Successfully updated {len(updates)} mod(s)!\nSave repository to write changes.", parent=self.root)

        def run():
            updates, errors = worker()
            self.root.after(0, on_done, updates, errors)

        threading.Thread(target=run, daemon=True).start()

    # --------------------------------------------------------------------------
    # File Operations
    # --------------------------------------------------------------------------

    def repo_json(self):
        return make_repo(self.repo_name.get(), self.mods)

    def preview_json(self):
        try:
            content = json.dumps(self.repo_json(), indent=2, ensure_ascii=False) + "\n"
        except ValueError as err:
            messagebox.showerror("Cannot preview yet", str(err), parent=self.root)
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
        if path:
            self.load_path(path)

    def load_path(self, path):
        try:
            name, mods = load_repo(path)
            self.repo_name.set(name)
            self.mods = [validate_mod(m) for m in mods]
            self.loaded_path = Path(path)
            self.refresh_list()
            self.clear_form()
            self.status.set(f"Loaded {self.loaded_path.name} ({len(self.mods)} mods).")
        except Exception as err:
            messagebox.showerror("Could not open repository", str(err), parent=self.root)

    def save_repo(self, save_as=False):
        try:
            content = json.dumps(self.repo_json(), indent=2, ensure_ascii=False) + "\n"
        except ValueError as err:
            messagebox.showerror("Check repository", str(err), parent=self.root)
            return

        if self.loaded_path and not save_as:
            try:
                self.loaded_path.write_text(content, encoding="utf-8")
                self.status.set(f"Saved {self.loaded_path.name}.")
            except OSError as err:
                messagebox.showerror("Could not save repository", str(err), parent=self.root)
            return

        initial_file = self.loaded_path.name if self.loaded_path else "repository.json"
        path = filedialog.asksaveasfilename(
            parent=self.root,
            title="Save repository JSON",
            defaultextension=".json",
            initialfile=initial_file,
            filetypes=[("JSON files", "*.json"), ("All files", "*.*")],
        )
        if not path:
            return

        try:
            Path(path).write_text(content, encoding="utf-8")
            self.loaded_path = Path(path)
            self.status.set(f"Saved {self.loaded_path.name}.")
            messagebox.showinfo("Repository saved", f"Saved {self.loaded_path.name} successfully.", parent=self.root)
        except OSError as err:
            messagebox.showerror("Could not save repository", str(err), parent=self.root)

    def save_repo_as(self):
        self.save_repo(save_as=True)

    def new_repo(self):
        self.repo_name.set("New Mod Repository")
        self.mods = []
        self.loaded_path = None
        self.refresh_list()
        self.clear_form()
        self.status.set("New repository.")


def cli_main(argv=None):
    """Command-line interface for headless repository checking and updating without touching a mouse."""
    import argparse

    parser = argparse.ArgumentParser(
        description="GTMM Mod Repository Builder & Updater (CLI & GUI)",
        epilog="Run without action flags (or with a file path) to open the graphical editor.",
    )
    parser.add_argument("file", nargs="?", default="repository.json", help="Path to repository.json (default: repository.json)")
    parser.add_argument("--check", "-c", action="store_true", help="Check GitHub for newer releases without modifying file")
    parser.add_argument("--update", "-u", action="store_true", help="Check GitHub and update repository.json with newer versions")
    parser.add_argument("--list", "-l", action="store_true", help="List all mods currently in the repository")
    args = parser.parse_args(argv)

    path = Path(args.file)
    if not path.is_file():
        print(f"Error: file not found: {path}", file=sys.stderr)
        return 1

    try:
        name, mods = load_repo(path)
    except Exception as e:
        print(f"Error loading {path}: {e}", file=sys.stderr)
        return 1

    if args.list:
        print(f"Repository: '{name}' ({len(mods)} mods in {path.name})")
        print("-" * 60)
        for i, m in enumerate(mods, 1):
            author = f" by {m['author']}" if m.get("author") else ""
            deps = f" (requires: {', '.join(m['dependencies'])})" if m.get("dependencies") else ""
            print(f"  {i}. {m['name']} v{m['version']}{author}{deps}")
        return 0

    if args.check or args.update:
        print(f"Checking updates for '{name}' ({len(mods)} mods) via GitHub API...")
        cache = {}
        updates = []
        for m in mods:
            try:
                res = check_github_update(m, cache=cache)
                if res and res.get("has_update"):
                    updates.append(res)
                    print(f"  ▲ {m['name']}: {res['old_version']} → {res['new_version']} (UPDATE AVAILABLE)")
                else:
                    print(f"  ✓ {m['name']}: {m['version']} (up to date)")
            except Exception as e:
                print(f"  ✗ {m['name']}: failed to check ({e})")

        print("-" * 60)
        if not updates:
            print("All mods are up to date.")
            return 0

        print(f"Found {len(updates)} update(s).")
        if args.update:
            for u in updates:
                mod = u["mod"]
                mod["version"] = u["new_version"]
                if len(u["new_urls"]) == 1:
                    mod["download_url"] = u["new_urls"][0]
                    mod["download_urls"] = [u["new_urls"][0]]
                else:
                    mod["download_urls"] = u["new_urls"]
                    mod["download_url"] = u["new_urls"][0]
            repo_data = make_repo(name, mods)
            path.write_text(json.dumps(repo_data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
            print(f"Updated and saved changes to {path}.")
        else:
            print("Run with --update (-u) to apply updates directly.")
        return 0

    return 0


def main():
    cli_flags = {"--check", "-c", "--update", "-u", "--list", "-l", "--help", "-h"}
    if any(arg in cli_flags for arg in sys.argv[1:]):
        sys.exit(cli_main())

    root = tk.Tk()
    initial_file = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("-") else None
    RepoBuilder(root, initial_path=initial_file)
    root.mainloop()


if __name__ == "__main__":
    main()
