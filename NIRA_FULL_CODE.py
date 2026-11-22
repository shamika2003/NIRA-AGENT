#!/usr/bin/env python3
"""Export NIRA's actual source as one readable Markdown file.

Usage (from the NIRA project folder):
    python NIRA_FULL_CODE.py
    python NIRA_FULL_CODE.py --cleanup --dry-run
    python NIRA_FULL_CODE.py --cleanup

The optional one-time cleanup removes obsolete documentation/export artifacts,
updates build.ps1 to use this exporter, and keeps a recoverable backup outside
of the repository. It never modifies agent runtime code.
"""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import uuid

OUTPUT = "NIRA_AGENT_FULL_CODE.md"

# Extension filtering deliberately excludes databases, caches, executables,
# credentials, and binary resources. New source files are discovered automatically.
SOURCE_EXTENSIONS = frozenset({
    ".cs", ".csproj", ".xaml", ".props", ".targets", ".sln", ".slnx",
    ".py", ".ps1", ".psm1", ".psd1", ".json", ".jsonc", ".yaml",
    ".yml", ".toml", ".xml", ".config", ".razor", ".resx", ".sql",
    ".js", ".jsx", ".ts", ".tsx", ".html", ".css", ".scss",
    ".sh", ".cmd", ".bat", ".fs", ".fsproj", ".vb", ".vbproj",
})
SOURCE_DOTFILES = frozenset({".editorconfig", ".gitignore", ".gitattributes"})
IGNORED_DIRS = frozenset({
    ".git", ".vs", ".vscode", ".idea", ".venv", "venv", "__pycache__",
    "node_modules", "bin", "obj", "dist", "build", "packages", "TestResults",
    ".pytest_cache", ".mypy_cache", ".ruff_cache", ".next", "coverage",
    "docs", "artifacts", "workspace", "logs", "temp", "tmp",
})
IGNORED_FILES = frozenset({
    OUTPUT.lower(), "manifest.json", "static_review.json", "package-lock.json",
    "pnpm-lock.yaml", "yarn.lock", "readme.md",
})
SENSITIVE_CONFIG_NAMES = frozenset({
    "appsettings.json", "launchsettings.json", "credentials.json", "secrets.json",
    "settings.local.json", "service-account.json", "id_rsa", "id_ed25519",
})
RETIRED_FILES = (
    "README.md",
    "STATIC_REVIEW.json",
    "docs/ARCHITECTURE.md",
    "docs/QUALITY-GATES.md",
    "NIRA_PROJECT_SOURCE_FILES.zip",
    "Export-NIRA-FullCode.ps1",
    "verify-source.ps1",
    "manifest.json",
    ".env.example",
)
# The known previously accepted build.ps1 must match before altering it.
# This is a source-migration guard, NOT a hardcoded AI decision rule.
PREVIOUS_BUILD_SHA256 = "5bda72c2e9666d0a3d333109d49d91352c387cac64e9727c6bfb7326c2a4773f"
BUILD_MARKER = "NIRA PYTHON FULL-SOURCE EXPORT"


class ExportError(RuntimeError):
    pass


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def link_or_junction(path: Path) -> bool:
    return path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction())


def excluded(name: str, *, directory: bool) -> bool:
    lower = name.lower()
    if directory:
        return lower in {v.lower() for v in IGNORED_DIRS} or lower.startswith(".nira-")
    if lower in IGNORED_FILES or lower in SENSITIVE_CONFIG_NAMES:
        return True
    if lower.startswith(".env") or lower.endswith((".local.json", ".user", ".suo")):
        return True
    if any(word in lower for word in ("private-key", "secret-key", "token-cache")):
        return True
    return False


def discover(root: Path) -> list[tuple[Path, str, bytes]]:
    result: list[tuple[Path, str, bytes]] = []
    for current, directories, filenames in os.walk(root, topdown=True, followlinks=False):
        parent = Path(current)
        directories[:] = sorted(
            (name for name in directories
             if not excluded(name, directory=True) and not link_or_junction(parent / name)),
            key=str.casefold,
        )
        for filename in sorted(filenames, key=str.casefold):
            if excluded(filename, directory=False):
                continue
            file = parent / filename
            if link_or_junction(file) or not file.is_file():
                continue
            if file.suffix.lower() not in SOURCE_EXTENSIONS and filename.lower() not in SOURCE_DOTFILES:
                continue
            relative = file.relative_to(root).as_posix()
            data = file.read_bytes()
            if b"\x00" in data:
                raise ExportError(f"Source contains NUL/binary bytes: {relative}")
            try:
                data.decode("utf-8-sig", errors="strict")
            except UnicodeDecodeError as error:
                raise ExportError(f"Source is not UTF-8: {relative}: {error}") from error
            result.append((file, relative, data))
    result.sort(key=lambda item: item[1].casefold())
    if not result:
        raise ExportError(f"No source files found under {root}")
    return result


