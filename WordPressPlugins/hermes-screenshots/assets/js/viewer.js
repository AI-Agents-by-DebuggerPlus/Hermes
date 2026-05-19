(function () {
  "use strict";

  if (typeof HermesScreenshots === "undefined") {
    return;
  }

  var root = document.querySelector("[data-hermes-screenshot]");
  if (!root) {
    return;
  }

  var pollMs = Math.max(3000, (HermesScreenshots.pollSeconds || 10) * 1000);

  function refresh() {
    var body = new URLSearchParams();
    body.set("action", "hermes_screenshots_latest");
    body.set("nonce", HermesScreenshots.nonce);

    fetch(HermesScreenshots.ajaxUrl, {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: body.toString(),
    })
      .then(function (r) {
        return r.json();
      })
      .then(function (data) {
        if (!data || !data.success || !data.data || !data.data.html) {
          return;
        }
        var wrap = document.createElement("div");
        wrap.innerHTML = data.data.html;
        var next = wrap.firstElementChild;
        if (next && root.parentNode) {
          root.parentNode.replaceChild(next, root);
          root = next;
        }
      })
      .catch(function () {
        /* ignore */
      });
  }

  setInterval(refresh, pollMs);
})();
