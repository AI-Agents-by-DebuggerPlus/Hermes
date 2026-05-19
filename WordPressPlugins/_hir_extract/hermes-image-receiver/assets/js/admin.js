/* Hermes Image Receiver — Admin JS */
jQuery(function ($) {
  var cfg = window.hirAdmin || {};

  // Status check
  function checkStatus() {
    fetch(cfg.restUrl + 'status')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data && data.status === 'ok') {
          $('#hir-status-dot').addClass('ok').removeClass('error');
          var sse = data.sse ? 'SSE включён' : 'SSE выключен';
          $('#hir-status-text').text('REST API доступен. ' + sse + '. v' + (data.version || '?'));
        } else {
          throw new Error('bad');
        }
      })
      .catch(() => {
        $('#hir-status-dot').addClass('error').removeClass('ok');
        $('#hir-status-text').text('REST API недоступен');
      });
  }

  checkStatus();
  $('#hir-refresh-status').on('click', checkStatus);

  // Regen token
  $('#hir-regen-token').on('click', function () {
    var chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
    var token = Array.from({length: 32}, () => chars[Math.floor(Math.random() * chars.length)]).join('');
    $('#hir-token-field').val(token).removeAttr('readonly');
  });

  // Load recent images
  function loadRecent() {
    fetch(cfg.restUrl + 'images?limit=12')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        var $wrap = $('#hir-recent-images');
        if (!data || !data.images || !data.images.length) {
          $wrap.html('<p>Изображений пока нет</p>');
          return;
        }
        $wrap.empty();
        data.images.forEach(function (img) {
          $wrap.append('<img src="' + $('<div>').text(img.url).html() + '" title="' + img.filename + '" />');
        });
      })
      .catch(() => { $('#hir-recent-images').html('<p>Ошибка загрузки</p>'); });
  }

  loadRecent();

  // Clear all
  $('#hir-clear-all').on('click', function () {
    if (!confirm('Удалить все записи из БД?')) return;
    fetch(cfg.restUrl + 'images', {
      method: 'DELETE',
      headers: { 'X-WP-Nonce': cfg.nonce }
    }).then(loadRecent);
  });
});
