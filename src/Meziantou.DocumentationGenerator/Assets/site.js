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
        var collapseButton = document.querySelector('.sidebar-collapse-button');
        var sidebar = document.getElementById('sidebar');
        if (!button || !collapseButton || !sidebar) return;
        var layout = document.querySelector('.site-body');
        var narrowScreen = window.matchMedia('(max-width: 56rem)');
        var collapsed = readStorage('docgen-sidebar-collapsed') === 'true';

        function updateSidebar() {
            layout.classList.toggle('sidebar-collapsed', collapsed);
            collapseButton.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
            var label = collapsed ? 'Expand table of contents' : 'Collapse table of contents';
            collapseButton.setAttribute('aria-label', label);
            collapseButton.title = label;
            button.setAttribute('aria-expanded', sidebar.classList.contains('is-open') ? 'true' : 'false');
            revealCurrentPage();
        }

        button.hidden = false;
        collapseButton.hidden = false;
        updateSidebar();
        collapseButton.addEventListener('click', function () {
            collapsed = !collapsed;
            writeStorage('docgen-sidebar-collapsed', collapsed ? 'true' : null);
            updateSidebar();
        });
        button.addEventListener('click', function () {
            var open = sidebar.classList.toggle('is-open');
            updateSidebar();
            if (open) {
                var current = sidebar.querySelector('[aria-current=page]') || sidebar.querySelector('a');
                if (current) current.focus();
            }
        });
        narrowScreen.addEventListener('change', updateSidebar);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && narrowScreen.matches && sidebar.classList.contains('is-open')) {
                sidebar.classList.remove('is-open');
                button.setAttribute('aria-expanded', 'false');
                button.focus();
            }
        });
    }

    // Scrolls the sidebar, never the page, so the current page is visible in the navigation
    function revealCurrentPage() {
        var sidebar = document.getElementById('sidebar');
        if (!sidebar || !sidebar.clientHeight || sidebar.scrollHeight <= sidebar.clientHeight) return;
        var current = sidebar.querySelector('[aria-current=page]');
        if (!current) return;
        var bounds = sidebar.getBoundingClientRect();
        var top = Math.max(bounds.top, 0);
        var bottom = Math.min(bounds.bottom, window.innerHeight);
        var rect = current.getBoundingClientRect();
        if (rect.top >= top && rect.bottom <= bottom) return;
        sidebar.scrollTop += rect.top - top - (bottom - top - rect.height) / 2;
    }

    // Pages only render the open sections of the sidebar. The children of a closed section are loaded from the navigation
    // index when it is expanded, using the same markup as the rendered sections. Without JavaScript, the toggles of closed
    // sections are hidden and their links lead to the first page of the section, where it is open.
    function initNavigation() {
        var sidebar = document.getElementById('sidebar');
        var indexUrl = sidebar && sidebar.getAttribute('data-nav-index');
        if (!indexUrl) return;
        var basePath = sidebar.getAttribute('data-base-path') || './';
        var loading = null;

        function load() {
            if (!loading) {
                loading = fetch(indexUrl, { credentials: 'same-origin' })
                    .then(function (response) { if (!response.ok) throw new Error('HTTP ' + response.status); return response.json(); })
                    .catch(function (error) { loading = null; throw error; });
            }

            return loading;
        }

        function createLink(title, url, flags) {
            if (url === null) {
                var span = document.createElement('span');
                span.className = 'nav-group';
                span.textContent = title;
                return span;
            }

            var link = document.createElement('a');
            link.textContent = title;
            if (flags & 2) {
                link.href = url;
                link.className = 'nav-external';
                link.rel = 'noopener';
            } else {
                link.href = basePath + url;
                if (flags & 1) link.className = 'nav-group';
            }

            return link;
        }

        function createList(index, id) {
            var list = document.createElement('ul');
            var children = index.nodes[id] || [];
            for (var i = 0; i < children.length; i++) {
                var child = children[i];
                var item = document.createElement('li');
                item.appendChild(createLink(child[0], child[1], child[2]));
                if (child.length > 3) {
                    item.className = child[2] & 4 ? 'nav-node nav-api-reference' : 'nav-node';
                    var details = document.createElement('details');
                    details.setAttribute('data-nav-id', child[3]);
                    var summary = document.createElement('summary');
                    summary.className = 'nav-toggle';
                    summary.setAttribute('aria-label', child[0] + ' section');
                    details.appendChild(summary);
                    item.appendChild(details);
                }

                list.appendChild(item);
            }

            return list;
        }

        // The toggle event does not bubble; it is handled during the capture phase for every section
        sidebar.addEventListener('toggle', function (e) {
            var details = e.target;
            if (!details.open || !details.hasAttribute('data-nav-id') || details.querySelector(':scope > ul')) return;
            details.setAttribute('aria-busy', 'true');
            load().then(function (index) {
                if (!details.querySelector(':scope > ul')) details.appendChild(createList(index, Number(details.getAttribute('data-nav-id'))));
            }, function () {
                details.open = false;
                announce('The table of contents cannot be loaded.');
            }).then(function () {
                details.removeAttribute('aria-busy');
            });
        }, true);

        // Start loading the index when the sidebar is about to be used, so sections usually expand without delay
        function preload() { load().catch(function () { }); }
        sidebar.addEventListener('pointerenter', preload, { once: true });
        sidebar.addEventListener('focusin', preload, { once: true });
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

    // Dependency graphs: selecting a package highlights what it depends on and what depends on it, directly or not
    function initDependencyGraphs() {
        var figures = document.querySelectorAll('.dependency-graph');
        for (var i = 0; i < figures.length; i++) initDependencyGraph(figures[i]);
    }

    function initDependencyGraph(figure) {
        var svg = figure.querySelector('svg');
        var viewport = figure.querySelector('.dependency-graph-viewport');
        if (!svg || !viewport) return;

        // The SVG comes from Meziantou.Framework.Diagrams: each package is a g.dependency-graph-node (data-id="n<index>")
        // in a link, and each arrow is a g.dependency-graph-edge with a class dependency-graph-edge-<from>-<to>
        var nodes = [], names = [];
        var nodeElements = figure.querySelectorAll('.dependency-graph-node');
        for (var n = 0; n < nodeElements.length; n++) {
            var nodeIndex = parseInt((nodeElements[n].getAttribute('data-id') || '').slice(1), 10);
            if (isNaN(nodeIndex)) continue;
            nodes[nodeIndex] = nodeElements[n];
            var label = nodeElements[n].querySelector('text');
            names[nodeIndex] = label ? label.textContent : '';
            nodeElements[n].setAttribute('data-name', names[nodeIndex]);
        }

        var dependencies = [], dependents = [];
        for (var i = 0; i < nodes.length; i++) { dependencies.push([]); dependents.push([]); }

        var edges = [];
        var edgeElements = figure.querySelectorAll('.dependency-graph-edge');
        for (var e = 0; e < edgeElements.length; e++) {
            var match = /(?:^|\s)dependency-graph-edge-(\d+)-(\d+)(?:\s|$)/.exec(edgeElements[e].getAttribute('class') || '');
            if (!match) continue;
            var from = parseInt(match[1], 10), to = parseInt(match[2], 10);
            if (!nodes[from] || !nodes[to]) continue;
            edges.push({ element: edgeElements[e], from: from, to: to });
            dependencies[from].push(to);
            dependents[to].push(from);
        }

        var filter = figure.querySelector('.dependency-graph-filter');
        var naturalWidth = parseFloat(svg.getAttribute('width'));
        var naturalHeight = parseFloat(svg.getAttribute('height'));
        var scale = 1;
        var selected = -1;

        function closure(start, adjacency) {
            var seen = {}, queue = adjacency[start].slice();
            while (queue.length) {
                var next = queue.shift();
                if (seen[next] || next === start) continue;
                seen[next] = true;
                queue.push.apply(queue, adjacency[next]);
            }
            return seen;
        }

        function select(index) {
            selected = index;
            figure.classList.toggle('has-selection', index >= 0);
            var down = index >= 0 ? closure(index, dependencies) : {};
            var up = index >= 0 ? closure(index, dependents) : {};
            for (var i = 0; i < nodes.length; i++) {
                if (!nodes[i]) continue;
                nodes[i].classList.toggle('is-selected', i === index);
                nodes[i].classList.toggle('is-dependency', !!down[i]);
                nodes[i].classList.toggle('is-dependent', !!up[i]);
            }
            for (var j = 0; j < edges.length; j++) {
                var edge = edges[j];
                edge.element.classList.toggle('is-dependency', index >= 0 && (edge.from === index || !!down[edge.from]) && !!down[edge.to]);
                edge.element.classList.toggle('is-dependent', index >= 0 && (edge.to === index || !!up[edge.to]) && !!up[edge.from]);
            }
            if (index >= 0) {
                announce(names[index] + ': ' + Object.keys(down).length + ' dependencies, ' + Object.keys(up).length + ' dependents. Select it again to open its page.');
            }
        }

        // The node of a click target: the drawing of a package, or its link (keyboard activation targets the link)
        function getNode(target) {
            if (!target || !target.closest || !svg.contains(target)) return null;
            var node = target.closest('.dependency-graph-node');
            if (node) return node;
            var link = target.closest('a');
            return link ? link.querySelector('.dependency-graph-node') : null;
        }

        function getIndex(node) {
            return parseInt((node.getAttribute('data-id') || '').slice(1), 10);
        }

        // The first click selects a package, a second click opens its page; a modified click always opens it
        figure.addEventListener('click', function (event) {
            var node = getNode(event.target);
            if (!node) {
                if (event.target.closest && event.target.closest('svg')) select(-1);
                return;
            }
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
            var index = getIndex(node);
            if (index === selected) return;
            event.preventDefault();
            select(index);
        });

        // Escape clears the selection when the focus is in the graph or on nothing else
        document.addEventListener('keydown', function (event) {
            if (event.key !== 'Escape' || selected < 0) return;
            var active = document.activeElement;
            if (active && active !== document.body && !figure.contains(active)) return;
            select(-1);
        });

        if (filter) {
            filter.addEventListener('input', function () {
                var text = filter.value.trim().toLowerCase();
                figure.classList.toggle('has-filter', text.length > 0);
                for (var i = 0; i < nodes.length; i++) {
                    if (nodes[i]) nodes[i].classList.toggle('is-match', text.length > 0 && names[i].toLowerCase().indexOf(text) >= 0);
                }
            });
        }

        // Zoom changes the size of the drawing; the viewport scrolls, and dragging the background scrolls it too
        function zoom(value, centerX, centerY) {
            var previous = scale;
            scale = Math.min(3, Math.max(0.2, value));
            if (centerX === undefined) { centerX = viewport.clientWidth / 2; centerY = viewport.clientHeight / 2; }
            var x = (viewport.scrollLeft + centerX) / previous, y = (viewport.scrollTop + centerY) / previous;
            svg.setAttribute('width', String(naturalWidth * scale));
            svg.setAttribute('height', String(naturalHeight * scale));
            viewport.scrollLeft = x * scale - centerX;
            viewport.scrollTop = y * scale - centerY;
        }

        function bind(selector, handler) {
            var button = figure.querySelector(selector);
            if (button) button.addEventListener('click', handler);
        }

        bind('.dependency-graph-zoom-in', function () { zoom(scale * 1.25); });
        bind('.dependency-graph-zoom-out', function () { zoom(scale / 1.25); });
        bind('.dependency-graph-fit', function () {
            var width = viewport.clientWidth - 24, height = viewport.clientHeight - 24;
            if (width <= 0 || height <= 0) return;
            zoom(Math.min(width / naturalWidth, height / naturalHeight), 0, 0);
            viewport.scrollLeft = 0;
            viewport.scrollTop = 0;
        });
        bind('.dependency-graph-reset', function () {
            zoom(1);
            viewport.scrollLeft = 0;
            viewport.scrollTop = 0;
            if (filter) { filter.value = ''; filter.dispatchEvent(new Event('input')); }
            select(-1);
        });

        viewport.addEventListener('wheel', function (event) {
            if (!event.ctrlKey && !event.metaKey) return;
            event.preventDefault();
            var rect = viewport.getBoundingClientRect();
            zoom(scale * (event.deltaY < 0 ? 1.1 : 1 / 1.1), event.clientX - rect.left, event.clientY - rect.top);
        }, { passive: false });

        // + and - zoom, 0 restores the actual size, when the focus is on the graph
        viewport.addEventListener('keydown', function (event) {
            if (event.ctrlKey || event.metaKey || event.altKey) return;
            if (event.key === '+' || event.key === '=') zoom(scale * 1.25);
            else if (event.key === '-' || event.key === '_') zoom(scale / 1.25);
            else if (event.key === '0') zoom(1);
            else return;
            event.preventDefault();
        });

        var drag = null;
        viewport.addEventListener('pointerdown', function (event) {
            if (event.button !== 0 || event.pointerType === 'touch' || getNode(event.target)) return;
            drag = { x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop, moved: false };
        });
        viewport.addEventListener('pointermove', function (event) {
            if (!drag) return;
            var dx = event.clientX - drag.x, dy = event.clientY - drag.y;
            if (!drag.moved && Math.abs(dx) + Math.abs(dy) < 4) return;
            if (!drag.moved) { drag.moved = true; viewport.setPointerCapture(event.pointerId); figure.classList.add('is-panning'); }
            viewport.scrollLeft = drag.left - dx;
            viewport.scrollTop = drag.top - dy;
        });
        function endDrag(event) {
            if (drag && drag.moved) {
                figure.classList.remove('is-panning');
                // A drag is not a click on the background, which would clear the selection
                viewport.addEventListener('click', function (e) { e.stopPropagation(); }, { capture: true, once: true });
                if (viewport.hasPointerCapture && viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
            }
            drag = null;
        }
        viewport.addEventListener('pointerup', endDrag);
        viewport.addEventListener('pointercancel', endDrag);

        // A graph wider than the page is shrunk a little, then scrolled to its top-left package rather than to empty space
        function fit() {
            var available = viewport.clientWidth - 24;
            if (available <= 0 || naturalWidth <= available) return 1;
            zoom(Math.max(0.6, available / naturalWidth), 0, 0);
            var first = null, best = Infinity;
            for (var i = 0; i < nodes.length; i++) {
                if (!nodes[i]) continue;
                var box = nodes[i].getBBox();
                if (box.x + box.y < best) { best = box.x + box.y; first = box; }
            }
            if (first) {
                viewport.scrollLeft = Math.max(0, first.x * scale - 12);
                viewport.scrollTop = Math.max(0, first.y * scale - 12);
            }
            return scale;
        }

        var initialScale = fit();
        var resetButton = figure.querySelector('.dependency-graph-reset');
        if (resetButton) {
            resetButton.addEventListener('click', function () {
                if (initialScale !== 1) { viewport.scrollLeft = 0; viewport.scrollTop = 0; scale = 1; fit(); }
            });
        }

        var toolbar = figure.querySelector('.dependency-graph-toolbar');
        if (toolbar) toolbar.hidden = false;
        var hint = figure.querySelector('.dependency-graph-hint');
        if (hint) hint.hidden = false;
        figure.classList.add('is-interactive');
    }

    function init() {
        initScrollableRegions();
        initTheme();
        initMenu();
        initNavigation();
        revealCurrentPage();
        initCopyButtons();
        initPermalinks();
        initFrameworkFilter();
        initActiveHeading();
        initDependencyGraphs();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
