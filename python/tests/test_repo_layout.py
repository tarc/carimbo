"""Static checks for the agent files, the justfile recipes and the CI workflow shape.

Text-only on purpose (no YAML library): these files are contracts between the repo and its
readers (humans, Claude Code, OpenCode, GitHub Actions), and drift between them is silent.
"""

from __future__ import annotations

import re
import tomllib
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# The justfile must never echo a provider key. Same pattern as the task's acceptance criterion.
KEY_ECHO = re.compile(r"(echo|printf)[^#]*\$\{?(CARIMBO_)?ANTHROPIC_API_KEY")
# A recipe header at column 0: `name params:`. `name := value` and `set ...` lines do not match.
RECIPE_HEADER = re.compile(r"^(?P<name>[A-Za-z_][A-Za-z0-9_-]*)(?:\s+[^:\n]*?)?\s*:(?!=)")
JUST_TOKEN = re.compile(r"(?<![\w-])just\s+(?P<name>[A-Za-z_][A-Za-z0-9_-]*)")
CODE_SPAN = re.compile(r"`([^`\n]+)`")
SHELL_LINE = re.compile(r"^\s*(?P<command>just\s+.+)$", flags=re.MULTILINE)


def _read(relative: str) -> str:
    return (REPO_ROOT / relative).read_text(encoding="utf-8")


def _recipes() -> dict[str, list[str]]:
    """Recipe name to its body lines (the indented lines after the header)."""
    recipes: dict[str, list[str]] = {}
    current: str | None = None
    for line in _read("justfile").splitlines():
        if line.startswith(("#", "[")):
            continue
        if line and not line[0].isspace():
            match = RECIPE_HEADER.match(line)
            current = match["name"] if match else None
            if current is not None:
                recipes[current] = []
        elif current is not None:
            recipes[current].append(line)
    return recipes


def _just_mentions(text: str) -> list[str]:
    """Recipe names after ``just`` inside inline code spans and on command lines of code blocks."""
    commands = CODE_SPAN.findall(text) + [m["command"] for m in SHELL_LINE.finditer(text)]
    return [m["name"] for command in commands for m in JUST_TOKEN.finditer(command)]


def _section(text: str, heading: str) -> str:
    """Text between ``heading`` and the next level-2 heading."""
    start = text.index(heading) + len(heading)
    following = re.search(r"^## ", text[start:], flags=re.MULTILINE)
    return text[start : start + following.start()] if following else text[start:]


def test_agents_md_is_canonical_and_self_sufficient() -> None:
    agents = _read("AGENTS.md")
    for heading in ("## Commands", "## Conventions", "## Secrets and live calls"):
        assert heading in agents, f"AGENTS.md lacks {heading}"


def test_claude_md_only_imports_agents_md() -> None:
    assert _read("CLAUDE.md") == "@AGENTS.md\n"
    assert (REPO_ROOT / ".claude" / "CLAUDE.md").is_file()


def test_every_documented_just_recipe_exists() -> None:
    defined = set(_recipes())
    assert {"dotnet-check", "py-check", "check", "skeleton", "_with-provider-key"} <= defined
    for name in ("AGENTS.md", "README.md"):
        for recipe in _just_mentions(_read(name)):
            assert recipe in defined, f"{name} mentions `just {recipe}`, which is not a recipe"


def test_secretspec_procedure_is_documented() -> None:
    secrets = _section(_read("AGENTS.md"), "## Secrets and live calls")
    for needle in (
        "secretspec config global init",
        "secretspec set CARIMBO_ANTHROPIC_API_KEY",
        "secretspec run -- claude --continue",
        "SECRETSPEC_REASON",
        "optional",
    ):
        assert needle in secrets, f"AGENTS.md secrets section lacks {needle!r}"
    readme = _read("README.md")
    for needle in (
        "secretspec config global init",
        "secretspec set CARIMBO_ANTHROPIC_API_KEY",
        "secretspec run -- claude --continue",
    ):
        assert needle in readme, f"README.md lacks {needle!r}"


def test_secretspec_toml_declares_an_optional_key_and_the_development_profile() -> None:
    declared = tomllib.loads(_read("secretspec.toml"))
    profiles = declared["profiles"]
    # A required key would block `secretspec run` for anyone without one.
    assert profiles["default"]["CARIMBO_ANTHROPIC_API_KEY"]["required"] is False
    # `secretspec config global init` selects `development`; an undeclared profile fails to run.
    assert "development" in profiles


def test_live_recipes_resolve_the_key_through_secretspec_and_never_echo_it() -> None:
    justfile = _read("justfile")
    assert "secretspec run" in justfile
    assert "SECRETSPEC_REASON" in justfile
    echoing = [line for line in justfile.splitlines() if KEY_ECHO.search(line)]
    assert echoing == []
    recipes = _recipes()
    for live in ("spike-live", "skeleton"):
        assert any("_with-provider-key" in line for line in recipes[live]), live


def test_schema_check_regenerates_before_it_diffs() -> None:
    body = _recipes()["schema-check"]
    regenerate = next(i for i, line in enumerate(body) if re.search(r"\bschema\s*$", line))
    diff = next(i for i, line in enumerate(body) if "git diff --exit-code" in line)
    assert regenerate < diff
    assert "git diff --exit-code -- schema python/src/carimbo_models" in body[diff]


def test_ci_workflow_is_fork_safe_and_unfiltered() -> None:
    ci = _read(".github/workflows/ci.yml")
    assert "pull_request:" in ci
    for forbidden in ("pull_request_target", "secrets.", "paths:", "paths-ignore:"):
        assert forbidden not in ci, f"ci.yml contains {forbidden!r}"
    assert "contents: read" in ci
    assert "just dotnet-check" in ci
    assert "just py-check" in ci
    # One job owns regeneration, so two jobs never race on the generated files.
    assert ci.count("just schema-check") == 1


def test_ci_steps_after_setup_are_just_recipes() -> None:
    ci = _read(".github/workflows/ci.yml")
    defined = set(_recipes())
    run_lines = re.findall(r"^\s*-\s+run:\s*(.+)$", ci, flags=re.MULTILINE)
    assert run_lines, "ci.yml has no run steps"
    for command in run_lines:
        match = re.fullmatch(r"just (?P<name>[A-Za-z_][A-Za-z0-9_-]*)", command.strip())
        assert match, f"CI runs {command!r}, which is not a bare just recipe"
        assert match["name"] in defined


def test_global_json_selects_mtp_and_packages_are_pinned_exactly() -> None:
    assert "Microsoft.Testing.Platform" in _read("global.json")
    props = _read("dotnet/Directory.Packages.props")
    versions = re.findall(r'<PackageVersion\b[^>]*\bVersion="([^"]*)"', props)
    assert versions, "no PackageVersion entries found"
    for version in versions:
        assert not any(ch in version for ch in "*[("), f"floating package version {version!r}"
