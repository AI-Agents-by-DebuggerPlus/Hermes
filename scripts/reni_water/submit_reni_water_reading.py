#!/usr/bin/env python3
"""
Reni vodokanal: copy "Показник на початок місяця" → "Новий показник", submit, screenshot.
(Site UI is Ukrainian; README may use Russian labels for convenience.)

  python submit_reni_water_reading.py --login   # once: save session
  python submit_reni_water_reading.py         # monthly job (1st of month via Task Scheduler)
  python submit_reni_water_reading.py --ack     # stop hourly reminders
  python submit_reni_water_reading.py --notify  # hourly reminder if pending ack
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_ENV = SCRIPT_DIR / "reni_water.env"
DEFAULT_COUNTER_URL = "https://my.renivodokanal.od.ua/lickar/main/counter-pokaz"
DEFAULT_SCREENSHOT_DIR = Path(r"d:\Documents\Utilities\water\HermesScreenShots")
DEFAULT_PROFILE_DIR = Path(r"d:\Documents\Utilities\water\browser-profile")
DEFAULT_PENDING_ACK = Path(r"d:\Documents\Utilities\water\pending_ack.json")

# Site UI is Ukrainian (see Screenshot_30). Russian synonyms kept as fallback only.
BEGIN_COL_MARKERS = (
    "початок місяця",
    "показник на початок",
    "на початок місяця",
    "началу месяца",  # fallback if page ever shows Russian
    "beginning of month",
)
NEW_COL_MARKERS = (
    "новий показник",
    "новый показатель",
    "new reading",
)
SUBMIT_BUTTON_MARKERS = ("передати", "передать", "submit", "надіслати")

# Confirmation after submit (Screenshot_3). Do not use bare «прийнято» — it matches the column header.
ROW_ACCEPTANCE_PHRASES = (
    "показания приняты",
    "показання прийняті",
    "показання прийнято",
)

LOGIN_MARKERS = (
    "Увійти",
    "Запам'ятати мене",
    "Забули пароль",
    'type="password"',
)

# Reduce headless / automation blocks; same profile used for --login and monthly submit.
CHROMIUM_ARGS = [
    "--disable-blink-features=AutomationControlled",
]


def configure_stdio_utf8() -> None:
    if sys.platform != "win32":
        return
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            try:
                reconfigure(encoding="utf-8", errors="replace")
            except Exception:
                pass


def load_env_file(path: Path) -> None:
    if not path.is_file():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip().strip('"').strip("'")
        if key and key not in os.environ:
            os.environ[key] = value


def cfg() -> dict:
    load_env_file(DEFAULT_ENV)
    override = os.environ.get("RENI_METER_READING", "").strip()
    return {
        "url": os.environ.get("RENI_COUNTER_URL", DEFAULT_COUNTER_URL).strip(),
        "reading_override": override,
        "use_beginning_column": os.environ.get("RENI_USE_BEGINNING_COLUMN", "1").strip()
        not in ("0", "false", "False", "no"),
        "screenshot_dir": Path(
            os.environ.get("RENI_SCREENSHOT_DIR", str(DEFAULT_SCREENSHOT_DIR))
        ),
        "profile_dir": Path(
            os.environ.get("RENI_BROWSER_PROFILE_DIR", str(DEFAULT_PROFILE_DIR))
        ),
        "pending_ack_path": Path(
            os.environ.get("RENI_PENDING_ACK_PATH", str(DEFAULT_PENDING_ACK))
        ),
        "login_user": os.environ.get("RENI_LOGIN_USER", "").strip(),
        "login_password": os.environ.get("RENI_LOGIN_PASSWORD", "").strip(),
        "login_url": os.environ.get(
            "RENI_LOGIN_URL", "https://my.renivodokanal.od.ua/user/login"
        ).strip(),
    }


def normalize_reading(text: str) -> str:
    t = (text or "").strip().replace("\xa0", " ").replace(",", ".")
    m = re.search(r"(\d+(?:\.\d+)?)", t)
    if not m:
        raise ValueError(f"no numeric reading in {text!r}")
    num = m.group(1)
    if "." in num:
        whole, frac = num.split(".", 1)
        if frac.strip("0") == "":
            return whole
    return num


def column_index(headers: list[str], markers: tuple[str, ...]) -> int:
    lowered = [h.lower() for h in headers]
    for i, h in enumerate(lowered):
        if any(m in h for m in markers):
            return i
    raise ValueError(f"column not found for markers {markers!r}; headers={headers!r}")


def looks_like_login_page(html: str, url: str) -> bool:
    if is_counter_page_ready_html(html):
        return False
    lower = html.lower()
    if any(m.lower() in lower for m in LOGIN_MARKERS[:3]):
        return True
    if 'type="password"' in lower:
        return True
    return False


def is_counter_page_ready_html(html: str) -> bool:
    lower = (html or "").lower()
    has_submit = "передат" in lower or "надісл" in lower
    has_meter = "показник" in lower or "початок місяця" in lower or "counter-pokaz" in lower
    has_password_form = 'type="password"' in lower and "увійти" in lower
    return has_submit and has_meter and not has_password_form


def is_counter_page_ready(page) -> bool:
    try:
        page.wait_for_load_state("domcontentloaded", timeout=30_000)
        html = page.content()
        if is_counter_page_ready_html(html):
            return True
        data = page.evaluate(
            """() => {
                const norm = (s) => (s || '').replace(/\\s+/g, ' ').trim().toLowerCase();
                const submitNeedles = ['передати', 'передать', 'надіслати', 'submit'];
                const hasSubmit = Array.from(document.querySelectorAll('button, a, input[type=submit]'))
                    .some(el => {
                        const t = norm(el.textContent) + ' ' + norm(el.value);
                        return submitNeedles.some(n => t.includes(n));
                    });
                const hasInput = document.querySelector("input[type='text'], input[type='number']") !== null;
                const body = norm(document.body ? document.body.innerText : '');
                const hasMeter = body.includes('показник') || body.includes('початок місяця');
                const onLogin = body.includes('увійти') && document.querySelector("input[type='password']");
                return hasSubmit && hasInput && hasMeter && !onLogin;
            }"""
        )
        return bool(data)
    except Exception:
        return False


def launch_persistent_context(playwright, profile_dir: Path, *, headless: bool):
    return playwright.chromium.launch_persistent_context(
        user_data_dir=str(profile_dir),
        headless=headless,
        locale="uk-UA",
        args=CHROMIUM_ARGS,
        ignore_default_args=["--enable-automation"],
        viewport={"width": 1360, "height": 900},
    )


def try_auto_login(page, username: str, password: str) -> bool:
    if not username or not password:
        return False

    print("AUTO_LOGIN: trying credentials from reni_water.env ...")
    try:
        page.wait_for_load_state("domcontentloaded", timeout=60_000)
        pwd = page.locator("input[type='password']").first
        pwd.wait_for(state="visible", timeout=15_000)
        user_field = page.locator(
            "input[type='text'], input[type='email'], input[type='tel'], input:not([type])"
        ).first
        user_field.fill(username)
        pwd.fill(password)

        clicked = False
        for label in ("Увійти", "Войти", "Login", "Вхід"):
            btn = page.get_by_role("button", name=label)
            if btn.count() > 0:
                btn.first.click()
                clicked = True
                break
        if not clicked:
            submit = page.locator("button[type='submit'], input[type='submit']").first
            if submit.count() > 0:
                submit.click()
                clicked = True
        if not clicked:
            return False

        page.wait_for_timeout(2_500)
        try:
            page.wait_for_load_state("networkidle", timeout=60_000)
        except Exception:
            pass
        if "counter-pokaz" not in page.url.lower():
            page.goto(
                os.environ.get("RENI_COUNTER_URL", DEFAULT_COUNTER_URL),
                wait_until="domcontentloaded",
                timeout=120_000,
            )
        return is_counter_page_ready(page)
    except Exception as ex:
        print(f"AUTO_LOGIN_FAILED: {ex}")
        return False


def ensure_logged_in(
    page, counter_url: str, login_user: str, login_password: str, login_url: str
) -> bool:
    page.goto(counter_url, wait_until="domcontentloaded", timeout=120_000)
    if is_counter_page_ready(page):
        print("SESSION_OK: already on meter readings page.")
        return True

    if login_url and login_url not in page.url:
        page.goto(login_url, wait_until="domcontentloaded", timeout=120_000)

    if try_auto_login(page, login_user, login_password):
        if not is_counter_page_ready(page):
            page.goto(counter_url, wait_until="domcontentloaded", timeout=120_000)
        if is_counter_page_ready(page):
            print("SESSION_OK: auto-login succeeded.")
            return True

    return False


def screenshot_path(out_dir: Path, tag: str) -> Path:
    out_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    return out_dir / f"reni_water_{tag}_{stamp}.png"


def write_pending_ack(
    path: Path,
    reading: str,
    screenshot: Path,
    *,
    auth_required: bool = False,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if auth_required:
        msg = (
            "Нужен вход на сайт Водоканал. Выполните run_submit.ps1 -login, "
            "затем снова передачу. Подтвердите: принял / понял или -Ack."
        )
    else:
        msg = (
            "Показания воды переданы. Проверьте скриншот и ответьте: "
            "принял / понял (или run_submit.ps1 -Ack)."
        )
    payload = {
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "reading_submitted": reading,
        "screenshot": str(screenshot),
        "auth_required": auth_required,
        "message_uk": msg,
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"PENDING_ACK: {path}")


def clear_pending_ack(path: Path) -> int:
    if path.is_file():
        path.unlink()
        print("ACK_OK: reminders stopped.")
    else:
        print("ACK_OK: nothing pending.")
    return 0


def notify_pending(path: Path) -> int:
    if not path.is_file():
        return 0
    data = json.loads(path.read_text(encoding="utf-8"))
    reading = data.get("reading_submitted", "?")
    shot = data.get("screenshot", "")
    created = data.get("created_utc", "")
    print(
        "NOTIFY: Reni vodokanal — показания переданы.\n"
        f"  Показание: {reading}\n"
        f"  Скриншот: {shot}\n"
        f"  С {created}\n"
        "  Подтвердите: .\\run_submit.ps1 -Ack  (или «принял» / «понял» в Hermes)"
    )
    return 1


def extract_headers_and_reading(page) -> tuple[list[str], str]:
    """Read table header texts and beginning-of-month value from the submit row."""
    data = page.evaluate(
        """() => {
            const norm = (s) => (s || '').replace(/\\s+/g, ' ').trim().toLowerCase();
            const submitNeedles = ['передати', 'передать', 'надіслати', 'submit'];
            const btn = Array.from(document.querySelectorAll('button, a, input[type=button], input[type=submit]'))
                .find(el => {
                    const t = norm(el.textContent) + ' ' + norm(el.value);
                    return submitNeedles.some(n => t.includes(n));
                });
            if (!btn) return { error: 'submit button (Передати/Передать) not found' };
            const row = btn.closest('tr');
            if (!row) return { error: 'no table row for submit button' };
            const table = row.closest('table');
            if (!table) return { error: 'no table' };
            let headerRow = table.querySelector('thead tr');
            if (!headerRow) {
                const rows = Array.from(table.querySelectorAll('tr'));
                headerRow = rows.find(r => r.querySelectorAll('th').length > 0) || rows[0];
            }
            const headers = Array.from(headerRow.querySelectorAll('th, td')).map(c => norm(c.textContent));
            const cells = Array.from(row.querySelectorAll('td, th')).map(c => norm(c.textContent));
            return { headers, cells };
        }"""
    )
    if data.get("error"):
        raise RuntimeError(data["error"])
    headers: list[str] = data["headers"]
    cells: list[str] = data["cells"]
    begin_idx = None
    lowered = [h.lower() for h in headers]
    for i, h in enumerate(lowered):
        if any(
            m in h
            for m in ["початок місяця", "початку місяця", "на початок", "началу месяца"]
        ):
            begin_idx = i
            break
    if begin_idx is None:
        begin_idx = column_index(headers, BEGIN_COL_MARKERS)
    if begin_idx >= len(cells):
        raise RuntimeError(f"row has {len(cells)} cells, need index {begin_idx}; headers={headers}")
    reading = normalize_reading(cells[begin_idx])
    return headers, reading


_ROW_ACCEPTANCE_JS = """
(rowEl) => {
    if (!rowEl) return false;
    const norm = (s) => (s || '').replace(/\\s+/g, ' ').trim().toLowerCase();
    const submitWords = ['передати', 'передать', 'надіслати'];
    const acceptedPhrases = ['показания приняты', 'показання прийняті', 'показання прийнято'];

    const hasVisibleSubmit = Array.from(
        rowEl.querySelectorAll('button, a, input[type=button], input[type=submit]')
    ).some((el) => {
        if (el.offsetParent === null) return false;
        const t = norm(el.textContent) + ' ' + norm(el.value);
        return submitWords.some((w) => t.includes(w));
    });
    if (hasVisibleSubmit) return false;

    const cellText = Array.from(rowEl.querySelectorAll('td'))
        .map((td) => norm(td.textContent))
        .join(' ');
    return acceptedPhrases.some((p) => cellText.includes(p));
}
"""

def meter_row_accepted(row) -> bool:
    try:
        return bool(row.evaluate(_ROW_ACCEPTANCE_JS))
    except Exception:
        return False


def wait_for_row_acceptance(row, timeout_ms: int = 25_000) -> bool:
    deadline = time.monotonic() + timeout_ms / 1000.0
    while time.monotonic() < deadline:
        if meter_row_accepted(row):
            return True
        row.page.wait_for_timeout(400)
    return False


def find_meter_data_row(page):
    """Row that has both new-reading input and Передать/Передати button (Screenshot_3)."""
    row = page.locator("tr").filter(has=page.locator("input.inputCounterValues")).filter(
        has=page.locator("button.insertCounterValueBtn, button, a")
    )
    if row.count() > 0:
        return row.first
    row = page.locator("tr").filter(
        has=page.locator("input[type='text'], input[type='number']")
    ).filter(has=page.locator("button, a, input[type='button'], input[type='submit']"))
    if row.count() == 0:
        return page.locator("tr").first
    return row.first


def find_new_reading_field(row):
    branded = row.locator("input.inputCounterValues")
    if branded.count() > 0:
        return branded.first
    return row.locator("input[type='text'], input[type='number']").first


def find_submit_control(row):
    branded = row.locator("button.insertCounterValueBtn, .insertCounterValueBtn")
    if branded.count() > 0:
        return branded.first
    submit = row.locator("button, a, input[type='button'], input[type='submit']").filter(
        has_text=re.compile(r"передат|надісл", re.I)
    )
    if submit.count() > 0:
        return submit.first
    for label in ("Передати", "Передать", "Надіслати"):
        loc = row.get_by_role("button", name=label)
        if loc.count() > 0:
            return loc.first
    return row.locator("button, input[type='button'], input[type='submit']").last


def wait_submit_enabled(submit, timeout_ms: int = 15_000) -> bool:
    deadline = time.monotonic() + timeout_ms / 1000.0
    while time.monotonic() < deadline:
        try:
            if submit.is_enabled():
                return True
        except Exception:
            pass
        submit.page.wait_for_timeout(200)
    try:
        return submit.is_enabled()
    except Exception:
        return False


def fill_new_reading_field(field, reading: str) -> None:
    """Type reading so site JS enables «Передать» (fill() alone leaves button disabled)."""
    field.click()
    field.fill("")
    field.press_sequentially(reading, delay=35)
    field.press("Tab")
    for event in ("input", "change", "keyup", "blur"):
        field.dispatch_event(event)
    field.page.wait_for_timeout(400)


def click_submit_button(submit) -> None:
    if wait_submit_enabled(submit, timeout_ms=12_000):
        submit.click(timeout=10_000)
        return
    print(
        "SUBMIT_BUTTON_DISABLED: «Передать» still disabled after input; trying JS click",
        file=sys.stderr,
    )
    submit.evaluate(
        """el => {
            el.removeAttribute('disabled');
            el.scrollIntoView({block: 'center'});
            el.click();
            if (el.form && typeof el.form.requestSubmit === 'function') {
                try { el.form.requestSubmit(el); } catch (e) { /* ignore */ }
            }
        }"""
    )


def ajax_response_accepted(body: str, reading: str) -> bool:
    try:
        data = json.loads(body)
    except json.JSONDecodeError:
        return False
    if not isinstance(data, dict):
        return False
    if data.get("error") or data.get("errors"):
        return False
    pokaz = data.get("pokaz")
    if pokaz is None:
        return False
    try:
        return normalize_reading(str(pokaz)) == normalize_reading(reading)
    except ValueError:
        return False


def submit_reading_via_ajax(page, submit, reading: str, timeout_ms: int = 25_000) -> bool:
    """Site submits via POST counterfactajax; 200 + pokaz confirms acceptance."""
    try:
        with page.expect_response(
            lambda r: "counterfactajax" in r.url and r.request.method == "POST",
            timeout=timeout_ms,
        ) as resp_info:
            click_submit_button(submit)
        resp = resp_info.value
        if resp.status != 200:
            print(f"AJAX_STATUS: {resp.status}", file=sys.stderr)
            return False
        body = resp.text()
        if ajax_response_accepted(body, reading):
            print("AJAX_OK: показания приняты (counterfactajax)")
            return True
        print(f"AJAX_BODY_UNEXPECTED: {body[:400]}", file=sys.stderr)
        return False
    except Exception as exc:
        print(f"AJAX_WAIT: {exc}", file=sys.stderr)
        return False


def fill_reading_and_submit(page, reading: str) -> bool:
    """Fill new reading, click Передать, confirm via counterfactajax or row message."""
    page.wait_for_load_state("domcontentloaded", timeout=60_000)

    row = find_meter_data_row(page)
    row.wait_for(state="visible", timeout=30_000)

    field = find_new_reading_field(row)
    submit = find_submit_control(row)

    field.wait_for(state="visible", timeout=30_000)
    submit.wait_for(state="visible", timeout=30_000)

    field.scroll_into_view_if_needed()
    submit.scroll_into_view_if_needed()

    fill_new_reading_field(field, reading)
    if submit_reading_via_ajax(page, submit, reading):
        return True

    fill_new_reading_field(field, reading)
    if submit_reading_via_ajax(page, submit, reading):
        return True

    if wait_for_row_acceptance(row, timeout_ms=8_000):
        return True
    return meter_row_accepted(row)


def run_login_flow(profile_dir: Path, url: str, login_user: str, login_password: str) -> int:
    from playwright.sync_api import sync_playwright

    profile_dir.mkdir(parents=True, exist_ok=True)
    print(f"Browser profile: {profile_dir}")
    with sync_playwright() as p:
        context = launch_persistent_context(p, profile_dir, headless=False)
        page = context.pages[0] if context.pages else context.new_page()
        page.goto(url, wait_until="domcontentloaded", timeout=120_000)
        print(f"Opened: {url}")
        print(
            "Войдите в ЭТОМ окне Chromium (не в обычном Chrome). "
            "Отметьте «Запам'ятати мене», если есть. Затем Enter здесь."
        )
        input("After login, press Enter here to verify and save session… ")
        if not is_counter_page_ready(page):
            page.goto(url, wait_until="domcontentloaded", timeout=120_000)
        if is_counter_page_ready(page):
            print("SESSION_OK: страница показаний доступна, cookies сохранены.")
        elif try_auto_login(page, login_user, login_password):
            print("SESSION_OK: вход по RENI_LOGIN_* из reni_water.env.")
        else:
            print(
                "SESSION_NOT_READY: вход не подтверждён. Повторите -login или добавьте "
                "RENI_LOGIN_USER / RENI_LOGIN_PASSWORD в reni_water.env (см. reni_water.env.example).",
                file=sys.stderr,
            )
            context.close()
            return 4
        context.close()
    print("Session saved.")
    return 0


def run_check_session_flow(
    profile_dir: Path,
    url: str,
    login_user: str,
    login_password: str,
    login_url: str,
) -> int:
    from playwright.sync_api import sync_playwright

    if not profile_dir.is_dir():
        print("SESSION_MISSING: profile not found. Run --login once.", file=sys.stderr)
        return 2

    with sync_playwright() as p:
        context = launch_persistent_context(p, profile_dir, headless=True)
        page = context.pages[0] if context.pages else context.new_page()
        ok = ensure_logged_in(page, url, login_user, login_password, login_url)
        context.close()

    if ok:
        print("SESSION_OK: автоматическая передача возможна без ручного входа.")
        return 0
    print("SESSION_EXPIRED: нужен -login или логин/пароль в reni_water.env.", file=sys.stderr)
    return 3


def run_submit_flow(
    profile_dir: Path,
    url: str,
    reading_override: str,
    use_beginning_column: bool,
    screenshot_dir: Path,
    pending_ack_path: Path,
    login_user: str,
    login_password: str,
    login_url: str,
) -> int:
    from playwright.sync_api import sync_playwright

    if not profile_dir.is_dir():
        print(
            "ERROR: Browser profile not found. Run once:\n"
            f"  python {Path(__file__).name} --login",
            file=sys.stderr,
        )
        return 2

    screenshot_dir.mkdir(parents=True, exist_ok=True)

    with sync_playwright() as p:
        context = launch_persistent_context(p, profile_dir, headless=True)
        page = context.pages[0] if context.pages else context.new_page()

        if not ensure_logged_in(page, url, login_user, login_password, login_url):
            html = page.content()
            current_url = page.url
            if not looks_like_login_page(html, current_url) and not is_counter_page_ready(page):
                print(f"AUTH_REQUIRED: unexpected page {current_url}", file=sys.stderr)
            else:
                print("AUTH_REQUIRED: not logged in. Run: run_submit.ps1 -login", file=sys.stderr)
                print(
                    "  Or set RENI_LOGIN_USER / RENI_LOGIN_PASSWORD in reni_water.env for auto-login.",
                    file=sys.stderr,
                )
            auth_shot = screenshot_path(screenshot_dir, "auth_required")
            page.screenshot(path=str(auth_shot), full_page=True)
            print(f"Screenshot: {auth_shot}")
            context.close()
            write_pending_ack(pending_ack_path, "?", auth_shot, auth_required=True)
            return 3

        if use_beginning_column and not reading_override:
            _, reading = extract_headers_and_reading(page)
            print(f"READING_FROM_BEGINNING_COLUMN: {reading}")
        else:
            reading = reading_override or "255"
            print(f"READING_OVERRIDE: {reading}")

        accepted = fill_reading_and_submit(page, reading)

        out = screenshot_path(screenshot_dir, "after_submit")
        page.screenshot(path=str(out), full_page=True)
        print(f"Screenshot: {out}")

        context.close()

    if not accepted:
        print(
            "SUBMIT_NOT_ACCEPTED: на сайте нет подтверждения «Показания приняты» "
            "(кнопка Передать могла не сработать).",
            file=sys.stderr,
        )
        write_pending_ack(pending_ack_path, reading, out, auth_required=False)
        return 11

    print("SUBMIT_ACCEPTED: показания приняты")
    print(f"OK: submitted reading={reading}")
    write_pending_ack(pending_ack_path, reading, out)
    return 0


def main() -> int:
    configure_stdio_utf8()
    parser = argparse.ArgumentParser(description="Reni vodokanal meter reading")
    parser.add_argument(
        "--login", "-login", action="store_true", help="Save login session (opens Chromium)"
    )
    parser.add_argument(
        "--ack", "-ack", action="store_true", help="Confirm notification (stop hourly reminders)"
    )
    parser.add_argument(
        "--notify", "-notify", action="store_true", help="Emit reminder if pending ack (hourly task)"
    )
    parser.add_argument(
        "--check-session",
        "-check-session",
        action="store_true",
        help="Verify saved login (no submit)",
    )
    args = parser.parse_args()
    settings = cfg()

    pending_path = Path(settings["pending_ack_path"])

    try:
        if args.ack:
            return clear_pending_ack(pending_path)
        if args.notify:
            return notify_pending(pending_path)

        reading_override = str(settings["reading_override"])
        if reading_override and not reading_override.isdigit() and "." not in reading_override:
            print("ERROR: RENI_METER_READING must be numeric.", file=sys.stderr)
            return 1

        profile = Path(settings["profile_dir"])
        url = str(settings["url"])
        login_user = str(settings["login_user"])
        login_password = str(settings["login_password"])
        login_url = str(settings["login_url"])

        if args.login:
            return run_login_flow(profile, url, login_user, login_password)

        if args.check_session:
            return run_check_session_flow(
                profile, url, login_user, login_password, login_url
            )

        return run_submit_flow(
            profile,
            url,
            reading_override,
            bool(settings["use_beginning_column"]),
            Path(settings["screenshot_dir"]),
            pending_path,
            login_user,
            login_password,
            login_url,
        )
    except Exception as ex:
        err_dir = Path(settings["screenshot_dir"])
        err_dir.mkdir(parents=True, exist_ok=True)
        err_path = err_dir / f"reni_water_error_{datetime.now():%Y%m%d_%H%M%S}.txt"
        err_path.write_text(str(ex), encoding="utf-8")
        print(f"ERROR: {ex}", file=sys.stderr)
        print(f"Details: {err_path}", file=sys.stderr)
        return 10


if __name__ == "__main__":
    raise SystemExit(main())
