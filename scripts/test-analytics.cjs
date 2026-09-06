const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');
const assert = require('node:assert/strict');

function run(path, hostname, success = false) {
  const calls = [], tags = [], timers = [], navigations = [], events = {};
  const window = {
    location: { hostname, pathname: '/account/register', href: 'https://' + hostname + '/account/register?token=secret',
      assign: url => navigations.push(url) },
    setTimeout: fn => timers.push(fn)
  };
  const document = {
    currentScript: { dataset: { registrationSuccess: String(success) } },
    referrer: 'https://pismolet.ru/?email=private@example.test',
    head: { appendChild: tag => tags.push(tag) },
    createElement: () => ({}),
    addEventListener: (name, handler) => { events[name] = handler; }
  };
  runInNewContext(readFileSync(path, 'utf8'), { window, document, URL });
  calls.push(...(window.ym?.a || []).map(args => Array.from(args)));
  return { window, calls, tags, timers, navigations, events };
}

const appPath = 'src/Pismolet.Web/wwwroot/registration-analytics.js';
for (const success of [false, true]) {
  const result = run(appPath, 'app.pismolet.ru', success);
  assert.equal(result.calls.filter(c => c[1] === 'reachGoal').length, Number(success));
  assert.equal(result.calls.filter(c => c[1] === 'hit').length, 1);
  assert.equal(result.calls[0][2].webvisor, false);
  assert.equal(result.calls[0][2].trackLinks, false);
  assert.ok(!JSON.stringify(result.calls).includes('secret'));
  assert.ok(!JSON.stringify(result.calls).includes('private@example.test'));
}
assert.equal(run(appPath, 'localhost', true).tags.length, 0);
const publicPath = 'public_html/assets/analytics.js';
assert.equal(run(publicPath, 'localhost').tags.length, 0);
assert.equal(run(publicPath, 'app.pismolet.ru').tags.length, 0);
for (const ctrlKey of [false, true]) {
  const result = run(publicPath, 'pismolet.ru');
  const link = { href: 'https://app.pismolet.ru/account/register', target: '' };
  let prevented = false;
  result.events.click({ target: { closest: () => link }, button: 0, ctrlKey,
    preventDefault: () => { prevented = true; } });
  assert.equal(prevented, !ctrlKey);
  const goal = Array.from(result.window.ym.a).find(c => c[1] === 'reachGoal');
  assert.equal(goal[2], 'registration_click');
  if (!ctrlKey) {
    result.timers[0](); // Blocked tag still allows navigation.
    goal[4](); // A late callback must not navigate twice.
    assert.equal(result.navigations.length, 1);
  } else assert.equal(result.timers.length, 0);
}
console.log('Analytics checks passed: success/error distinction, URL privacy, host guards, navigation fallback.');
