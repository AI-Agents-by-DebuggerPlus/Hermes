/**
 * Hermes Image Receiver — Frontend Gallery
 */
(function () {
  'use strict';

  const galleries = {};

  /* ── Fullscreen overlay (singleton) ───────────────── */
  let _fsEl    = null;
  let _fsTimer = null;

  function getFsEl() {
    if (_fsEl) return _fsEl;
    _fsEl = document.createElement('div');
    _fsEl.id        = 'hir-fs';
    _fsEl.innerHTML = `
      <img  id="hir-fs-img"  src="" alt="" />
      <div  id="hir-fs-bar">
        <span id="hir-fs-meta"></span>
        <button id="hir-fs-close" type="button">✕</button>
      </div>
      <div id="hir-fs-progress"></div>`;
    document.body.appendChild(_fsEl);
    _fsEl.querySelector('#hir-fs-close').addEventListener('click', closeFsOverlay);
    _fsEl.querySelector('#hir-fs-img').addEventListener('click',   closeFsOverlay);
    document.addEventListener('keydown', e => { if (e.key === 'Escape') closeFsOverlay(); });
    return _fsEl;
  }

  function openFsOverlay(item) {
    const el   = getFsEl();
    el.querySelector('#hir-fs-img').src = item.url;
    const m = item.meta || {};
    el.querySelector('#hir-fs-meta').textContent = [
      item.filename,
      m.width ? `${m.width}×${m.height}` : '',
      formatTime(item.created_at),
    ].filter(Boolean).join(' · ');

    const prog = el.querySelector('#hir-fs-progress');
    prog.style.animation = 'none';
    void prog.offsetWidth;
    prog.style.animation = '';

    el.classList.add('hir-fs--visible');
    if (_fsTimer) clearTimeout(_fsTimer);
    _fsTimer = setTimeout(closeFsOverlay, 5000);
  }

  function closeFsOverlay() {
    _fsEl?.classList.remove('hir-fs--visible');
    if (_fsTimer) { clearTimeout(_fsTimer); _fsTimer = null; }
  }

  /* ── Init ──────────────────────────────────────────── */
  function initAll() {
    document.querySelectorAll('.hir-gallery-wrap').forEach(initGallery);
  }

  function initGallery(wrap) {
    const uid = wrap.dataset.uid;
    if (galleries[uid]) return;

    let cfg = {};
    if (wrap.dataset.hirConfig) {
      try { cfg = JSON.parse(wrap.dataset.hirConfig); } catch (e) {}
    } else {
      cfg = window['hirConfig_' + uid.replace(/-/g, '_')] || {};
    }

    const state = {
      uid, wrap,
      ws: null, sse: null, pollTimer: null,
      connected: false, sseConnected: false,
      images: [], lightboxIdx: -1,
      channel:     cfg.channel     || wrap.dataset.channel     || '',
      max:         parseInt(cfg.max || wrap.dataset.max || 20),
      autoconnect: (cfg.autoconnect !== undefined ? cfg.autoconnect : wrap.dataset.autoconnect) !== 'false',
      wsPort:      parseInt(cfg.wsPort || wrap.dataset.wsPort || 8765),
      wsOnly:      cfg.wsOnly === true || cfg.wsOnly === 1,
      useSse:      cfg.useSse !== false && cfg.useSse !== 0,
      sseUrl:      cfg.sseUrl || '',
      wsHost:      (cfg.wsHost || '').trim()
        || ((cfg.wsOnly === true || cfg.wsOnly === 1) ? '' : location.hostname),
      restUrl:     cfg.restUrl || '/wp-json/hermes/v1/',
      token:       cfg.token   || '',
      layout:      cfg.layout  || 'grid',
      lastId:      0,
      fsLive:      false,
    };

    galleries[uid] = state;
    bindControls(state);

    if (state.useSse && !state.wsOnly) {
      setStatus(state, 'Режим SSE: Hermes.Wpf → REST, галерея ← Server-Sent Events.');
      if (state.autoconnect) connectSSE(state);
      fetchImages(state);
      return;
    }

    if (state.wsOnly) {
      setStatus(state, 'Режим WebSocket-only. REST-polling отключён.');
      if (state.autoconnect) connectWS(state);
    } else {
      if (state.autoconnect) connectWS(state);
      fetchImages(state);
    }
  }

  /* ── SSE (как WPFtoWordPressSSE: браузер ← WordPress) ─ */
  function connectSSE(state) {
    if (!state.useSse) return;
    disconnectSSE(state);

    let url = state.sseUrl || (state.restUrl.replace(/\/?$/, '/') + 'stream');
    const sep = url.includes('?') ? '&' : '?';
    url += sep + 'last_id=' + state.lastId;
    if (state.channel) url += '&channel=' + encodeURIComponent(state.channel);

    setLed(state, 'connecting');
    setStatus(state, 'Подключение SSE…');

    let source;
    try { source = new EventSource(url); }
    catch (e) {
      setLed(state, 'error');
      setStatus(state, 'Ошибка SSE: ' + e.message);
      if (!state.wsOnly) startPolling(state);
      return;
    }

    state.sse = source;

    source.onopen = () => {
      state.sseConnected = true;
      state.connected = true;
      setLed(state, 'connected');
      setStatus(state, 'SSE: ожидание снимков от Hermes.Wpf…');
      updateConnectBtn(state, true);
      stopPolling(state);
    };

    source.onmessage = evt => {
      try { handleSseMessage(state, JSON.parse(evt.data)); } catch (e) {}
    };

    source.onerror = () => {
      if (state.sseConnected) {
        setLed(state, 'connecting');
        setStatus(state, 'SSE: переподключение…');
      } else if (!state.wsOnly) {
        setLed(state, 'error');
        setStatus(state, 'SSE недоступен. Используется REST-опрос.');
        startPolling(state);
      }
    };
  }

  function disconnectSSE(state) {
    if (state.sse) {
      state.sse.close();
      state.sse = null;
    }
    state.sseConnected = false;
    if (!state.ws || state.ws.readyState > 1) {
      state.connected = false;
    }
  }

  function handleSseMessage(state, data) {
    if (data.type && data.type !== 'image') return;
    const imageUrl = data.image_url || data.url;
    if (!imageUrl) return;
    if (state.channel && data.channel && data.channel !== state.channel) return;
    prependImage(state, {
      id:         data.id || Date.now(),
      url:        imageUrl,
      channel:    data.channel || 'default',
      filename:   data.filename || 'image.png',
      created_at: data.created_at || new Date().toISOString(),
      meta:       data.meta || {},
    });
  }

  /* ── WebSocket ─────────────────────────────────────── */
  function connectWS(state) {
    if (state.ws && state.ws.readyState <= 1) return;

    if (state.wsOnly && !state.wsHost) {
      setLed(state, 'error');
      setStatus(state, 'Укажите IP ПК с Hermes.Wpf: WordPress → Hermes Receiver → «IP ПК с Hermes» или ws_host в шорткоде.');
      return;
    }

    // На HTTPS без wsOnly — переходим на REST polling и сразу предупреждаем
    if (location.protocol === 'https:' && !state.wsOnly) {
      setLed(state, 'connected');
      setStatus(state, 'HTTPS: изображения через REST API (опрос каждые 3 с). Hermes.Wpf отправляет снимки на сервер.');
      updateConnectBtn(state, true);
      startPolling(state);
      return;
    }

    // На HTTPS с wsOnly — пробуем WSS; если браузер заблокирует, покажем ошибку (без fallback)
    const protocol = location.protocol === 'https:' ? 'wss' : 'ws';
    const wsUrl    = `${protocol}://${state.wsHost}:${state.wsPort}`;

    setLed(state, 'connecting');
    setStatus(state, `Подключение к ${wsUrl}…`);

    let ws;
    try { ws = new WebSocket(wsUrl); }
    catch (e) {
      setLed(state, 'error');
      const msg = state.wsOnly
        ? `Ошибка WebSocket: ${e.message}. REST-polling отключён — изображения не будут получены.`
        : `Ошибка WebSocket: ${e.message}. Используется REST-опрос.`;
      setStatus(state, msg);
      if (!state.wsOnly) startPolling(state);
      return;
    }

    state.ws = ws;

    ws.onopen = () => {
      state.connected = true;
      setLed(state, 'connected');
      setStatus(state, `Подключено к Hermes.Wpf (${wsUrl})`);
      updateConnectBtn(state, true);
      if (!state.wsOnly) stopPolling(state);
      ws.send(JSON.stringify({ type: 'subscribe', channel: state.channel, token: state.token }));
    };

    ws.onmessage = evt => { try { handleMessage(state, JSON.parse(evt.data)); } catch (e) {} };

    ws.onclose = evt => {
      state.connected = false;
      const reconnectMsg = `Отключено (${evt.code}). Переподключение через 5 с…`;
      const wsOnlyMsg    = `Отключено (${evt.code}). REST-polling отключён. Переподключение через 5 с…`;
      setLed(state, '');
      setStatus(state, state.wsOnly ? wsOnlyMsg : reconnectMsg);
      updateConnectBtn(state, false);
      setTimeout(() => { if (galleries[state.uid] && !state.connected) connectWS(state); }, 5000);
      // В wsOnly-режиме polling НЕ запускаем даже при обрыве соединения
      if (!state.wsOnly) startPolling(state);
    };

    ws.onerror = () => {
      setLed(state, 'error');
      const msg = state.wsOnly
        ? 'Ошибка WebSocket. REST-polling отключён — ожидаем переподключения.'
        : 'Ошибка WebSocket. Используется REST-опрос.';
      setStatus(state, msg);
      if (!state.wsOnly) startPolling(state);
    };
  }

  function disconnectWS(state) {
    stopPolling(state);
    disconnectSSE(state);
    if (state.ws) { state.ws.onclose = null; state.ws.close(); state.ws = null; }
    state.connected = false;
    setLed(state, '');
    setStatus(state, 'Отключено.');
    updateConnectBtn(state, false);
  }

  /* ── Message ───────────────────────────────────────── */
  function handleMessage(state, msg) {
    if (msg.type !== 'image' && !msg.data) return;
    if (state.channel && msg.channel && msg.channel !== state.channel) return;
    const url = msg.url || (msg.data ? 'data:' + (msg.mime || 'image/png') + ';base64,' + msg.data : null);
    if (!url) return;
    prependImage(state, {
      id: msg.id || Date.now(), url,
      channel:    msg.channel    || 'default',
      filename:   msg.filename   || 'image.png',
      created_at: msg.created_at || new Date().toISOString(),
      meta:       msg.meta       || {},
    });
  }

  /* ── REST polling ──────────────────────────────────── */
  function startPolling(state) {
    if (state.wsOnly || state.pollTimer) return;   // <-- guard
    state.pollTimer = setInterval(() => fetchImages(state), 3000);
  }
  function stopPolling(state) {
    if (state.pollTimer) { clearInterval(state.pollTimer); state.pollTimer = null; }
  }
  function fetchImages(state) {
    if (state.wsOnly) return;                      // <-- guard
    let url = state.restUrl + 'images?limit=20';
    if (state.channel) url += '&channel=' + encodeURIComponent(state.channel);
    if (state.lastId)  url += '&since='   + state.lastId;
    fetch(url)
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (!data?.images) return;
        [...data.images].reverse().forEach(img => prependImage(state, img, false));
        if (data.images.length) state.lastId = Math.max(state.lastId, ...data.images.map(i => i.id));
      }).catch(() => {});
  }

  /* ── Render ────────────────────────────────────────── */
  function prependImage(state, item, flash = true) {
    if (state.images.find(i => i.id === item.id)) return;
    state.images.unshift(item);
    if (state.images.length > state.max) state.images = state.images.slice(0, state.max);
    if (typeof item.id === 'number') state.lastId = Math.max(state.lastId, item.id);
    renderGallery(state, flash ? item.id : null);
    if (flash && state.fsLive) openFsOverlay(item);
  }

  function renderGallery(state, flashId) {
    const container = state.wrap.querySelector(`.hir-images[data-uid="${state.uid}"]`);
    if (!container) return;
    container.querySelector('.hir-empty-state')?.remove();

    const existing = new Map();
    container.querySelectorAll('.hir-img-item').forEach(el => existing.set(el.dataset.id, el));
    existing.forEach((el, id) => { if (!state.images.find(i => String(i.id) === id)) el.remove(); });

    state.images.forEach((img, idx) => {
      const idStr = String(img.id);
      let el = existing.get(idStr);
      if (!el) {
        el = buildImageEl(state, img, idx);
        container.insertBefore(el, container.children[idx] || null);
      }
      if (img.id === flashId) { el.classList.remove('hir-new'); void el.offsetWidth; el.classList.add('hir-new'); }
    });

    const counter = state.wrap.querySelector(`.hir-counter[data-uid="${state.uid}"]`);
    if (counter) counter.textContent = `${state.images.length} фото`;
  }

  function buildImageEl(state, img, idx) {
    const el = document.createElement('div');
    el.className   = 'hir-img-item';
    el.dataset.id  = String(img.id);
    el.dataset.idx = idx;
    el.innerHTML = `
      <img src="${escHtml(img.url)}" alt="${escHtml(img.filename)}" loading="lazy" />
      <div class="hir-img-meta">
        <span class="hir-img-channel">#${escHtml(img.channel)}</span>
        <span>${escHtml(formatTime(img.created_at))}</span>
      </div>`;
    el.addEventListener('click', () => openFsOverlay(img));
    return el;
  }

  /* ── Controls ──────────────────────────────────────── */
  function bindControls(state) {
    const uid = state.uid;

    state.wrap.querySelector(`.hir-btn-connect[data-uid="${uid}"]`)?.addEventListener('click', () => {
      if (state.useSse && !state.wsOnly) {
        if (state.sseConnected) disconnectWS(state);
        else connectSSE(state);
        return;
      }
      if (state.connected || state.ws?.readyState <= 1) disconnectWS(state);
      else connectWS(state);
    });

    state.wrap.querySelector(`.hir-btn-clear[data-uid="${uid}"]`)?.addEventListener('click', () => {
      state.images = []; state.lastId = 0;
      const c = state.wrap.querySelector(`.hir-images[data-uid="${uid}"]`);
      if (c) c.innerHTML = `<div class="hir-empty-state"><div class="hir-empty-icon">📡</div><p>Ожидание изображений от Hermes.Wpf…</p></div>`;
      const cnt = state.wrap.querySelector(`.hir-counter[data-uid="${uid}"]`);
      if (cnt) cnt.textContent = '0 фото';
    });

    state.wrap.querySelector(`.hir-btn-fslive[data-uid="${uid}"]`)?.addEventListener('click', () => {
      state.fsLive = !state.fsLive;
      const btn = state.wrap.querySelector(`.hir-btn-fslive[data-uid="${uid}"]`);
      if (btn) {
        btn.textContent = state.fsLive ? '⬛ Стоп' : '▶ Авто-показ';
        btn.classList.toggle('active', state.fsLive);
      }
    });
  }

  /* ── Helpers ───────────────────────────────────────── */
  function setLed(state, cls) {
    state.wrap.querySelectorAll(`.hir-led[data-uid="${state.uid}"]`).forEach(el => el.className = 'hir-led ' + cls);
  }
  function setStatus(state, msg) {
    state.wrap.querySelectorAll(`.hir-status-msg[data-uid="${state.uid}"]`).forEach(el => el.textContent = msg);
  }
  function updateConnectBtn(state, on) {
    state.wrap.querySelectorAll(`.hir-btn-connect[data-uid="${state.uid}"]`).forEach(btn => {
      btn.textContent = on ? 'Отключить' : 'Подключить';
      btn.classList.toggle('active', on);
    });
  }
  function formatTime(iso) {
    try { return new Date(iso).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
    catch { return ''; }
  }
  function escHtml(s) {
    return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
  }

  /* ── Boot ──────────────────────────────────────────── */
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initAll);
  else initAll();

  window.HermesReceiver = { initAll, galleries };
})();