def file_tree(relatives: list[str]) -> list[str]:
    tree: dict[str, dict] = {}
    for relative in relatives:
        node = tree
        for part in relative.split("/"):
            node = node.setdefault(part, {})
    lines = ["NIRA-AGENT/"]

    def visit(node: dict[str, dict], prefix: str) -> None:
        children = sorted(node.items(), key=lambda item: (not bool(item[1]), item[0].casefold()))
        for index, (name, descendants) in enumerate(children):
            last = index == len(children) - 1
            lines.append(prefix + ("└── " if last else "├── ") + name + ("/" if descendants else ""))
            if descendants:
                visit(descendants, prefix + ("    " if last else "│   "))
    visit(tree, "")
    return lines


def source_language(path: str) -> str:
    ext = Path(path).suffix.lower()
    return {
        ".cs": "csharp", ".csproj": "xml", ".xaml": "xml", ".props": "xml",
        ".targets": "xml", ".xml": "xml", ".resx": "xml", ".py": "python",
        ".ps1": "powershell", ".psm1": "powershell", ".psd1": "powershell",
        ".json": "json", ".jsonc": "json", ".yaml": "yaml", ".yml": "yaml",
        ".toml": "toml", ".html": "html", ".css": "css", ".js": "javascript",
        ".jsx": "jsx", ".ts": "typescript", ".tsx": "tsx", ".sql": "sql",
        ".sh": "bash", ".bat": "bat", ".cmd": "bat", ".razor": "razor",
    }.get(ext, "text")


def generate_markdown(files: list[tuple[Path, str, bytes]]) -> str:
    content = [
        "# NIRA — COMPLETE SOURCE CODE", "",
        "Auto-exported from the **actual current project files**. No generated ZIP, build",
        "artifacts, known credential files, local databases, README files, or old NIRA code are included.",
        "Review the exported code for any embedded secrets before sharing it.", "",
        f"**Source files:** {len(files)}", "",
        "## Project file tree", "", "```text",
        *file_tree([relative for _, relative, _ in files]),
        "```", "", "## Full source contents", "",
    ]
    for number, (_, relative, data) in enumerate(files, start=1):
        text = data.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
        ticks = max((len(match.group()) for match in re.finditer(r"`+", text)), default=0)
        fence = "`" * max(3, ticks + 1)
        content.extend([
            f"### {number}. `{relative}`", "",
            f"**SHA-256:** `{digest(data)}`  |  **Bytes:** {len(data)}", "",
            fence + source_language(relative) + "\n" + text +
            ("" if text.endswith("\n") else "\n") + fence,
            "",
        ])
    return "\n".join(content).rstrip("\n") + "\n"


def export(root: Path) -> None:
    destination = root / OUTPUT
    if link_or_junction(destination):
        raise ExportError("Refusing to overwrite a symlinked output file")
    files = discover(root)
    document = generate_markdown(files)
    temp_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", dir=root,
            prefix=".nira-source-", suffix=".tmp", delete=False,
        ) as handle:
            temp_path = Path(handle.name)
            handle.write(document)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temp_path, destination)
    finally:
        if temp_path and temp_path.exists():
            temp_path.unlink()
    print(f"NIRA SOURCE EXPORT PASS ({len(files)} real code/config files)")
    print(f"File: {destination}")
    print(f"SHA-256: {digest(destination.read_bytes())}")


