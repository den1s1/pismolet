(function (window, document) {
  'use strict';
  if (window.location.hostname !== 'pismolet.ru' && window.location.hostname !== 'www.pismolet.ru') return;

  var counter = 109909063;
  window.ym = window.ym || function () { (window.ym.a = window.ym.a || []).push(arguments); };
  window.ym.l = Date.now();
  window.ym(counter, 'init', {
    clickmap: false,
    trackLinks: true,
    accurateTrackBounce: true,
    webvisor: false
  });

  var tag = document.createElement('script');
  tag.async = true;
  tag.src = 'https://mc.yandex.ru/metrika/tag.js?id=' + counter;
  document.head.appendChild(tag);

  document.addEventListener('click', function (event) {
    var link = event.target.closest && event.target.closest('a[href]');
    if (!link) return;
    var target = new URL(link.href, window.location.href);
    if (target.origin !== 'https://app.pismolet.ru' || target.pathname !== '/account/register') return;

    // Keep navigation working even when analytics is blocked or unavailable.
    var sameTab = !event.defaultPrevented && event.button === 0 && !event.ctrlKey && !event.metaKey &&
      !event.shiftKey && !event.altKey && (!link.target || link.target === '_self');
    var navigated = false;
    function navigate() {
      if (!sameTab || navigated) return;
      navigated = true;
      window.location.assign(link.href);
    }
    if (sameTab) {
      event.preventDefault();
      window.setTimeout(navigate, 350);
    }
    try {
      window.ym(counter, 'reachGoal', 'registration_click', { page: window.location.pathname }, navigate);
    } catch (_) {
      navigate();
    }
  });
})(window, document);
