=== Hermes Screenshots ===
Requires at least: 6.0
Requires PHP: 7.4
Stable tag: 1.1.0

Receives desktop screenshots from Hermes.Wpf via REST (no Supabase).

== Setup ==

1. Activate plugin.
2. Settings → Hermes Screenshots — copy API key.
3. Hermes.Wpf Settings — enable publish, paste site URL and API key.
4. Add shortcode: [hermes_screenshot]

== REST ==

POST /wp-json/hermes/v1/screenshot
Header: X-Hermes-Api-Key: <key from settings>
Body: multipart/form-data, field name "file" (PNG)

Optional fields: captured_at, foreground, width, height
