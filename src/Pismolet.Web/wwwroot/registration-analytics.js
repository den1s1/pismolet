(function (window, document) {
  'use strict';
  if (window.location.hostname !== 'app.pismolet.ru' || window.location.pathname !== '/account/register') return;

  var counter = 109909063;
  var script = document.currentScript;
  var succeeded = script && script.dataset.registrationSuccess === 'true';
  var referrer = '';
  try {
    var previous = new URL(document.referrer);
    referrer = previous.origin + previous.pathname;
  } catch (_) { /* Direct visit. */ }

  window.ym = window.ym || function () { (window.ym.a = window.ym.a || []).push(arguments); };
  window.ym.l = Date.now();
  // Only the registration page is measured; no form values, tokens or account identifiers.
  window.ym(counter, 'init', {
    defer: true,
    webvisor: false,
    clickmap: false,
    trackLinks: false,
    accurateTrackBounce: true,
    url: 'https://app.pismolet.ru/account/register',
    referrer: referrer
  });
  window.ym(counter, 'hit', 'https://app.pismolet.ru/account/register', {
    title: succeeded ? 'Аккаунт создан' : 'Регистрация', referer: referrer
  });
  if (succeeded) window.ym(counter, 'reachGoal', 'registration_success');

  var tag = document.createElement('script');
  tag.async = true;
  tag.src = 'https://mc.yandex.ru/metrika/tag.js?id=' + counter;
  document.head.appendChild(tag);
})(window, document);
