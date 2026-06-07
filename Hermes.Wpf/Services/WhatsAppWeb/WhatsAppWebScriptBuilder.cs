using System.Text.Json;

namespace Hermes.Wpf.Services.WhatsAppWeb;

internal static class WhatsAppWebScriptBuilder
{
    private const string MatchHelper = """
        function matchesContact(title, contactName) {
          if (!title || !contactName) return false;
          var t = String(title).trim().toLowerCase();
          var c = String(contactName).trim().toLowerCase();
          if (t === c) return true;
          if (t.startsWith(c)) return true;
          return t.indexOf(c) >= 0;
        }
        function isQrVisible() {
          var canvas = document.querySelector('canvas');
          if (!canvas) return false;
          var r = canvas.getBoundingClientRect();
          return r.width > 100 && r.height > 100;
        }
        function matchesMarkerPrefix(text, marker) {
          if (!marker) return true;
          var t = String(text).trim().toLowerCase();
          var m = String(marker).trim().toLowerCase();
          return t.startsWith(m) || t.indexOf(m) >= 0;
        }
        function hasMessageStructure(box) {
          return !!(box.querySelector('[data-testid="msg-text"]') || box.querySelector('.copyable-text'));
        }
        function extractMessageText(box) {
          var textEl = box.querySelector('[data-testid="msg-text"] span')
            || box.querySelector('[data-testid="msg-text"]')
            || box.querySelector('span[data-lexical-text="true"]')
            || box.querySelector('.copyable-text span.selectable-text')
            || box.querySelector('.copyable-text span')
            || box.querySelector('.selectable-text');
          if (textEl) {
            var fromEl = (textEl.innerText || textEl.textContent || '').trim();
            if (fromEl.length > 0) return fromEl;
          }
          var copyable = box.querySelector('.copyable-text');
          if (copyable) {
            var fromCopyable = (copyable.innerText || copyable.textContent || '').trim();
            if (fromCopyable.length > 0) return fromCopyable;
          }
          var fallback = (box.innerText || box.textContent || '').trim();
          if (fallback.length > 0 && fallback.length < 8000) return fallback;
          return '';
        }
        function messageIdFor(box, text) {
          var id = box.getAttribute('data-id') || '';
          if (!id) {
            var parent = box.closest('[data-id]');
            if (parent) id = parent.getAttribute('data-id') || '';
          }
          if (!id) {
            var meta = box.getAttribute('data-pre-plain-text')
              || (box.querySelector('[data-pre-plain-text]') && box.querySelector('[data-pre-plain-text]').getAttribute('data-pre-plain-text'))
              || '';
            id = 'hash:' + meta + '|' + text;
          }
          return id;
        }
        function scrollChatToBottom() {
          var main = document.querySelector('#main');
          if (!main) return false;
          var panel = main.querySelector('[data-testid="conversation-panel-messages"]')
            || main.querySelector('div[tabindex="-1"]')
            || main;
          panel.scrollTop = panel.scrollHeight;
          panel.dispatchEvent(new Event('scroll', { bubbles: true }));
          return true;
        }
        function findComposeInput() {
          var main = document.querySelector('#main');
          if (!main) return null;
          var candidates = [
            main.querySelector('[data-testid="conversation-compose-box-input"]'),
            main.querySelector('footer div[contenteditable="true"][role="textbox"]'),
            main.querySelector('footer div[contenteditable="true"]'),
            main.querySelector('div[contenteditable="true"][data-tab="10"]'),
            main.querySelector('div[data-lexical-editor="true"]')
          ];
          for (var i = 0; i < candidates.length; i++) {
            if (candidates[i]) return candidates[i];
          }
          return null;
        }
        function isIncomingMessage(box) {
          var node = box;
          for (var i = 0; i < 12 && node; i++) {
            if (node.classList) {
              if (node.classList.contains('message-out')) return false;
              if (node.classList.contains('message-in')) return true;
            }
            node = node.parentElement;
          }
          return true;
        }
        function sleep(ms) {
          return new Promise(function(resolve) { setTimeout(resolve, ms); });
        }
        function composeText(input) {
          return (input.innerText || input.textContent || '').trim();
        }
        function isComposeEmpty(input) {
          return composeText(input).length === 0;
        }
        function setComposeText(input, messageText) {
          input.focus();
          try {
            var sel = window.getSelection();
            var range = document.createRange();
            range.selectNodeContents(input);
            sel.removeAllRanges();
            sel.addRange(range);
            document.execCommand('delete', false, null);
          } catch (e) {}
          try {
            var dt = new DataTransfer();
            dt.setData('text/plain', messageText);
            input.dispatchEvent(new ClipboardEvent('paste', {
              bubbles: true,
              cancelable: true,
              clipboardData: dt
            }));
          } catch (e2) {}
          if (composeText(input).length === 0) {
            var paragraph = input.querySelector('p[dir]') || input.querySelector('p');
            if (paragraph) {
              paragraph.textContent = messageText;
            } else {
              document.execCommand('insertText', false, messageText);
            }
          }
          input.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, inputType: 'insertText', data: messageText }));
          input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: messageText }));
          input.dispatchEvent(new Event('change', { bubbles: true }));
          return composeText(input).length;
        }
        function findSendButton() {
          var selectors = [
            'button[data-testid="compose-btn-send"]',
            'button[aria-label="Send"]',
            'button[aria-label="Отправить"]',
            '#main button[aria-label*="Send"]',
            '#main button[aria-label*="end"]',
            '#main button[aria-label*="тправ"]'
          ];
          for (var i = 0; i < selectors.length; i++) {
            var btn = document.querySelector(selectors[i]);
            if (btn) return btn;
          }
          var icon = document.querySelector('#main span[data-icon="send"]')
            || document.querySelector('span[data-icon="send"]');
          if (icon) return icon.closest('button') || icon.parentElement;
          return null;
        }
        function isSendEnabled(btn) {
          if (!btn) return false;
          if (btn.disabled) return false;
          if (btn.getAttribute('aria-disabled') === 'true') return false;
          return true;
        }
        function clickSendButton() {
          var btn = findSendButton();
          if (!isSendEnabled(btn)) return false;
          btn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
          btn.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
          btn.click();
          return true;
        }
        function pressEnter(input) {
          var opts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true };
          input.dispatchEvent(new KeyboardEvent('keydown', opts));
          input.dispatchEvent(new KeyboardEvent('keypress', opts));
          input.dispatchEvent(new KeyboardEvent('keyup', opts));
        }
        """;

