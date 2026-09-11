#!/usr/bin/env python3
"""Pre-flight MEMORY.md compactor (Hermes self-learning harness, design §1).

Deterministic first; OpenRouter auxiliary LLM only if still over budget.
Evicted entries go to MEMORY.archive.jsonl.
Project-specific lines can be appended to <project>/hermes/project.md.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

BUDGET = 2200
SOFT_TRIGGER = 0.85
KEEP_RATIO = 0.80
OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"
DEFAULT_MODEL = "openrouter/free"

SPECIFIC = re.compile(
    r"(HermesProjects|Utilities|Accountant|ProjectManager|"
    r"[A-Za-z]:\\|/[Mm]nt/|\\.xlsx|\\.md|\\$\\d|skill `)",
    re.I,
)


def split_entries(text: str) -> list[str]:
    raw = text.replace("\r\n", "\n").strip()
    if not raw:
        return []
    if "§" in raw:
        parts = [p.strip() for p in raw.split("§")]
        return [p for p in parts if p]
    parts = re.split(r"\n(?=## )", raw)
    return [p.strip() for p in parts if p.strip()]


def load_meta(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def entry_id(text: str) -> str:
    return re.sub(r"\s+", " ", text)[:80]


def score(text: str, meta: dict) -> float:
    info = meta.get(entry_id(text), {})
    age = 0.0
    added = info.get("added_at")
    if added:
        try:
            then = datetime.fromisoformat(added.replace("Z", "+00:00"))
            age = max(0.0, (datetime.now(timezone.utc) - then).total_seconds() / 86400.0)
        except ValueError:
            age = 0.0
    usage = float(info.get("ref_count") or 0)
    length_penalty = max(0, len(text) - 180) / 40.0
    spec = 8.0 if SPECIFIC.search(text) or len(text) > 500 else 0.0
    if "[System / Hermes WPF]" in text or "flashcard_start" in text:
        spec += 12.0
    return usage * 3.0 - age * 0.15 - length_penalty - spec


def is_project_bound(text: str) -> bool:
    if len(text) > 500 or "[System / Hermes WPF]" in text:
        return False
    return bool(SPECIFIC.search(text))


def append_project(project_root: Path, entries: list[str]) -> None:
    dest = project_root / "hermes" / "project.md"
    dest.parent.mkdir(parents=True, exist_ok=True)
    existing = dest.read_text(encoding="utf-8") if dest.exists() else ""
    block = ["", "## Memory compactor (moved from MEMORY.md)", ""]
    for e in entries:
        line = " ".join(text_line for text_line in e.splitlines() if text_line.strip())
        if line and line not in existing:
            block.append(f"- {line[:400]}")
    if len(block) > 3:
        dest.write_text(existing.rstrip() + "\n" + "\n".join(block) + "\n", encoding="utf-8")


def archive(archive_path: Path, entries: list[str], reason: str) -> None:
    if not entries:
        return
    now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    with archive_path.open("a", encoding="utf-8") as fh:
        for e in entries:
            fh.write(json.dumps({"ts": now, "reason": reason, "text": e}, ensure_ascii=False) + "\n")


def render(entries: list[str]) -> str:
    return "\n§\n".join(entries).strip() + ("\n" if entries else "")


def total_len(entries: list[str]) -> int:
    if not entries:
        return 0
    return sum(len(e) for e in entries) + 3 * (len(entries) - 1)


def resolve_openrouter_key() -> str:
    for env_name in ("OPENROUTER_API_KEY", "HERMES_OPENROUTER_API_KEY"):
        v = (os.environ.get(env_name) or "").strip()
        if v:
            return v
    # Optional: ~/.hermes/.env line OPENROUTER_API_KEY=...
    env_path = Path.home() / ".hermes" / ".env"
    if env_path.is_file():
        try:
            for line in env_path.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                k, _, val = line.partition("=")
                if k.strip() in ("OPENROUTER_API_KEY", "HERMES_OPENROUTER_API_KEY"):
                    return val.strip().strip('"').strip("'")
        except OSError:
            pass
    return ""


def llm_compress(entries: list[str], target_chars: int) -> list[str] | None:
    """Ask OpenRouter to compress entries. Returns None on skip/failure."""
    api_key = resolve_openrouter_key()
    if not api_key or not entries:
        return None

    numbered = "\n".join(f"{i + 1}. {e[:600]}" for i, e in enumerate(entries))
    prompt = (
        f"Compress these {len(entries)} memory facts into fewer lines "
        f"(target total ≤ {target_chars} characters). "
        "Do not invent facts. Keep distinguishable facts. "
        "Each fact ≤ 1 line. Return ONLY a JSON array of strings.\n\n"
        f"{numbered}"
    )
    model = (os.environ.get("HERMES_COMPACT_MODEL") or DEFAULT_MODEL).strip()
    body = {
        "model": model,
        "messages": [
            {
                "role": "system",
                "content": "You compress Hermes MEMORY.md facts. Reply with JSON array of strings only.",
            },
            {"role": "user", "content": prompt},
        ],
    }
    req = urllib.request.Request(
        OPENROUTER_URL,
        data=json.dumps(body).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
            "HTTP-Referer": "https://github.com/hermes-wpf",
            "X-Title": "Hermes Memory Compactor",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=45) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, json.JSONDecodeError, OSError) as ex:
        print(f"llm-compress-skip {ex}", file=sys.stderr)
        return None

    try:
        content = payload["choices"][0]["message"]["content"].strip()
    except (KeyError, IndexError, TypeError, AttributeError):
        return None

    # Strip optional markdown fence
    if content.startswith("```"):
        content = re.sub(r"^```(?:json)?\s*", "", content)
        content = re.sub(r"\s*```$", "", content)

    try:
        parsed = json.loads(content)
    except json.JSONDecodeError:
        # Try to find array substring
        m = re.search(r"\[.*\]", content, re.S)
        if not m:
            return None
        try:
            parsed = json.loads(m.group(0))
        except json.JSONDecodeError:
            return None

    if not isinstance(parsed, list):
        return None
    out = [str(x).strip() for x in parsed if str(x).strip()]
    return out or None


def pack_by_score(entries: list[str], meta: dict, limit: int) -> list[str]:
    ranked = sorted(entries, key=lambda e: score(e, meta), reverse=True)
    kept: list[str] = []
    used = 0
    for e in ranked:
        extra = len(e) + (3 if kept else 0)
        if used + extra <= limit:
            kept.append(e)
            used += extra
    return kept


def maybe_compact(memory_dir: Path, project_root: Path | None) -> str:
    memory_path = memory_dir / "MEMORY.md"
    if not memory_path.exists():
        return "missing"
    text = memory_path.read_text(encoding="utf-8")
    if len(text) < BUDGET * SOFT_TRIGGER:
        return f"ok {len(text)}<{int(BUDGET * SOFT_TRIGGER)}"

    meta_path = memory_dir / "memory_meta.json"
    meta = load_meta(meta_path)
    entries = split_entries(text)
    now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    for e in entries:
        meta.setdefault(entry_id(e), {"added_at": now, "ref_count": 0})

    project_bound: list[str] = []
    global_entries: list[str] = []
    for e in entries:
        if is_project_bound(e) and project_root and project_root.is_dir():
            project_bound.append(e)
        else:
            global_entries.append(e)

    limit = int(BUDGET * KEEP_RATIO)
    llm_note = ""
    remaining = list(global_entries)

    # Design §1.3: if still over budget after project split — try auxiliary LLM first.
    if total_len(remaining) > limit:
        compressed = llm_compress(remaining, target_chars=limit)
        if compressed is not None and total_len(compressed) <= total_len(remaining):
            remaining = compressed
            llm_note = f" llm={len(global_entries)}->{len(remaining)}"

    kept = remaining
    if total_len(kept) > limit:
        kept = pack_by_score(kept, meta, limit)

    evicted = [e for e in global_entries if e not in kept]
    # Also archive any original global entries replaced by LLM rewrite
    if llm_note and kept != global_entries:
        for e in global_entries:
            if e not in kept and e not in evicted:
                evicted.append(e)

    if project_root and project_bound:
        append_project(project_root, project_bound)
    archive(memory_dir / "MEMORY.archive.jsonl", evicted + project_bound, "compact")
    memory_path.write_text(render(kept), encoding="utf-8")
    meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    return (
        f"compacted {len(text)}->{memory_path.stat().st_size} "
        f"kept={len(kept)} archived={len(evicted) + len(project_bound)}{llm_note}"
    )


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--memory-dir", required=True)
    p.add_argument("--project-root", default="")
    args = p.parse_args()
    root = Path(args.project_root) if args.project_root else None
    try:
        print(maybe_compact(Path(args.memory_dir), root))
    except OSError as ex:
        print(f"compact-skip {ex}", file=sys.stderr)
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
