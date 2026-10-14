from __future__ import annotations

from pathlib import Path


# ============================================================
# CONFIGURATION
# ============================================================

BASE_DIR = Path(__file__).resolve().parent

OUTPUT_FILE = BASE_DIR / "NIRA_AGENT_FULL_CODE.md"


# ============================================================
# FOLDERS TO COMPLETELY IGNORE
# ============================================================

# These are generated/build/dependency folders.
# They are NOT useful when giving NIRA source code to an AI.
IGNORE_DIRS = {
    ".git",
    ".vs",
    ".idea",
    ".vscode",

    "bin",
    "obj",

    "__pycache__",
    "node_modules",

    ".playwright",

    "packages",

    ".venv",
    "venv",
    "env",
}


# ============================================================
# FILES WHOSE CONTENT SHOULD BE EXPORTED
# ============================================================

CONTENT_EXTENSIONS = {
    # C# / .NET
    ".cs",
    ".csproj",
    ".sln",
    ".props",
    ".targets",

    # WPF / configuration
    ".xaml",
    ".xml",
    ".config",

    # NIRA prompts/config
    ".yaml",
    ".yml",
    ".json",

    # Documentation / text
    ".md",
    ".txt",

    # Scripts
    ".ps1",
    ".bat",
    ".cmd",

    # Web-related files, if present later
    ".html",
    ".css",
    ".js",
    ".ts",
}


# ============================================================
# BINARY / RESOURCE FILES
# ============================================================

# These will appear in the PROJECT TREE,
# but their binary contents will NOT be dumped into Markdown.
RESOURCE_EXTENSIONS = {
    ".png",
    ".jpg",
    ".jpeg",
    ".webp",
    ".gif",
    ".ico",
    ".svg",

    ".onnx",
    ".wav",
    ".mp3",

    ".db",
    ".sqlite",
    ".sqlite3",
}


# ============================================================
# SPECIAL FILE NAMES TO INCLUDE
# ============================================================

SPECIAL_TEXT_FILES = {
    ".editorconfig",
    ".gitignore",
    ".gitattributes",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
}


# ============================================================
# FILES TO NEVER EXPORT
# ============================================================

IGNORE_FILES = {
    OUTPUT_FILE.name,
    Path(__file__).name,

    # Common secret files
    ".env",
    ".env.local",
    ".env.development",
    ".env.production",
    "secrets.json",
}


# ============================================================
# MARKDOWN LANGUAGE MAP
# ============================================================

LANGUAGE_MAP = {
    ".cs": "csharp",
    ".csproj": "xml",
    ".sln": "text",
    ".props": "xml",
    ".targets": "xml",

    ".xaml": "xml",
    ".xml": "xml",
    ".config": "xml",

    ".yaml": "yaml",
    ".yml": "yaml",
    ".json": "json",

    ".md": "markdown",
    ".txt": "text",

    ".ps1": "powershell",
    ".bat": "bat",
    ".cmd": "bat",

    ".html": "html",
    ".css": "css",
    ".js": "javascript",
    ".ts": "typescript",

    ".editorconfig": "ini",
    ".gitignore": "text",
    ".gitattributes": "text",
}


# ============================================================
# HELPERS
# ============================================================

def should_ignore(path: Path) -> bool:
    """
    Check whether any part of this path belongs to
    an ignored/generated directory.
    """

    try:
        relative = path.relative_to(BASE_DIR)
    except ValueError:
        return True

    for part in relative.parts:
        if part in IGNORE_DIRS:
            return True

    if path.name in IGNORE_FILES:
        return True

    return False


def is_content_file(path: Path) -> bool:
    """
    Return True if this file's CONTENT should be included.
    """

    if path.name in SPECIAL_TEXT_FILES:
        return True

    return path.suffix.lower() in CONTENT_EXTENSIONS


def is_tree_file(path: Path) -> bool:
    """
    Files shown in the clean project tree.

    Includes:
        source code
        config
        prompts
        documentation
        images/resources
        models

    Excludes:
        bin
        obj
        dependencies
        generated files
    """

    if is_content_file(path):
        return True

    return path.suffix.lower() in RESOURCE_EXTENSIONS


def get_project_files() -> list[Path]:
    """
    Get files that should appear in the clean source tree.
    """

    files: list[Path] = []

    for path in BASE_DIR.rglob("*"):

        if not path.is_file():
            continue

        if should_ignore(path):
            continue

        if not is_tree_file(path):
            continue

        files.append(path)

    return sorted(
        files,
        key=lambda p: str(
            p.relative_to(BASE_DIR)
        ).lower()
    )


def get_content_files(
    project_files: list[Path]
) -> list[Path]:
    """
    From the clean tree, select only files whose text/code
    should actually be dumped into the Markdown document.
    """

    return [
        path
        for path in project_files
        if is_content_file(path)
    ]


# ============================================================
# READ TEXT SAFELY
# ============================================================

def read_text_file(path: Path) -> str:

    encodings = (
        "utf-8",
        "utf-8-sig",
        "utf-16",
        "cp1252",
        "latin-1",
    )

    for encoding in encodings:
        try:
            return path.read_text(
                encoding=encoding
            )

        except UnicodeDecodeError:
            continue

        except Exception as error:
            return (
                f"[ERROR READING FILE: {error}]"
            )

    return "[UNABLE TO DECODE FILE]"


# ============================================================
# TREE BUILDER
# ============================================================

