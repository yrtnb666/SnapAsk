// SnapAsk 页面辅助脚本（注入到 DeepSeek 网页版）
// __CFG__ 会在运行时被替换成 {"input":"..","send":"..","reply":"..","generating":".."}
// 逻辑沿用 EAgentX 的成熟实现：定位输入框、写值、提交、判定「生成中 / 已完成」。
(function () {
  try {
    // 主窗口最小化时，嵌入的浏览器窗口会被一起隐藏，页面随即进入 hidden 状态：
    // DeepSeek 会停止渲染对话（DOM 里连消息都没有），我们也就抓不到回答——
    // 表现为「小窗卡在生成中、内容不同步」。这里让页面以为自己一直可见。
    try {
      Object.defineProperty(document, 'hidden', { get: function () { return false; }, configurable: true });
      Object.defineProperty(document, 'visibilityState', { get: function () { return 'visible'; }, configurable: true });
      if (window.addEventListener) {
        window.addEventListener('visibilitychange', function (e) {
          try { e.stopImmediatePropagation(); } catch (e1) { }
        }, true);
      }
    } catch (e0) { }

    var CFG = __CFG__;
    var old = window.__snapask;
    if (old && old.v === 1) { old.cfg = CFG; return 'ok'; }

    var E = { v: 1, cfg: CFG };

    E.all = function (sel) {
      try { return Array.prototype.slice.call(document.querySelectorAll(sel)); } catch (e) { return []; }
    };

    E.vis = function (el) {
      if (!el) return false;
      var r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return false;
      var st = window.getComputedStyle(el);
      return st.visibility !== 'hidden' && st.display !== 'none' && parseFloat(st.opacity || '1') > 0.01;
    };

    E.first = function (sel) {
      var a = E.all(sel);
      for (var i = 0; i < a.length; i++) { if (E.vis(a[i])) return a[i]; }
      return null;
    };

    E.norm = function (s) {
      return (s || '').replace(/\s+/g, ' ').trim();
    };

    E.msgCount = function () {
      return E.all('.ds-message').length;
    };

    E.userSent = function (text) {
      var needle = E.norm(text);
      if (!needle) return false;
      var ms = E.all('.ds-message');
      for (var i = ms.length - 1; i >= 0 && i >= ms.length - 4; i--) {
        if (ms[i].querySelector('.ds-markdown')) continue;
        var t = E.norm(ms[i].innerText || '');
        if (t === needle || (t.length > 40 && needle.indexOf(t) === 0)) return true;
      }
      return false;
    };

    E.input = function () {
      var el = E.first(E.cfg.input);
      if (el) return el;
      var t = E.all('textarea');
      for (var i = 0; i < t.length; i++) { if (E.vis(t[i])) return t[i]; }
      var e = E.all('[contenteditable="true"]');
      for (var j = 0; j < e.length; j++) { if (E.vis(e[j])) return e[j]; }
      return null;
    };

    E.sendBtn = function () {
      var b = E.first(E.cfg.send);
      if (b && !b.disabled && b.getAttribute('aria-disabled') !== 'true') return b;

      var el = E.input();
      if (!el) return null;
      var ir = el.getBoundingClientRect();
      var c = E.all('button,div[role="button"],div[class*="button"]');
      var best = null, bestScore = -1000;

      for (var i = 0; i < c.length; i++) {
        var x = c[i];
        if (!E.vis(x)) continue;
        if (x.disabled || x.getAttribute('aria-disabled') === 'true') continue;

        var txt = ((x.getAttribute('aria-label') || '') + ' ' + (x.getAttribute('title') || '') + ' ' + (x.innerText || ''))
          .replace(/\s+/g, ' ').trim();
        if (/attach|upload|附件|上传|文件|deep|思考|search|联网|语音|voice/i.test(txt)) continue;

        var r = x.getBoundingClientRect();
        var score = 0;
        if (/send|发送|submit/i.test(txt)) score += 100;
        if ((x.className || '').toString().indexOf('primary') >= 0) score += 25;
        var dy = Math.abs((r.top + r.height / 2) - (ir.top + ir.height / 2));
        var dx = r.left - ir.right;
        if (dx > -60 && dx < 260) score += 20;
        if (dy < ir.height) score += 20;
        score -= Math.min(60, dy / 8);

        if (score > bestScore) { bestScore = score; best = x; }
      }
      return bestScore >= 30 ? best : null;
    };

    E.replies = function () {
      var a = E.all(E.cfg.reply);
      var keep = [];
      for (var i = 0; i < a.length; i++) {
        if (!E.vis(a[i])) continue;
        var nested = false;
        for (var j = 0; j < a.length; j++) {
          if (i !== j && a[j] && a[j] !== a[i] && a[j].contains(a[i])) { nested = true; break; }
        }
        if (!nested) keep.push(a[i]);
      }
      return keep;
    };

    // 把 markdown 渲染结果压成纯文本；代码块还原成 ``` 围栏，便于复制。
    E.serialize = function (el) {
      var clone = el.cloneNode(true);
      var pres = clone.querySelectorAll('pre');
      for (var i = 0; i < pres.length; i++) {
        var pre = pres[i];
        var code = pre.querySelector('code');
        var content = code ? (code.innerText || code.textContent || '') : (pre.innerText || '');
        var block = '\n```\n' + content.replace(/\s+$/, '') + '\n```\n';
        pre.parentNode.replaceChild(document.createTextNode(block), pre);
      }
      return (clone.innerText || clone.textContent || '').trim();
    };

    E.generating = function () {
      // DeepSeek 生成时右下主按钮从「发送箭头」变成「停止方块」。
      var primary = E.first('.ds-button--primary');
      if (primary) {
        var cls = String(primary.className || '');
        var svg = primary.querySelector('svg');
        var icon = svg ? (svg.innerHTML || '') : '';
        if (icon.indexOf('M2 4.88') >= 0) return true;   // 停止方块
        if (icon.indexOf('M8.3125') >= 0) return false;  // 发送箭头
        if (cls.indexOf('disabled') >= 0) return false;
        return true;
      }
      if (E.first(E.cfg.generating)) return true;
      var c = E.all('div[role="button"],button');
      for (var i = 0; i < c.length; i++) {
        if (!E.vis(c[i])) continue;
        var t = (c[i].getAttribute('aria-label') || '') + ' ' + (c[i].className || '');
        var s = c[i].querySelector('svg');
        var ic = s ? (s.innerHTML || '') : '';
        if (ic.indexOf('M2 4.88') >= 0) return true;
        if (/stop|停止|中止/i.test(t)) return true;
      }
      return false;
    };

    // 一次返回：回复文本、是否已出现助手消息、动作栏按钮数、是否仍在生成。
    E.replyState = function () {
      var ms = E.all('.ds-message');
      for (var i = ms.length - 1; i >= 0; i--) {
        if (!E.vis(ms[i])) continue;
        var md = ms[i].querySelector('.ds-markdown');
        if (md) {
          var actions = 0;
          var p = ms[i].parentElement;
          if (p) {
            for (var k = 0; k < p.children.length; k++) {
              var sib = p.children[k];
              if (sib === ms[i]) continue;
              actions += sib.querySelectorAll('button,div[role="button"]').length;
            }
          }
          return { has: true, text: E.serialize(md), actions: actions, messages: ms.length, generating: E.generating() };
        }
        if (E.norm(ms[i].innerText || '').length > 0) {
          return { has: false, text: '', actions: 0, messages: ms.length, generating: E.generating() };
        }
      }
      return { has: false, text: '', actions: 0, messages: ms.length, generating: E.generating() };
    };

    // 图片附件状态：出现 blob 缩略图或文件卡片说明已进输入区；「上传中」表示还没传完。
    E.attachState = function () {
      var imgs = E.all('img[src^="blob:"]');
      var cards = E.all('[class*="file-item"],[class*="fileItem"],[class*="attach-item"],[class*="file-card"]');
      var body = document.body ? (document.body.innerText || '') : '';
      var uploading = /上传中|正在上传|uploading/i.test(body);
      return { count: imgs.length + cards.length, uploading: uploading };
    };

    E.rateLimit = function () {
      var ms = E.all('.ds-message');
      if (!ms.length) return false;
      var last = ms[ms.length - 1];
      var t = (last.parentElement ? last.parentElement.innerText : last.innerText) || '';
      return t.indexOf('消息发送过于频繁') >= 0 ||
             t.indexOf('请求过于频繁') >= 0 ||
             t.indexOf('发送太频繁') >= 0;
    };

    E.beforeSend = function () {
      return { count: E.replies().length, messages: E.msgCount() };
    };

    E.setInput = function (text) {
      var el = E.input();
      if (!el) return { ok: false, reason: '找不到输入框' };
      el.focus();
      var tag = (el.tagName || '').toUpperCase();
      if (tag === 'TEXTAREA' || tag === 'INPUT') {
        var proto = tag === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        var d = Object.getOwnPropertyDescriptor(proto, 'value');
        if (d && d.set) d.set.call(el, text); else el.value = text;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
      } else {
        el.innerHTML = '';
        var sel = window.getSelection();
        var range = document.createRange();
        range.selectNodeContents(el);
        sel.removeAllRanges();
        sel.addRange(range);
        var ok = false;
        try { ok = document.execCommand('insertText', false, text); } catch (e1) { ok = false; }
        if (!ok) {
          el.textContent = text;
          try { el.dispatchEvent(new InputEvent('input', { bubbles: true, data: text, inputType: 'insertText' })); }
          catch (e2) { el.dispatchEvent(new Event('input', { bubbles: true })); }
        }
        sel.removeAllRanges();
      }
      var cur = (tag === 'TEXTAREA' || tag === 'INPUT') ? el.value : (el.innerText || '');
      return { ok: true, length: (cur || '').length, tag: tag };
    };

    E.inputEmpty = function () {
      var el = E.input();
      if (!el) return true;
      var tag = (el.tagName || '').toUpperCase();
      var v = (tag === 'TEXTAREA' || tag === 'INPUT') ? el.value : (el.innerText || '');
      return !v || v.replace(/\s+/g, '').length === 0;
    };

    E.clickSend = function () {
      var b = E.sendBtn();
      if (b) { b.click(); return 'click'; }
      var el = E.input();
      if (!el) return 'no-input';
      el.focus();
      var o = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true, composed: true };
      ['keydown', 'keypress', 'keyup'].forEach(function (t) {
        try { el.dispatchEvent(new KeyboardEvent(t, o)); } catch (e) {}
      });
      return 'enter';
    };

    E.diag = function () {
      var inp = E.input();
      var b = E.sendBtn();
      return {
        url: location.href,
        title: document.title,
        input: !!inp,
        inputTag: inp ? inp.tagName : '',
        placeholder: inp ? (inp.getAttribute('placeholder') || '') : '',
        send: !!b,
        replies: E.replies().length,
        generating: E.generating(),
        login: !!document.querySelector('input[type="password"]'),
        sample: (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').slice(0, 160)
      };
    };

    window.__snapask = E;
    return 'ok';
  } catch (err) {
    return 'err:' + err;
  }
})()
