#!/usr/bin/env bash
# Open https/http URL in AVG Secure Browser (Windows).
set -euo pipefail
URL="${1:-}"
if [[ -z "$URL" ]]; then
  echo "usage: open.sh 'https://...'" >&2
  exit 2
fi
case "$URL" in
  http://*|https://*) ;;
  *)
    echo "refusing non-http(s) URL: $URL" >&2
    exit 2
    ;;
esac
PS_URL="${URL//\'/\'\'}"
powershell.exe -NoProfile -Command "
  \$u = '${PS_URL}'
  \$avg = 'C:\Program Files\AVG\Browser\Application\AVGBrowser.exe'
  if (Test-Path \$avg) {
    Start-Process -FilePath \$avg -ArgumentList \$u
    Write-Output 'ok avg'
  } else {
    Start-Process \$u
    Write-Output 'ok default-fallback'
  }
"
