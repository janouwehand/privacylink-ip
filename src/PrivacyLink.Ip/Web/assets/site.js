/* PrivacyLink IP - client-side enhancement.
 *
 * The server already rendered the address of the connection that loaded the
 * page. This script only asks for the missing address family, once, and wires
 * up the Copy buttons. It stores nothing and talks to no one else.
 */
(function () {
  "use strict";

  var ENDPOINTS = {
    4: "https://v4.ip.privacylink.nl/api/ip",
    6: "https://v6.ip.privacylink.nl/api/ip"
  };

  var TIMEOUT_MS = 3000;
  var COPY_RESET_MS = 1800;
  var dutch = document.documentElement.lang === "nl";

  var RESULT_TEXT = dutch ? {
    timeout: "controle duurde te lang",
    unavailable: "niet bereikbaar vanuit deze browser",
    error: "controle mislukt"
  } : {
    timeout: "check timed out",
    unavailable: "not reachable from this browser",
    error: "check failed"
  };

  var state = {
    4: { status: "loading", ip: "", note: "" },
    6: { status: "loading", ip: "", note: "" }
  };

  var copyTimers = { 4: 0, 6: 0 };
  var parts = {};
  var announcer = null;
  var announceTimer = 0;

  function announce(message) {
    if (!announcer) {
      announcer = document.getElementById("announcer");
    }
    if (!announcer) {
      return;
    }
    window.clearTimeout(announceTimer);
    announcer.textContent = "";
    announceTimer = window.setTimeout(function () {
      announcer.textContent = message;
    }, 80);
  }

  function label(family) {
    return family === 4 ? "IPv4" : "IPv6";
  }

  function key(family) {
    return family === 4 ? "ipv4" : "ipv6";
  }

  /* Returns { ip, version } for a plain IP string, or null. */
  function parseIp(raw) {
    var text = String(raw === null || raw === undefined ? "" : raw).trim();

    if (!text || /\s/.test(text)) {
      return null;
    }

    if (/^\d{1,3}(\.\d{1,3}){3}$/.test(text)) {
      var octets = text.split(".");
      for (var i = 0; i < octets.length; i += 1) {
        var part = octets[i];
        if (part.length > 1 && part.charAt(0) === "0") {
          return null;
        }
        if (Number(part) > 255) {
          return null;
        }
      }
      return { ip: text, version: 4 };
    }

    if (text.indexOf(":") !== -1 && /^[0-9a-fA-F:.]+$/.test(text)) {
      try {
        /* The URL parser rejects malformed IPv6 literals for us. */
        new URL("http://[" + text + "]/");
      } catch (error) {
        return null;
      }
      return { ip: text, version: 6 };
    }

    return null;
  }

  function ui(family) {
    if (!parts[family]) {
      var name = key(family);
      parts[family] = {
        row: document.querySelector('.ip-row[data-family="' + family + '"]'),
        value: document.getElementById(name + "-value"),
        copy: document.getElementById(name + "-copy"),
        retry: document.getElementById(name + "-retry"),
        note: document.getElementById(name + "-note")
      };
    }
    return parts[family];
  }

  function setCopyLabel(button, text) {
    if (!button) {
      return;
    }
    var span = button.querySelector(".btn__label, .copy__label");
    if (span) {
      span.textContent = text;
    } else {
      button.textContent = text;
    }
  }

  function selectValue(element) {
    if (!element) {
      return;
    }
    try {
      var range = document.createRange();
      range.selectNodeContents(element);
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
    } catch (error) {
      /* Selection is a convenience only. */
    }
  }

  function noteFor(family) {
    var entry = state[family];
    if (entry.note) {
      return entry.note;
    }
    var name = label(family);
    if (dutch) {
      switch (entry.status) {
        case "loading": return name + "-adres controleren\u2026";
        case "timeout": return "Geen antwoord binnen 3 seconden. Een firewall, proxy of VPN kan deze controle blokkeren.";
        case "unavailable": return "Niet bereikbaar vanuit deze browser. Dit bewijst niet dat je verbinding geen " + name + " heeft; een VPN, firewall of proxy kan dit veroorzaken.";
        case "error": return "De " + name + "-controle kon niet worden voltooid. Probeer het later opnieuw.";
        default: return "";
      }
    }
    switch (entry.status) {
      case "loading":
        return "Checking for an " + name + " address\u2026";
      case "timeout":
        return "No answer within 3 seconds. A firewall, proxy or VPN may be blocking this check.";
      case "unavailable":
        return "Not reachable from this browser. That does not prove your connection lacks " +
          name + " \u2014 a VPN, firewall or proxy can cause it.";
      case "error":
        return "The " + name + " check could not be completed. Try again in a moment.";
      default:
        return "";
    }
  }

  function render(family) {
    var entry = state[family];
    var el = ui(family);
    if (!el.row) {
      return;
    }

    el.row.setAttribute("data-status", entry.status);
    el.row.setAttribute("aria-busy", entry.status === "loading" ? "true" : "false");

    var text = entry.ip || "\u2014";
    if (el.value && el.value.textContent !== text) {
      el.value.textContent = text;
    }

    var note = noteFor(family);
    if (el.note) {
      if (note) {
        if (el.note.textContent !== note) {
          el.note.textContent = note;
        }
        el.note.hidden = false;
      } else {
        el.note.textContent = "";
        el.note.hidden = true;
      }
    }

    if (el.copy) {
      if (entry.ip) {
        el.copy.disabled = false;
        el.copy.setAttribute("aria-label", dutch ? label(family) + "-adres kopiëren" : "Copy " + label(family) + " address");
      } else {
        el.copy.disabled = true;
      }
    }

    if (el.retry) {
      var retryable = entry.status === "unavailable" || entry.status === "timeout" || entry.status === "error";
      el.retry.hidden = !retryable;
      if (retryable) {
        el.retry.disabled = false;
      }
    }
  }

  function phrase(family) {
    var entry = state[family];
    var name = label(family);
    if (dutch) {
      switch (entry.status) {
        case "available": return name + " beschikbaar";
        case "loading": return name + " controleren\u2026";
        case "timeout": return name + " duurde te lang";
        case "unavailable": return name + " niet bereikbaar";
        default: return name + "-controle mislukt";
      }
    }
    switch (entry.status) {
      case "available":
        return name + " available";
      case "loading":
        return "checking " + name + "\u2026";
      case "timeout":
        return name + " timed out";
      case "unavailable":
        return name + " not reachable";
      default:
        return name + " check failed";
    }
  }

  function renderConnection() {
    var line = document.getElementById("connection");
    var text = document.getElementById("connection-text");
    if (!line || !text) {
      return;
    }

    var both = state[4].status === "available" && state[6].status === "available";
    var busy = state[4].status === "loading" || state[6].status === "loading";
    var message = both
      ? (dutch ? "Verbinding: IPv4 + IPv6 beschikbaar" : "Connection: IPv4 + IPv6 available")
      : (dutch ? "Verbinding: " : "Connection: ") + phrase(4) + " \u00B7 " + phrase(6);

    if (text.textContent !== message) {
      text.textContent = message;
    }
    line.setAttribute("data-state", both ? "ok" : busy ? "wait" : "warn");
  }

  function copyText(value) {
    function legacyCopy() {
      var area = document.createElement("textarea");
      area.value = value;
      area.setAttribute("readonly", "");
      area.setAttribute("aria-hidden", "true");
      area.style.position = "fixed";
      area.style.top = "-1000px";
      area.style.left = "0";
      area.style.opacity = "0";

      var succeeded = false;
      try {
        document.body.appendChild(area);
        area.select();
        succeeded = typeof document.execCommand === "function" && document.execCommand("copy") === true;
      } catch (error) {
        succeeded = false;
      }

      if (area.parentNode) {
        area.parentNode.removeChild(area);
      }

      return succeeded;
    }

    var attempt;
    if (navigator.clipboard && navigator.clipboard.writeText && window.isSecureContext) {
      attempt = navigator.clipboard.writeText(value).then(function () {
        return true;
      }, function () {
        return legacyCopy();
      });
    } else {
      attempt = Promise.resolve(legacyCopy());
    }

    return attempt.then(function (ok) {
      if (!ok) {
        throw new Error("copy_unavailable");
      }
    });
  }

  function onCopy(family) {
    var entry = state[family];
    var el = ui(family);
    if (!entry.ip || !el.copy) {
      return;
    }

    copyText(entry.ip).then(function () {
      window.clearTimeout(copyTimers[family]);
      el.copy.classList.add("is-copied");
      setCopyLabel(el.copy, dutch ? "Gekopieerd" : "Copied");
      copyTimers[family] = window.setTimeout(function () {
        el.copy.classList.remove("is-copied");
        setCopyLabel(el.copy, dutch ? "Kopiëren" : "Copy");
      }, COPY_RESET_MS);
      announce(label(family) + (dutch ? "-adres gekopieerd." : " address copied."));
    }).catch(function () {
      window.clearTimeout(copyTimers[family]);
      setCopyLabel(el.copy, dutch ? "Kopiëren mislukt" : "Copy failed");
      copyTimers[family] = window.setTimeout(function () {
        setCopyLabel(el.copy, dutch ? "Kopiëren" : "Copy");
      }, COPY_RESET_MS + 700);
      selectValue(el.value);
      announce(dutch ? "Kopiëren mislukt. Het adres is geselecteerd zodat je het met je toetsenbord kunt kopiëren." : "Copy failed. The address is selected, so you can copy it with your keyboard.");
    });
  }

  function readServerState() {
    var element = document.getElementById("ip-server-state");
    if (!element) {
      return null;
    }

    try {
      var data = JSON.parse(element.textContent || "");
      if (!data || typeof data.initialIp !== "string") {
        return null;
      }

      var parsed = parseIp(data.initialIp);
      if (!parsed || Number(data.initialVersion) !== parsed.version) {
        return null;
      }

      var other = Number(data.otherVersion);
      if (other !== 4 && other !== 6) {
        other = parsed.version === 4 ? 6 : 4;
      }
      if (other === parsed.version) {
        other = parsed.version === 4 ? 6 : 4;
      }

      return { ip: parsed.ip, version: parsed.version, otherVersion: other };
    } catch (error) {
      return null;
    }
  }

  function readRendered(family) {
    var el = ui(family).value;
    return el ? parseIp(el.textContent) : null;
  }

  function detect(family) {
    var entry = state[family];
    if (entry.pending) {
      return;
    }

    entry.pending = true;
    entry.status = "loading";
    entry.note = "";
    render(family);
    renderConnection();

    var controller = null;
    var timedOut = false;
    var timer = 0;

    function fail(status) {
      window.clearTimeout(timer);
      entry.pending = false;
      entry.status = status;
      entry.note = "";
      render(family);
      renderConnection();
      announce(label(family) + " " + RESULT_TEXT[status] + ".");
    }

    if (typeof window.fetch !== "function") {
      fail("error");
      return;
    }

    if (typeof window.AbortController === "function") {
      controller = new window.AbortController();
      timer = window.setTimeout(function () {
        timedOut = true;
        controller.abort();
      }, TIMEOUT_MS);
    }

    var options = {
      method: "GET",
      mode: "cors",
      credentials: "omit",
      cache: "no-store",
      redirect: "error"
    };
    if (controller) {
      options.signal = controller.signal;
    }

    window.fetch(ENDPOINTS[family], options).then(function (response) {
      if (!response.ok) {
        throw new Error("http_" + response.status);
      }
      return response.text();
    }).then(function (body) {
      var parsed = parseIp(body);
      if (!parsed) {
        throw new Error("invalid_ip");
      }
      if (parsed.version !== family) {
        throw new Error("family_mismatch");
      }
      window.clearTimeout(timer);
      entry.pending = false;
      entry.ip = parsed.ip;
      entry.status = "available";
      entry.note = "";
      render(family);
      renderConnection();
      announce(label(family) + (dutch ? "-adres gevonden." : " address found."));
    }).catch(function (error) {
      if (timedOut || (error && error.name === "AbortError")) {
        fail("timeout");
      } else if (error && error.name === "TypeError") {
        /* DNS, CORS or connection failure: the host did not answer. */
        fail("unavailable");
      } else {
        fail("error");
      }
    });
  }

  function bind() {
    [4, 6].forEach(function (family) {
      var el = ui(family);
      if (el.copy) {
        el.copy.addEventListener("click", function () {
          onCopy(family);
        });
      }
      if (el.retry) {
        el.retry.addEventListener("click", function () {
          el.retry.disabled = true;
          detect(family);
        });
      }
    });
  }

  function initTabs() {
    var groups = document.querySelectorAll("[data-tabs]");

    Array.prototype.forEach.call(groups, function (group) {
      var list = group.querySelector('[role="tablist"]');
      if (!list) {
        return;
      }

      var tabs = Array.prototype.slice.call(list.querySelectorAll('[role="tab"]'));
      if (!tabs.length) {
        return;
      }

      var panels = {};
      var complete = true;

      tabs.forEach(function (tab) {
        var controlled = tab.getAttribute("aria-controls");
        var panel = controlled ? document.getElementById(controlled) : null;
        if (!panel) {
          complete = false;
        } else {
          panels[tab.id] = panel;
        }
      });

      if (!complete) {
        return;
      }

      var vertical = list.getAttribute("aria-orientation") === "vertical";

      function select(tab, focus, updateHash) {
        tabs.forEach(function (other) {
          var active = other === tab;
          other.setAttribute("aria-selected", active ? "true" : "false");
          other.tabIndex = active ? 0 : -1;
          panels[other.id].hidden = !active;
        });

        if (focus) {
          tab.focus();
        }

        if (updateHash) {
          var hash = "#" + tab.id;
          try {
            if (window.location.hash !== hash) {
              window.history.replaceState(null, "", hash);
            }
          } catch (error) {
            /* A history API that refuses the update must not break the tabs. */
          }
        }
      }

      function tabFromHash() {
        var id = window.location.hash.replace(/^#/, "");
        if (!id) {
          return null;
        }
        for (var i = 0; i < tabs.length; i += 1) {
          if (tabs[i].id === id) {
            return tabs[i];
          }
        }
        return null;
      }

      var initial = tabFromHash();
      if (!initial) {
        initial = tabs.filter(function (tab) {
          return tab.getAttribute("aria-selected") === "true";
        })[0] || tabs[0];
      }

      select(initial, false, false);

      list.addEventListener("click", function (event) {
        var tab = event.target.closest ? event.target.closest('[role="tab"]') : null;
        if (!tab || tabs.indexOf(tab) === -1) {
          return;
        }
        select(tab, false, true);
      });

      list.addEventListener("keydown", function (event) {
        var current = tabs.indexOf(document.activeElement);
        if (current === -1) {
          return;
        }

        var next = -1;
        if ((!vertical && event.key === "ArrowRight") || (vertical && event.key === "ArrowDown")) {
          next = (current + 1) % tabs.length;
        } else if ((!vertical && event.key === "ArrowLeft") || (vertical && event.key === "ArrowUp")) {
          next = (current - 1 + tabs.length) % tabs.length;
        } else if (event.key === "Home") {
          next = 0;
        } else if (event.key === "End") {
          next = tabs.length - 1;
        }

        if (next === -1) {
          return;
        }

        event.preventDefault();
        select(tabs[next], true, true);
      });
    });
  }

  function snippetTarget(button) {
    var selector = button.getAttribute("data-copy-snippet");

    if (selector && selector !== "true") {
      try {
        var found = document.querySelector(selector);
        if (found) {
          return found.tagName === "CODE" ? found : found.querySelector("code");
        }
      } catch (error) {
        /* Invalid selector: fall back to the nearest card. */
      }
    }

    var card = button.closest ? button.closest(".codecard") : null;
    if (card) {
      return card.querySelector("pre code") || card.querySelector("code");
    }

    return null;
  }

  function initSnippetCopy() {
    var buttons = document.querySelectorAll("[data-copy-snippet]");

    Array.prototype.forEach.call(buttons, function (button) {
      var timer = 0;

      button.addEventListener("click", function () {
        var target = snippetTarget(button);
        if (!target) {
          return;
        }

        var value = (target.textContent || "").replace(/\s+$/, "");
        if (!value) {
          return;
        }

        copyText(value).then(function () {
          window.clearTimeout(timer);
          button.classList.add("is-copied");
          setCopyLabel(button, "Copied");
          timer = window.setTimeout(function () {
            button.classList.remove("is-copied");
            setCopyLabel(button, "Copy");
          }, COPY_RESET_MS);
          announce("Example copied.");
        }).catch(function () {
          window.clearTimeout(timer);
          setCopyLabel(button, "Copy failed");
          timer = window.setTimeout(function () {
            setCopyLabel(button, "Copy");
          }, COPY_RESET_MS + 700);
          selectValue(target);
          announce("Copy failed. The example is selected, so you can copy it with your keyboard.");
        });
      });
    });
  }

  function init() {
    announcer = document.getElementById("announcer");

    initTabs();
    initSnippetCopy();

    if (!document.querySelector(".ip-row")) {
      return;
    }

    var server = readServerState();
    var initialFamily = 0;

    if (server) {
      initialFamily = server.version;
      state[initialFamily].status = "available";
      state[initialFamily].ip = server.ip;
    } else {
      /* Fall back to whatever the server rendered into the rows. */
      var rendered4 = readRendered(4);
      var rendered6 = readRendered(6);
      if (rendered4) {
        initialFamily = 4;
        state[4].status = "available";
        state[4].ip = rendered4.ip;
      } else if (rendered6) {
        initialFamily = 6;
        state[6].status = "available";
        state[6].ip = rendered6.ip;
      }
    }

    bind();

    if (!initialFamily) {
      state[4] = {
        status: "error",
        ip: "",
        note: dutch ? "Deze pagina kon het adres van de server niet lezen. Laad de pagina opnieuw." : "This page could not read the address the server sent. Reload the page to try again."
      };
      state[6] = {
        status: "error",
        ip: "",
        note: dutch ? "Deze pagina kon het adres van de server niet lezen. Laad de pagina opnieuw." : "This page could not read the address the server sent. Reload the page to try again."
      };
      render(4);
      render(6);
      renderConnection();
      return;
    }

    var otherFamily = initialFamily === 4 ? 6 : 4;
    state[otherFamily].status = "loading";
    state[otherFamily].ip = "";
    state[otherFamily].note = "";

    render(initialFamily);
    render(otherFamily);
    renderConnection();

    detect(otherFamily);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
