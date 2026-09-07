#!/usr/bin/env bash
set -euo pipefail
SRC="/mnt/d/Programming/AI_Agents/Hermes/Docs/TradingAnalytics/skills/open-local-artifact/SKILL.md"
DST="$HOME/.hermes/skills/domain/open-local-artifact"
mkdir -p "$DST"
sed 's/\r$//' "$SRC" > "$DST/SKILL.md"
grep -q 'отобрази визуально' "$DST/SKILL.md"
echo skill_sync_ok
MEM="$HOME/.hermes/memories/MEMORY.md"
if ! grep -q 'open-local-artifact.*Trading Analytics' "$MEM" 2>/dev/null; then
  printf '\n- Trading Analytics HTML: after write_file same turn open-local-artifact (quoted path, Trading Analytics space).\n' >> "$MEM"
  echo memory_patched
else
  echo memory_exists
fi