    public static string BuildFocusComposeScript() =>
        MatchHelper + """
        (function() {
          if (isQrVisible()) return { status: 'qr' };
          var input = findComposeInput();
          if (!input) return { status: 'no_compose' };
          input.focus();
          input.click();
          try {
            var sel = window.getSelection();
            var range = document.createRange();
            range.selectNodeContents(input);
            sel.removeAllRanges();
            sel.addRange(range);
          } catch (e) {}
          return { status: 'focused', detail: composeText(input) };
        })();
        """;

    public static string BuildComposeVerifyScript() =>
        MatchHelper + """
        (function() {
          var input = findComposeInput();
          var text = input ? composeText(input) : '';
          var btn = findSendButton();
          return {
            status: text.length === 0 ? 'sent' : 'send_failed',
            remaining: text,
            detail: btn ? (isSendEnabled(btn) ? 'send_enabled' : 'send_disabled') : 'no_send_btn',
            method: 'cdp'
          };
        })();
        """;

    public static string BuildComposeMessageScript(string messageText)
    {
        var text = JsonSerializer.Serialize(messageText);
        return MatchHelper + $$"""
        (function() {
          var messageText = {{text}};
          if (isQrVisible()) return { status: 'qr' };
          var header = document.querySelector('#main header') || document.querySelector('[data-testid="conversation-header"]');
          if (!header) return { status: 'chat_not_open' };
          var input = findComposeInput();
          if (!input) return { status: 'no_compose' };
          var len = setComposeText(input, messageText);
          if (len === 0) {
            return { status: 'compose_empty', detail: 'text_not_in_input', remaining: composeText(input) };
          }
          return { status: 'composed', detail: String(len), remaining: composeText(input) };
        })();
        """;
    }

    public static string BuildSubmitComposeScript() =>
        MatchHelper + """
        (function() {
          if (isQrVisible()) return { status: 'qr' };
          var input = findComposeInput();
          if (!input) return { status: 'no_compose' };
          var before = composeText(input);
          if (before.length === 0) return { status: 'compose_empty', detail: 'nothing_to_send' };
          if (clickSendButton()) {
            if (isComposeEmpty(input)) {
              scrollChatToBottom();
              return { status: 'sent', method: 'button', remaining: '' };
            }
          }
          pressEnter(input);
          if (isComposeEmpty(input)) {
            scrollChatToBottom();
            return { status: 'sent', method: 'enter', remaining: '' };
          }
          return {
            status: 'send_failed',
            method: 'none',
            remaining: composeText(input),
            detail: 'compose_not_cleared'
          };
        })();
        """;

    public static string BuildSendMessageScript(string messageText)
    {
        var text = JsonSerializer.Serialize(messageText);
        return MatchHelper + $$"""
        (function() {
          var messageText = {{text}};
          if (isQrVisible()) return { status: 'qr' };
          var header = document.querySelector('#main header') || document.querySelector('[data-testid="conversation-header"]');
          if (!header) return { status: 'chat_not_open' };
          var input = findComposeInput();
          if (!input) return { status: 'no_compose' };
          var len = setComposeText(input, messageText);
          if (len === 0) return { status: 'compose_empty', detail: 'text_not_in_input' };
          for (var attempt = 0; attempt < 4; attempt++) {
            if (clickSendButton() && isComposeEmpty(input)) {
              scrollChatToBottom();
              return { status: 'sent', method: 'button', attempt: attempt + 1 };
            }
            pressEnter(input);
            if (isComposeEmpty(input)) {
              scrollChatToBottom();
              return { status: 'sent', method: 'enter', attempt: attempt + 1 };
            }
          }
          return {
            status: 'send_failed',
            remaining: composeText(input),
            detail: 'compose_not_cleared'
          };
        })();
        """;
    }

