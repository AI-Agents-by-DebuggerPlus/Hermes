# curl for Hermes.EnglishLearning.Xp (Windows XP)

Bundled here: **curl 7.84.0 win32-mingw (OpenSSL)** — required on XP because stock Schannel has no TLS 1.2.

Files that must ship with the app:

- `curl.exe`
- `libcurl.dll`
- `curl-ca-bundle.crt`

On XP you may also need Universal CRT (UCRT) redistributable x86 if curl fails to start.

The build copies `tools\**\*` to the output folder automatically.
