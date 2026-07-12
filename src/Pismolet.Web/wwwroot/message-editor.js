(function () {
  'use strict';

  var root = document.querySelector('[data-body-editor]');
  if (!root) return;

  var tabInput = root.querySelector('input[name="bodyTab"]');
  var formatInput = root.querySelector('input[name="bodyFormat"]');
  var tabButtons = root.querySelectorAll('[data-body-tab]');
  var panels = root.querySelectorAll('[data-body-panel]');
  var richEditor = root.querySelector('[data-rich-text-editor]');
  var fallbackBody = root.querySelector('textarea[name="body"][data-body-fallback]');
  var visualSource = root.querySelector('textarea[name="visualBody"]');
  var htmlSource = root.querySelector('textarea[name="htmlBody"]');

  function syncRichEditor() {
    if (!richEditor) return;
    var editable = richEditor.querySelector('[data-rich-editable]');
    var source = richEditor.querySelector('[data-rich-html-source]');
    if (!editable || !source) return;
    source.value = editable.innerHTML.trim();
  }

  function syncFallbackBody() {
    syncRichEditor();
    if (!fallbackBody) return;
    var tab = tabInput ? tabInput.value : 'visual';
    fallbackBody.value = tab === 'html'
      ? (htmlSource ? htmlSource.value : '')
      : (visualSource ? visualSource.value : '');
  }

  function selectBodyTab(tab) {
    if (tab !== 'html') tab = 'visual';
    syncFallbackBody();
    if (tabInput) tabInput.value = tab;
    if (formatInput) formatInput.value = 'html';
    tabButtons.forEach(function (button) {
      var active = button.getAttribute('data-body-tab') === tab;
      button.className = active ? 'button compact' : 'btn secondary compact';
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    panels.forEach(function (panel) {
      panel.style.display = panel.getAttribute('data-body-panel') === tab ? '' : 'none';
    });
  }

  tabButtons.forEach(function (button) {
    button.addEventListener('click', function () {
      selectBodyTab(button.getAttribute('data-body-tab'));
    });
  });

  var form = root.closest('form');
  if (form) form.addEventListener('submit', syncFallbackBody);

  if (!richEditor) {
    selectBodyTab(tabInput ? tabInput.value : 'visual');
    return;
  }

  var editable = richEditor.querySelector('[data-rich-editable]');
  var source = richEditor.querySelector('[data-rich-html-source]');
  if (!editable || !source) {
    selectBodyTab(tabInput ? tabInput.value : 'visual');
    return;
  }

  editable.innerHTML = source.value || '';
  editable.addEventListener('input', syncFallbackBody);
  editable.addEventListener('blur', syncFallbackBody);
  editable.addEventListener('click', function (event) {
    if (closestElement(event.target, 'a')) event.preventDefault();
  });

  var savedRange = null;

  function closestElement(node, selector) {
    var element = node && node.nodeType === Node.ELEMENT_NODE ? node : node && node.parentElement;
    return element && element.closest ? element.closest(selector) : null;
  }

  function isInsideEditable(node) {
    if (!node) return false;
    var element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentNode;
    return element === editable || editable.contains(element);
  }

  function rememberSelection() {
    var selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return;
    var range = selection.getRangeAt(0);
    if (!isInsideEditable(range.commonAncestorContainer)) return;
    savedRange = range.cloneRange();
    updateCurrentColor();
  }

  function restoreSelection(createAtEnd) {
    var range = savedRange;
    if (!range && createAtEnd) {
      range = document.createRange();
      range.selectNodeContents(editable);
      range.collapse(false);
      savedRange = range.cloneRange();
    }
    if (!range) return null;

    var selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    return range;
  }

  document.addEventListener('selectionchange', rememberSelection);
  editable.addEventListener('keyup', rememberSelection);
  editable.addEventListener('mouseup', rememberSelection);

  richEditor.querySelectorAll('[data-rich-command]').forEach(function (button) {
    button.addEventListener('mousedown', rememberSelection);
    button.addEventListener('click', function () {
      restoreSelection(true);
      document.execCommand(button.getAttribute('data-rich-command'), false, null);
      rememberSelection();
      syncFallbackBody();
      editable.focus();
    });
  });

  var fontSizeSelect = richEditor.querySelector('[data-rich-font-size]');
  var fontSizeValues = ['10px', '12px', '14px', '16px', '18px', '22px', '28px'];
  var pendingFontSize = '';

  function fillFontSizeOptions() {
    if (!fontSizeSelect) return;
    while (fontSizeSelect.options.length > 1) fontSizeSelect.remove(1);
    fontSizeValues.forEach(function (value) {
      var option = document.createElement('option');
      option.value = value;
      option.textContent = value.replace('px', '');
      fontSizeSelect.appendChild(option);
    });
  }

  function normalizeFontSizeMarkup(value) {
    var changed = false;
    editable.querySelectorAll('font[size="7"]').forEach(function (font) {
      font.removeAttribute('size');
      font.style.fontSize = value;
      changed = true;
    });
    return changed;
  }

  if (fontSizeSelect) {
    fillFontSizeOptions();
    fontSizeSelect.addEventListener('mousedown', rememberSelection);
    fontSizeSelect.addEventListener('change', function () {
      var value = fontSizeSelect.value;
      if (!value) return;
      restoreSelection(true);
      pendingFontSize = value;
      try {
        document.execCommand('styleWithCSS', false, false);
      } catch (_) {
      }
      document.execCommand('fontSize', false, '7');
      if (normalizeFontSizeMarkup(value)) pendingFontSize = '';
      fontSizeSelect.value = '';
      rememberSelection();
      syncFallbackBody();
      editable.focus();
    });

    editable.addEventListener('input', function () {
      if (!pendingFontSize || !normalizeFontSizeMarkup(pendingFontSize)) return;
      pendingFontSize = '';
      syncFallbackBody();
    });
  }

  var defaultTextColor = '#1f2937';
  var colorToggle = richEditor.querySelector('[data-rich-color-toggle]');
  var colorMenu = richEditor.querySelector('[data-rich-color-menu]');
  var colorCurrent = richEditor.querySelector('[data-rich-color-current]');
  var colorButtons = Array.prototype.slice.call(richEditor.querySelectorAll('[data-rich-color-value]'));
  var colorReset = richEditor.querySelector('[data-rich-color-reset]');
  var colorProbe = document.createElement('span');
  colorProbe.hidden = true;
  document.body.appendChild(colorProbe);
  var canonicalColors = {};

  function normalizeCssColor(value) {
    if (!value) return '';
    colorProbe.style.color = '';
    colorProbe.style.color = value;
    if (!colorProbe.style.color) return '';
    return window.getComputedStyle(colorProbe).color.replace(/\s+/g, '').toLowerCase();
  }

  colorButtons.forEach(function (button) {
    var value = button.getAttribute('data-rich-color-value');
    canonicalColors[normalizeCssColor(value)] = value;
  });
  canonicalColors[normalizeCssColor(defaultTextColor)] = defaultTextColor;

  function currentSelectionColor() {
    var range = savedRange;
    if (!range) return defaultTextColor;
    var node = range.startContainer;
    var element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
    if (!element || !editable.contains(element)) return defaultTextColor;
    var computed = normalizeCssColor(window.getComputedStyle(element).color);
    return canonicalColors[computed] || window.getComputedStyle(element).color || defaultTextColor;
  }

  function updateCurrentColor(forcedColor) {
    if (!colorCurrent) return;
    var color = forcedColor || currentSelectionColor();
    colorCurrent.style.backgroundColor = color;
    colorCurrent.setAttribute('aria-label', 'Текущий цвет текста: ' + color);
    var normalized = normalizeCssColor(color);
    colorButtons.forEach(function (button) {
      button.setAttribute(
        'aria-pressed',
        normalizeCssColor(button.getAttribute('data-rich-color-value')) === normalized ? 'true' : 'false');
    });
  }

  function closeColorMenu(restoreFocus) {
    if (!colorMenu) return;
    colorMenu.hidden = true;
    if (colorToggle) colorToggle.setAttribute('aria-expanded', 'false');
    if (restoreFocus && colorToggle) colorToggle.focus();
  }

  function openColorMenu() {
    if (!colorMenu) return;
    rememberSelection();
    colorMenu.hidden = false;
    if (colorToggle) colorToggle.setAttribute('aria-expanded', 'true');
    updateCurrentColor();
    var selected = colorMenu.querySelector('[aria-pressed="true"]') || colorButtons[0];
    if (selected) selected.focus();
  }

  function applyTextColor(color) {
    restoreSelection(true);
    try {
      document.execCommand('styleWithCSS', false, true);
    } catch (_) {
    }
    document.execCommand('foreColor', false, color);
    savedRange = null;
    rememberSelection();
    syncFallbackBody();
    updateCurrentColor(color);
    closeColorMenu(false);
    editable.focus();
  }

  if (colorToggle && colorMenu) {
    colorToggle.addEventListener('mousedown', rememberSelection);
    colorToggle.addEventListener('click', function () {
      if (colorMenu.hidden) openColorMenu(); else closeColorMenu(false);
    });
    colorMenu.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeColorMenu(true);
      }
    });
    colorButtons.forEach(function (button) {
      button.addEventListener('click', function () {
        applyTextColor(button.getAttribute('data-rich-color-value'));
      });
    });
    if (colorReset) {
      colorReset.addEventListener('click', function () {
        applyTextColor(defaultTextColor);
      });
    }
  }
  updateCurrentColor(defaultTextColor);

  var linkToggle = richEditor.querySelector('[data-rich-link-toggle]');
  var linkPopover = richEditor.querySelector('[data-rich-link-popover]');
  var linkInput = richEditor.querySelector('[data-rich-link-url]');
  var linkError = richEditor.querySelector('[data-rich-link-error]');
  var linkSave = richEditor.querySelector('[data-rich-link-save]');
  var linkCancel = richEditor.querySelector('[data-rich-link-cancel]');
  var linkDelete = richEditor.querySelector('[data-rich-link-delete]');
  var activeLink = null;

  function findAnchorForRange(range) {
    if (!range) return null;
    var candidates = [range.startContainer, range.endContainer, range.commonAncestorContainer];
    for (var index = 0; index < candidates.length; index++) {
      var anchor = closestElement(candidates[index], 'a');
      if (anchor && editable.contains(anchor)) return anchor;
    }
    return null;
  }

  function setLinkError(message) {
    if (linkError) {
      linkError.textContent = message || '';
      linkError.hidden = !message;
    }
    if (linkInput) linkInput.setAttribute('aria-invalid', message ? 'true' : 'false');
  }

  function normalizeLinkUrl(value) {
    var trimmed = (value || '').trim();
    if (!trimmed) return { ok: false, error: 'Введите адрес ссылки.' };

    var schemeMatch = trimmed.match(/^([a-z][a-z0-9+.-]*):/i);
    if (schemeMatch && !/^https?:$/i.test(schemeMatch[0])) {
      return { ok: false, error: 'Разрешены только ссылки с http:// или https://.' };
    }

    var candidate = trimmed;
    if (/^\/\//.test(candidate)) candidate = 'https:' + candidate;
    else if (!/^https?:\/\//i.test(candidate)) candidate = 'https://' + candidate;

    try {
      var url = new URL(candidate);
      if ((url.protocol !== 'http:' && url.protocol !== 'https:') || !url.hostname) {
        return { ok: false, error: 'Введите корректный адрес сайта.' };
      }
      return { ok: true, value: url.toString() };
    } catch (_) {
      return { ok: false, error: 'Введите корректный адрес сайта.' };
    }
  }

  function closeLinkPopover(restoreFocus) {
    if (!linkPopover) return;
    linkPopover.hidden = true;
    activeLink = null;
    setLinkError('');
    if (linkToggle) linkToggle.setAttribute('aria-expanded', 'false');
    if (restoreFocus && linkToggle) linkToggle.focus();
  }

  function openLinkPopover() {
    if (!linkPopover || !linkInput) return;
    rememberSelection();
    activeLink = findAnchorForRange(savedRange);
    linkInput.value = activeLink ? (activeLink.getAttribute('href') || '') : '';
    if (linkDelete) linkDelete.hidden = !activeLink;
    setLinkError('');
    linkPopover.hidden = false;
    if (linkToggle) linkToggle.setAttribute('aria-expanded', 'true');
    window.setTimeout(function () {
      linkInput.focus();
      linkInput.select();
    }, 0);
  }

  function saveLink() {
    if (!linkInput) return;
    var normalized = normalizeLinkUrl(linkInput.value);
    if (!normalized.ok) {
      setLinkError(normalized.error);
      linkInput.focus();
      return;
    }

    setLinkError('');
    if (activeLink && editable.contains(activeLink)) {
      activeLink.setAttribute('href', normalized.value);
    } else {
      var range = restoreSelection(true);
      if (range && !range.collapsed) {
        document.execCommand('createLink', false, normalized.value);
      } else if (range) {
        var anchor = document.createElement('a');
        anchor.href = normalized.value;
        anchor.textContent = normalized.value;
        range.insertNode(anchor);
        range.setStartAfter(anchor);
        range.collapse(true);
        savedRange = range.cloneRange();
      }
    }

    syncFallbackBody();
    closeLinkPopover(false);
    editable.focus();
    rememberSelection();
  }

  function deleteLink() {
    if (!activeLink || !editable.contains(activeLink)) return;
    var parent = activeLink.parentNode;
    var first = activeLink.firstChild;
    var last = activeLink.lastChild;
    while (activeLink.firstChild) parent.insertBefore(activeLink.firstChild, activeLink);
    parent.removeChild(activeLink);

    if (first && last) {
      var range = document.createRange();
      range.setStartBefore(first);
      range.setEndAfter(last);
      savedRange = range;
    }

    syncFallbackBody();
    closeLinkPopover(false);
    editable.focus();
    restoreSelection(false);
  }

  if (linkToggle && linkPopover && linkInput) {
    linkToggle.addEventListener('mousedown', rememberSelection);
    linkToggle.addEventListener('click', function () {
      if (linkPopover.hidden) openLinkPopover(); else closeLinkPopover(false);
    });
    linkInput.addEventListener('input', function () { setLinkError(''); });
    linkPopover.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeLinkPopover(true);
      } else if (event.key === 'Enter' && event.target === linkInput) {
        event.preventDefault();
        saveLink();
      }
    });
    if (linkSave) linkSave.addEventListener('click', saveLink);
    if (linkCancel) linkCancel.addEventListener('click', function () { closeLinkPopover(true); });
    if (linkDelete) linkDelete.addEventListener('click', deleteLink);
  }

  var clearFormattingButton = richEditor.querySelector('[data-rich-clear-formatting]');
  var preservedTags = {
    a: true,
    article: true,
    blockquote: true,
    br: true,
    div: true,
    em: true,
    h1: true,
    h2: true,
    h3: true,
    h4: true,
    h5: true,
    h6: true,
    i: true,
    li: true,
    ol: true,
    p: true,
    section: true,
    strong: true,
    b: true,
    table: true,
    tbody: true,
    td: true,
    tfoot: true,
    th: true,
    thead: true,
    tr: true,
    u: true,
    ul: true
  };

  function isServiceFooterElement(element) {
    return element.matches('.pismolet-footer, .service-preview-footer, [data-pismolet-service-footer]');
  }

  function wrapChildren(element, tagName) {
    if (!element.firstChild) return;
    var wrapper = document.createElement(tagName);
    while (element.firstChild) wrapper.appendChild(element.firstChild);
    element.appendChild(wrapper);
  }

  function unwrapElement(element) {
    var parent = element.parentNode;
    if (!parent) return;
    while (element.firstChild) parent.insertBefore(element.firstChild, element);
    parent.removeChild(element);
  }

  function cleanFormattingNode(parent) {
    Array.prototype.slice.call(parent.childNodes).forEach(function (node) {
      if (node.nodeType !== Node.ELEMENT_NODE) return;
      var element = node;
      if (isServiceFooterElement(element)) return;

      var style = element.style;
      var weight = style.fontWeight || '';
      var preserveBold = weight === 'bold' || (parseInt(weight, 10) >= 600);
      var preserveItalic = /italic|oblique/i.test(style.fontStyle || '');
      var preserveUnderline = /underline/i.test((style.textDecorationLine || '') + ' ' + (style.textDecoration || ''));

      cleanFormattingNode(element);
      if (preserveUnderline && !element.matches('u')) wrapChildren(element, 'u');
      if (preserveItalic && !element.matches('em, i')) wrapChildren(element, 'em');
      if (preserveBold && !element.matches('strong, b')) wrapChildren(element, 'strong');

      var tagName = element.tagName.toLowerCase();
      if (tagName === 'a') {
        var href = element.getAttribute('href');
        Array.prototype.slice.call(element.attributes).forEach(function (attribute) {
          element.removeAttribute(attribute.name);
        });
        if (href) element.setAttribute('href', href);
        else unwrapElement(element);
        return;
      }

      if (preservedTags[tagName]) {
        Array.prototype.slice.call(element.attributes).forEach(function (attribute) {
          element.removeAttribute(attribute.name);
        });
      } else {
        unwrapElement(element);
      }
    });
  }

  function clearSelectedFormatting() {
    var range = restoreSelection(false);
    if (!range || range.collapsed || !isInsideEditable(range.commonAncestorContainer)) {
      editable.focus();
      return;
    }

    var fragment = range.extractContents();
    cleanFormattingNode(fragment);
    var first = fragment.firstChild;
    var last = fragment.lastChild;
    range.insertNode(fragment);

    if (first && last) {
      range.setStartBefore(first);
      range.setEndAfter(last);
      savedRange = range.cloneRange();
      restoreSelection(false);
    }

    syncFallbackBody();
    editable.focus();
  }

  if (clearFormattingButton) {
    clearFormattingButton.addEventListener('mousedown', rememberSelection);
    clearFormattingButton.addEventListener('click', clearSelectedFormatting);
  }

  document.addEventListener('mousedown', function (event) {
    if (colorMenu && !colorMenu.hidden
        && !colorMenu.contains(event.target)
        && event.target !== colorToggle
        && !colorToggle.contains(event.target)) {
      closeColorMenu(false);
    }
    if (linkPopover && !linkPopover.hidden
        && !linkPopover.contains(event.target)
        && event.target !== linkToggle
        && !linkToggle.contains(event.target)) {
      closeLinkPopover(false);
    }
  });

  selectBodyTab(tabInput ? tabInput.value : 'visual');
})();