    public static string BuildScrollToBottomScript() =>
        MatchHelper + """
        (function() {
          scrollChatToBottom();
          return { status: 'ok' };
        })();
        """;

    public static string BuildIsReadyScript() =>
        MatchHelper + """
        (function() {
          if (isQrVisible()) return { status: 'qr' };
          var pane = document.querySelector('#pane-side');
          if (!pane) return { status: 'loading' };
          var titles = pane.querySelectorAll('span[title]');
          if (!titles || titles.length === 0) return { status: 'loading' };
          return { status: 'ready', chatCount: titles.length };
        })();
        """;

    public static string BuildOpenChatScript(string contactName)
    {
        var contact = JsonSerializer.Serialize(contactName);
        return MatchHelper + $$"""
        (function() {
          var contactName = {{contact}};
          if (isQrVisible()) return { status: 'qr' };
          var pane = document.querySelector('#pane-side');
          if (!pane) return { status: 'loading' };
          var header = document.querySelector('#main header') || document.querySelector('[data-testid="conversation-header"]');
          if (header) {
            var ht = header.innerText || header.textContent || '';
            if (matchesContact(ht, contactName)) return { status: 'already_open' };
          }
          function clickChat(el) {
            var row = el.closest('[data-testid="cell-frame-container"]') || el.closest('[role="listitem"]') || el.parentElement;
            if (!row) return false;
            row.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
            row.click();
            return true;
          }
          var spans = pane.querySelectorAll('span[title]');
          for (var i = 0; i < spans.length; i++) {
            var title = spans[i].getAttribute('title') || '';
            if (matchesContact(title, contactName) && clickChat(spans[i])) {
              return { status: 'opened', matched: title };
            }
          }
          var search = document.querySelector('#side div[contenteditable="true"]');
          if (search) {
            search.focus();
            document.execCommand('selectAll', false, null);
            document.execCommand('insertText', false, contactName);
          }
          return { status: 'not_found' };
        })();
        """;
    }

    /// <param name="forBaseline">When true, collect visible message ids (ignore marker) without forwarding.</param>
    public static string BuildPollScript(string contactName, string textMarker, bool forBaseline = false)
    {
        var contact = JsonSerializer.Serialize(contactName);
        var marker = JsonSerializer.Serialize(textMarker ?? string.Empty);
        var baselineFlag = forBaseline ? "true" : "false";
        return MatchHelper + $$"""
        (function() {
          var contactName = {{contact}};
          var textMarker = {{marker}};
          var forBaseline = {{baselineFlag}};
          if (isQrVisible()) return { status: 'qr', messages: [] };
          var pane = document.querySelector('#pane-side');
          if (!pane) return { status: 'loading', messages: [] };

          function findAndOpenChat() {
            var spans = pane.querySelectorAll('span[title]');
            for (var i = 0; i < spans.length; i++) {
              var title = spans[i].getAttribute('title') || '';
              if (!matchesContact(title, contactName)) continue;
              var row = spans[i].closest('[data-testid="cell-frame-container"]') || spans[i].closest('[role="listitem"]') || spans[i].parentElement;
              if (row) { row.click(); return true; }
            }
            return false;
          }

          var header = document.querySelector('#main header') || document.querySelector('[data-testid="conversation-header"]');
          var headerText = header ? (header.innerText || header.textContent || '') : '';
          if (!matchesContact(headerText, contactName)) {
            if (!findAndOpenChat()) return { status: 'chat_not_found', messages: [] };
            return { status: 'opening', messages: [] };
          }

          scrollChatToBottom();
          var containers = document.querySelectorAll('#main [data-testid="msg-container"]');
          var seen = new Set();
          var messages = [];
          var tailStart = containers.length > 50 ? containers.length - 50 : 0;
          for (var j = tailStart; j < containers.length; j++) {
            var box = containers[j];
            var structured = hasMessageStructure(box);
            var text = extractMessageText(box);
            if (!text) continue;
            if (!forBaseline && !matchesMarkerPrefix(text, textMarker)) continue;
            var id = messageIdFor(box, text);
            if (seen.has(id)) continue;
            seen.add(id);
            messages.push({
              id: id,
              text: text,
              hasMessageStructure: structured,
              isIncoming: isIncomingMessage(box)
            });
          }
          return { status: 'ok', messages: messages, domCount: containers.length };
        })();
        """;
    }
}