def build_tree(files: list[Path]) -> str:

    tree: dict = {}

    for file_path in files:

        relative = file_path.relative_to(
            BASE_DIR
        )

        current = tree

        for part in relative.parts:
            current = current.setdefault(
                part,
                {}
            )

    lines = [
        f"{BASE_DIR.name}/"
    ]

    def walk(
        node: dict,
        prefix: str = ""
    ) -> None:

        items = list(node.items())

        for index, (name, children) in enumerate(items):

            is_last = (
                index == len(items) - 1
            )

            connector = (
                "└── " if is_last
                else "├── "
            )

            lines.append(
                prefix
                + connector
                + name
            )

            if children:

                extension = (
                    "    " if is_last
                    else "│   "
                )

                walk(
                    children,
                    prefix + extension
                )

    walk(tree)

    return "\n".join(lines)


# ============================================================
# MARKDOWN FENCE
# ============================================================

def get_code_fence(content: str) -> str:
    """
    Prevent a file containing ``` from accidentally
    breaking the generated Markdown.
    """

    longest = 3

    for line in content.splitlines():

        stripped = line.lstrip()

        if stripped.startswith("`"):

            count = 0

            for char in stripped:

                if char == "`":
                    count += 1
                else:
                    break

            longest = max(
                longest,
                count + 1
            )

    return "`" * longest


# ============================================================
# LANGUAGE DETECTION
# ============================================================

def markdown_language(path: Path) -> str:

    if path.name == ".editorconfig":
        return "ini"

    if path.name in {
        ".gitignore",
        ".gitattributes",
    }:
        return "text"

    return LANGUAGE_MAP.get(
        path.suffix.lower(),
        "text"
    )


# ============================================================
# EXPORT
# ============================================================

def main() -> None:

    print()
    print("=" * 72)
    print("NIRA AGENT - MARKDOWN EXPORTER")
    print("=" * 72)
    print()

    project_files = get_project_files()

    content_files = get_content_files(
        project_files
    )

    resource_files = [
        file
        for file in project_files
        if not is_content_file(file)
    ]

    output: list[str] = []

    # ========================================================
    # HEADER
    # ========================================================

    output.append(
        "# NIRA AGENT - FULL SOURCE CODE"
    )

    output.append("")

    output.append(
        "> Complete clean source export of the NIRA Agent project."
    )

    output.append("")

    output.append(
        f"**Project:** `{BASE_DIR.name}`"
    )

    output.append("")

    output.append(
        f"**Source/code files:** {len(content_files)}"
    )

    output.append("")

    output.append(
        f"**Resource files shown in tree:** {len(resource_files)}"
    )

    output.append("")

    output.append(
        f"**Total clean project files:** {len(project_files)}"
    )

    output.append("")

    output.append("---")
    output.append("")


    # ========================================================
    # PROJECT TREE
    # ========================================================

    output.append(
        "# 1. Project File Tree"
    )

    output.append("")

    output.append(
        "Generated build folders such as `bin/` and `obj/` "
        "are intentionally excluded."
    )

    output.append("")

    output.append("```text")

    output.append(
        build_tree(project_files)
    )

    output.append("```")

    output.append("")

    output.append("---")

    output.append("")


    # ========================================================
    # SOURCE CODE
    # ========================================================

    output.append(
        "# 2. Source Code and Configuration"
    )

    output.append("")

    output.append(
        "The following sections contain the actual source code, "
        "UI definitions, project files, prompts and configuration."
    )

    output.append("")

    for number, file_path in enumerate(
        content_files,
        start=1
    ):

        relative = file_path.relative_to(
            BASE_DIR
        )

        content = read_text_file(
            file_path
        )

        language = markdown_language(
            file_path
        )

        fence = get_code_fence(
            content
        )

        output.append("---")
        output.append("")

        output.append(
            f"## {number}. `{relative}`"
        )

        output.append("")

        output.append(
            f"**File:** `{relative}`"
        )

        output.append("")

        output.append(
            f"{fence}{language}"
        )

        output.append(
            content.rstrip()
        )

        output.append(fence)

        output.append("")


    # ========================================================
    # RESOURCE LIST
    # ========================================================

    if resource_files:

        output.append("---")
        output.append("")

        output.append(
            "# 3. Binary / Resource Files"
        )

        output.append("")

        output.append(
            "These files are part of the project and are shown "
            "in the tree, but their binary data is intentionally "
            "not written into this Markdown file."
        )

        output.append("")

        for file_path in resource_files:

            relative = file_path.relative_to(
                BASE_DIR
            )

            try:
                size = file_path.stat().st_size

                if size >= 1024 * 1024:
                    display_size = (
                        f"{size / (1024 * 1024):.2f} MB"
                    )

                elif size >= 1024:
                    display_size = (
                        f"{size / 1024:.2f} KB"
                    )

                else:
                    display_size = (
                        f"{size} bytes"
                    )

            except OSError:
                display_size = "unknown size"

            output.append(
                f"- `{relative}` — {display_size}"
            )

        output.append("")


    # ========================================================
    # WRITE OUTPUT
    # ========================================================

    final_text = "\n".join(
        output
    )

    OUTPUT_FILE.write_text(
        final_text,
        encoding="utf-8"
    )


    # ========================================================
    # FINISHED
    # ========================================================

    print("Export complete.")
    print()

    print(
        f"Project files found : {len(project_files)}"
    )

    print(
        f"Code/config files   : {len(content_files)}"
    )

    print(
        f"Resource files      : {len(resource_files)}"
    )

    print()

    print(
        f"Output:"
    )

    print(
        OUTPUT_FILE
    )

    print()
    print("=" * 72)


if __name__ == "__main__":
    main()