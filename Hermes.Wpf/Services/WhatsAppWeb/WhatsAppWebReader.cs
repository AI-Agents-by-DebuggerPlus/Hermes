using System.IO;
using System.Text.Json;
using System.Windows;
using Hermes.Wpf.Views;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Wpf.Services.WhatsAppWeb;

public sealed class WhatsAppWebReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WhatsAppWebWindow _window;
    private bool _initialized;

    public WhatsAppWebReader(WhatsAppWebWindow window) => _window = window;

    public bool IsInitialized => _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _window.Dispatcher.InvokeAsync(async () =>
        {
            var profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesWpf",
                "WhatsAppWebViewProfile");
            Directory.CreateDirectory(profileDir);

            var env = await CoreWebView2Environment.CreateAsync(null, profileDir);
            await _window.WebView.EnsureCoreWebView2Async(env);
            _window.WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _window.WebView.Source = new Uri("https://web.whatsapp.com");
        }).Task.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        _initialized = true;
    }

    public Task<T?> ExecuteJsonAsync<T>(string script, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _window.Dispatcher
            .InvokeAsync(() => ExecuteJsonCoreAsync<T>(script))
            .Task.Unwrap();
    }

    public Task<string?> ExecuteRawAsync(string script, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _window.Dispatcher
            .InvokeAsync(() => ExecuteRawCoreAsync(script))
            .Task.Unwrap();
    }

    internal Task<WhatsAppWebSimpleResult?> SendTextViaCdpAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _window.Dispatcher
            .InvokeAsync(() => SendTextViaCdpCoreAsync(text, cancellationToken))
            .Task.Unwrap();
    }

    private async Task<WhatsAppWebSimpleResult?> SendTextViaCdpCoreAsync(string text, CancellationToken cancellationToken)
    {
        var webView = _window.WebView.CoreWebView2;
        if (webView is null)
        {
            return new WhatsAppWebSimpleResult { Status = "no_webview" };
        }

        var focusRaw = await webView.ExecuteScriptAsync(WhatsAppWebScriptBuilder.BuildFocusComposeScript())
            .ConfigureAwait(true);
        var focus = DeserializeScriptResult<WhatsAppWebSimpleResult>(focusRaw);
        if (focus?.Status is not "focused")
        {
            return focus ?? new WhatsAppWebSimpleResult
            {
                Status = "focus_failed",
                Detail = focusRaw ?? string.Empty,
            };
        }

        await Task.Delay(250, cancellationToken).ConfigureAwait(true);

        await DispatchSelectAllAsync(webView).ConfigureAwait(true);
        await Task.Delay(80, cancellationToken).ConfigureAwait(true);

        var insertPayload = JsonSerializer.Serialize(new { text });
        await webView.CallDevToolsProtocolMethodAsync("Input.insertText", insertPayload).ConfigureAwait(true);
        await Task.Delay(450, cancellationToken).ConfigureAwait(true);

        var afterInsertRaw = await webView.ExecuteScriptAsync(
            WhatsAppWebScriptBuilder.BuildFocusComposeScript()).ConfigureAwait(true);
        var afterInsert = DeserializeScriptResult<WhatsAppWebSimpleResult>(afterInsertRaw);
        if (string.IsNullOrWhiteSpace(afterInsert?.Detail))
        {
            return new WhatsAppWebSimpleResult
            {
                Status = "compose_empty",
                Detail = "cdp_insert_empty",
                Remaining = afterInsert?.Detail ?? string.Empty,
                Method = "cdp",
            };
        }

        await DispatchEnterAsync(webView).ConfigureAwait(true);
        await Task.Delay(500, cancellationToken).ConfigureAwait(true);

        var verifyRaw = await webView.ExecuteScriptAsync(WhatsAppWebScriptBuilder.BuildComposeVerifyScript())
            .ConfigureAwait(true);
        var verify = DeserializeScriptResult<WhatsAppWebSimpleResult>(verifyRaw)
                     ?? new WhatsAppWebSimpleResult { Status = "verify_null", Detail = verifyRaw ?? string.Empty };
        verify.Method = "cdp";
        return verify;
    }

    private static async Task DispatchSelectAllAsync(CoreWebView2 webView)
    {
        const string keyDown =
            """{"type":"keyDown","key":"a","code":"KeyA","windowsVirtualKeyCode":65,"nativeVirtualKeyCode":65,"modifiers":2}""";
        const string keyUp =
            """{"type":"keyUp","key":"a","code":"KeyA","windowsVirtualKeyCode":65,"nativeVirtualKeyCode":65,"modifiers":2}""";
        await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyDown).ConfigureAwait(true);
        await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyUp).ConfigureAwait(true);
    }

    private static async Task DispatchEnterAsync(CoreWebView2 webView)
    {
        const string keyDown =
            """{"type":"rawKeyDown","key":"Enter","code":"Enter","windowsVirtualKeyCode":13,"nativeVirtualKeyCode":13}""";
        const string charEvent =
            """{"type":"char","key":"Enter","code":"Enter","text":"\r","windowsVirtualKeyCode":13,"nativeVirtualKeyCode":13}""";
        const string keyUp =
            """{"type":"keyUp","key":"Enter","code":"Enter","windowsVirtualKeyCode":13,"nativeVirtualKeyCode":13}""";
        await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyDown).ConfigureAwait(true);
        await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", charEvent).ConfigureAwait(true);
        await webView.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", keyUp).ConfigureAwait(true);
    }

    private async Task<string?> ExecuteRawCoreAsync(string script)
    {
        if (_window.WebView.CoreWebView2 is null)
        {
            return null;
        }

        return await _window.WebView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
    }

    private async Task<T?> ExecuteJsonCoreAsync<T>(string script)
    {
        var raw = await ExecuteRawCoreAsync(script).ConfigureAwait(true);
        return DeserializeScriptResult<T>(raw);
    }

    internal static T? DeserializeScriptResult<T>(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null")
        {
            return default;
        }

        var payload = raw;
        if (payload.StartsWith('"') && payload.EndsWith('"'))
        {
            try
            {
                var unwrapped = JsonSerializer.Deserialize<string>(payload);
                if (!string.IsNullOrWhiteSpace(unwrapped))
                {
                    payload = unwrapped;
                }
            }
            catch (JsonException)
            {
                // keep original payload
            }
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public void ShowWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
        });
    }
}