def upgraded_build(old: bytes) -> bytes:
    text = old.decode("utf-8-sig")
    if BUILD_MARKER in text:
        return old
    if digest(old) != PREVIOUS_BUILD_SHA256:
        raise ExportError(
            "build.ps1 differs from the reviewed baseline; refusing to rewrite custom build logic. "
            "The source exporter can still run without cleanup."
        )
    remove = (
        "& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'verify-source.ps1')\n"
        "if ($LASTEXITCODE -ne 0) { throw 'Source verification failed.' }"
    )
    if remove not in text:
        raise ExportError("Expected old build verification block not found")
    # Drop the now-obsolete manifest comment too; the build still forces real recompilation.
    text = text.replace(
        "# Verify the actual source on disk before building; a manifest PASS alone does\n"
        "# not prove that an old DLL was rebuilt, so every build below is a forced Rebuild.\n",
        "# Rebuild all projects and execute the newly compiled test assembly.\n",
        1,
    )
    text = text.replace(remove,
        "# Source changes are tracked with Git; every build recompiles the real code.\n"
        "# " + BUILD_MARKER + "\n"
        "if (-not (Test-Path -LiteralPath (Join-Path $root 'NIRA_FULL_CODE.py'))) {\n"
        "    throw 'Python source exporter is missing.'\n"
        "}", 1)
    start = text.index("# Generate a full-source Markdown export only after a successful build and tests.")
    end = text.index("Write-Host 'NIRA FOUNDATION BUILD + KERNEL TESTS PASS'", start)
    text = text[:start] + (
        "# One source document, created from real files; no extra ZIP or manifest.\n"
        "if (-not $SkipExport) {\n"
        "    $exporter = Join-Path $root 'NIRA_FULL_CODE.py'\n"
        "    if (Get-Command py -ErrorAction SilentlyContinue) {\n"
        "        & py -3 $exporter --root $root\n"
        "    } elseif (Get-Command python -ErrorAction SilentlyContinue) {\n"
        "        & python $exporter --root $root\n"
        "    } else {\n"
        "        throw 'Python 3 is required for the source export.'\n"
        "    }\n"
        "    if ($LASTEXITCODE -ne 0) { throw 'Full code export failed.' }\n"
        "}\n"
    ) + text[end:]
    if "verify-source.ps1" in text or "Export-NIRA-FullCode.ps1" in text:
        raise ExportError("Legacy exporter is still referenced after migration")
    return text.encode("utf-8")


def cleanup(root: Path, dry_run: bool) -> None:
    build_path = root / "build.ps1"
    if not build_path.is_file() or link_or_junction(build_path):
        raise ExportError("Expected existing build.ps1 not found or unsafe")
    old_build = build_path.read_bytes()
    new_build = upgraded_build(old_build)
    paths = [root / name for name in RETIRED_FILES if (root / name).exists()]
    for file in paths:
        if not file.is_file() or link_or_junction(file):
            raise ExportError(f"Refusing to remove non-regular entry: {file}")
    print("Cleanup (agent C# files, tests, and run scripts stay untouched):")
    for file in paths:
        print("  REMOVE redundant file:", file.relative_to(root).as_posix())
    if new_build != old_build:
        print("  UPDATE build.ps1 to run Python exporter after tests")
    if dry_run:
        print("DRY RUN: project unchanged")
        return
    if not paths and new_build == old_build:
        print("Cleanup already applied")
        export(root)
        return
    backup_parent = Path(os.environ.get("LOCALAPPDATA") or (Path.home() / ".nira"))
    backup = backup_parent / "ELVARA" / "NIRA" / "SourceCleanup" / (
        "cleanup-" + uuid.uuid4().hex
    )
    backup.mkdir(parents=True, exist_ok=False)
    original_build = backup / "build.ps1"
    shutil.copy2(build_path, original_build)
    backed_up: list[Path] = []
    modified_build = False
    try:
        for file in paths:
            relative = file.relative_to(root)
            destination = backup / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, destination)
            backed_up.append(file)
        if new_build != old_build:
            temp = root / (".nira-build-" + uuid.uuid4().hex + ".tmp")
            try:
                temp.write_bytes(new_build)
                os.replace(temp, build_path)
            finally:
                if temp.exists():
                    temp.unlink()
            modified_build = True
        for file in backed_up:
            file.unlink()
        docs = root / "docs"
        if docs.is_dir() and not any(docs.iterdir()):
            docs.rmdir()
        export(root)
    except Exception:
        if modified_build:
            shutil.copy2(original_build, build_path)
        for file in backed_up:
            original = backup / file.relative_to(root)
            if original.exists() and not file.exists():
                file.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(original, file)
        raise
    print("CLEANUP PASS. Restorable original files at:", backup)
    print("No NIRA runtime source or test source was removed.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Export NIRA's current source into one Markdown file")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent,
                        help="NIRA project folder (defaults to the script's directory)")
    parser.add_argument("--cleanup", action="store_true", help="One-time safe cleanup of legacy export/docs")
    parser.add_argument("--dry-run", action="store_true", help="Preview cleanup without changing anything")
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    if not root.is_dir():
        raise ExportError("Project root is not a directory")
    if args.dry_run and not args.cleanup:
        parser.error("--dry-run requires --cleanup")
    if args.cleanup:
        cleanup(root, args.dry_run)
    else:
        export(root)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ExportError, OSError, UnicodeError) as exc:
        print("NIRA source export failed:", exc, file=sys.stderr)
        sys.exit(1)
