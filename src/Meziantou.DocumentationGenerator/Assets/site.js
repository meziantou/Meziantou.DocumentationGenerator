// Progressive enhancements. Every page is usable without JavaScript: these features only add convenience.
(function () {
    'use strict';

    var THEME_KEY = 'docgen-theme';
    var root = document.documentElement;

    function readStorage(key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    }

    function writeStorage(key, value) {
        try {
            if (value === null) window.localStorage.removeItem(key); else window.localStorage.setItem(key, value);
            return true;
        } catch (e) {
            return false;
        }
    }

    function announce(message) {
        var region = document.getElementById('live-status');
        if (!region) return;
        region.textContent = '';
        window.setTimeout(function () { region.textContent = message; }, 50);
    }

    // Theme: "system" follows prefers-color-scheme through CSS; "light" and "dark" are stored when storage is available
    function initTheme() {
        var select = document.getElementById('theme-select');
        if (!select) return;
        var stored = readStorage(THEME_KEY);
        select.value = stored === 'light' || stored === 'dark' ? stored : 'system';
        select.closest('[data-theme-picker]').hidden = false;
        select.addEventListener('change', function () {
            var value = select.value;
            if (value === 'light' || value === 'dark') root.setAttribute('data-theme', value); else root.removeAttribute('data-theme');
            writeStorage(THEME_KEY, value === 'system' ? null : value);
        });
    }

    function initMenu() {
        var button = document.querySelector('.menu-button');
        var sidebar = document.getElementById('sidebar');
        if (!button || !sidebar) return;
        button.hidden = false;
        button.addEventListener('click', function () {
            var open = sidebar.classList.toggle('is-open');
            button.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (open) {
                var current = sidebar.querySelector('[aria-current=page]') || sidebar.querySelector('a');
                if (current) current.focus();
            }
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && sidebar.classList.contains('is-open')) {
                sidebar.classList.remove('is-open');
                button.setAttribute('aria-expanded', 'false');
                button.focus();
            }
        });
    }

    // Scrolls the sidebar, never the page, so the current page is visible in the navigation
    function revealCurrentPage() {
        var sidebar = document.getElementById('sidebar');
        if (!sidebar || sidebar.scrollHeight <= sidebar.clientHeight) return;
        var current = sidebar.querySelector('[aria-current=page]');
        if (!current) return;
        var bounds = sidebar.getBoundingClientRect();
        var top = Math.max(bounds.top, 0);
        var bottom = Math.min(bounds.bottom, window.innerHeight);
        var rect = current.getBoundingClientRect();
        if (rect.top >= top && rect.bottom <= bottom) return;
        sidebar.scrollTop += rect.top - top - (bottom - top - rect.height) / 2;
    }

    function initCopyButtons() {
        var blocks = document.querySelectorAll('main pre > code');
        for (var i = 0; i < blocks.length; i++) {
            (function (code) {
                var button = document.createElement('button');
                button.type = 'button';
                button.className = 'copy-button';
                button.textContent = 'Copy';
                button.setAttribute('aria-label', 'Copy code to the clipboard');
                button.addEventListener('click', function () {
                    var text = code.textContent;
                    if (!navigator.clipboard || !navigator.clipboard.writeText) {
                        announce('Copying is not available in this browser.');
                        return;
                    }

                    navigator.clipboard.writeText(text).then(function () {
                        button.textContent = 'Copied';
                        announce('Code copied to the clipboard.');
                        window.setTimeout(function () { button.textContent = 'Copy'; }, 2000);
                    }, function () {
                        announce('The code could not be copied.');
                    });
                });
                // The button is outside the pre so that it does not move when the code scrolls horizontally
                var pre = code.parentElement;
                var wrapper = document.createElement('div');
                wrapper.className = 'code-block';
                pre.parentNode.insertBefore(wrapper, pre);
                wrapper.appendChild(pre);
                wrapper.appendChild(button);
            })(blocks[i]);
        }
    }

    // Permalinks on headings and API members
    function initPermalinks() {
        var targets = document.querySelectorAll('main h2[id], main h3[id], main h4[id], main section.api-member[id] > .heading-row > :is(h3, h4), main .overload-group[id] > h3');
        for (var i = 0; i < targets.length; i++) {
            var heading = targets[i];
            var id = heading.id || (heading.closest('[id]') || {}).id;
            if (!id) continue;
            var link = document.createElement('a');
            link.className = 'heading-anchor';
            link.href = '#' + encodeURIComponent(id).replace(/%2C/g, ',').replace(/%28/g, '(').replace(/%29/g, ')');
            link.setAttribute('aria-label', 'Permalink to ' + heading.textContent);
            link.textContent = '#';
            heading.appendChild(link);
        }
    }

    // Optional filter by target framework. Every variant is visible without JavaScript and the URL never changes.
    function initFrameworkFilter() {
        var container = document.querySelector('[data-tfm-filter]');
        if (!container) return;
        var select = container.querySelector('select');
        var frameworks = select.getAttribute('data-tfm-options').split(' ');
        var all = document.createElement('option');
        all.value = '';
        all.textContent = 'All';
        select.appendChild(all);
        frameworks.forEach(function (tfm) {
            var option = document.createElement('option');
            option.value = tfm;
            option.textContent = tfm;
            select.appendChild(option);
        });
        container.hidden = false;
        select.addEventListener('change', function () {
            var value = select.value;
            var elements = document.querySelectorAll('main [data-tfms]:not(article)');
            var hidden = 0;
            for (var i = 0; i < elements.length; i++) {
                var visible = !value || (' ' + elements[i].getAttribute('data-tfms') + ' ').indexOf(' ' + value + ' ') >= 0;
                elements[i].hidden = !visible;
                if (!visible && elements[i].classList.contains('api-member')) hidden++;
            }

            announce(value ? 'Showing the APIs available in ' + value + '. ' + hidden + ' members are hidden.' : 'Showing the APIs of every target framework.');
        });
    }

    // Highlights the heading currently visible in the "On this page" navigation
    function initActiveHeading() {
        var links = document.querySelectorAll('.page-toc a[href^="#"]');
        if (!links.length || !('IntersectionObserver' in window)) return;
        var byId = {};
        var headings = [];
        for (var i = 0; i < links.length; i++) {
            var id = decodeURIComponent(links[i].getAttribute('href').slice(1));
            var heading = document.getElementById(id);
            if (heading) { byId[id] = links[i]; headings.push(heading); }
        }

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                for (var key in byId) byId[key].removeAttribute('aria-current');
                var link = byId[entry.target.id];
                if (link) link.setAttribute('aria-current', 'true');
            });
        }, { rootMargin: '0px 0px -70% 0px' });
        headings.forEach(function (h) { observer.observe(h); });
    }

    // Regions that scroll horizontally (wide tables, long code lines) must be reachable with the keyboard
    function initScrollableRegions() {
        function update() {
            var elements = document.querySelectorAll('main pre, main table');
            for (var i = 0; i < elements.length; i++) {
                var element = elements[i];
                if (element.scrollWidth > element.clientWidth + 1) {
                    element.setAttribute('tabindex', '0');
                } else if (element.getAttribute('tabindex') === '0') {
                    element.removeAttribute('tabindex');
                }
            }
        }

        update();
        var timer = 0;
        window.addEventListener('resize', function () { clearTimeout(timer); timer = setTimeout(update, 200); });
    }

    function init() {
        initScrollableRegions();
        initTheme();
        initMenu();
        revealCurrentPage();
        initCopyButtons();
        initPermalinks();
        initFrameworkFilter();
        initActiveHeading();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
