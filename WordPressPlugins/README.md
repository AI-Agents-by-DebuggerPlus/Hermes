# WordPress plugins (Hermes)

| Плагин | Папка | Назначение |
|--------|--------|------------|
| Hermes Screenshots | `hermes-screenshots/` | Приём **чистого** PNG с Hermes.Wpf по REST, показ на сайте |

## Hermes Screenshots (без Supabase)

1. Установить плагин в `wp-content/plugins/hermes-screenshots/`, активировать.
2. **Settings → Hermes Screenshots** — скопировать **API key**; endpoint: `POST /wp-json/hermes/v1/screenshot`.
3. **Hermes.Wpf → Settings** — включить публикацию, указать URL сайта и тот же API key.
4. На страницу: `[hermes_screenshot]`

Файл сохраняется в `wp-content/uploads/hermes-screenshots/latest.png` и отображается shortcode'ом.
