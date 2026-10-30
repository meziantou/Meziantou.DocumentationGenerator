// Client-side search over the static index generated with the site. Nothing is sent to a server.
(function (global) {
    'use strict';

    var MAX_RESULTS = 20;
    var KIND_PRIORITY = { 'class': 0, 'struct': 0, 'interface': 0, 'enum': 0, 'delegate': 0, 'package': 1, 'namespace': 2, 'article': 3 };

    function normalize(value) {
        return (value || '').toLowerCase().replace(/\s+/g, ' ').trim();
    }

    // "Tree<T>.Node" and "List{T}" become "tree.node" and "list"
    function stripGenerics(value) {
        var result = '';
        var depth = 0;
        for (var i = 0; i < value.length; i++) {
            var c = value[i];
            if (c === '<' || c === '{' || c === '[' || c === '(') { depth++; continue; }
            if (c === '>' || c === '}' || c === ']' || c === ')') { if (depth > 0) depth--; continue; }
            if (depth === 0) result += c;
        }

        return result;
    }

    // "HttpClientHandler.SendAsync" becomes ["http", "client", "handler", "send", "async"]
    function tokenize(value) {
        var tokens = [];
        var parts = stripGenerics(value || '').split(/[^A-Za-z0-9_]+/);
        for (var i = 0; i < parts.length; i++) {
            var words = parts[i].match(/[A-Z]+(?![a-z])|[A-Z]?[a-z0-9_]+|[A-Z]/g);
            if (!words) continue;
            for (var j = 0; j < words.length; j++) tokens.push(words[j].toLowerCase());
        }

        return tokens;
    }

    function acronym(value) {
        var parts = stripGenerics(value || '').split('.');
        var last = parts[parts.length - 1];
        return tokenize(last).map(function (t) { return t[0]; }).join('');
    }

    // Damerau-Levenshtein distance limited to small values
    function distance(a, b, max) {
        if (Math.abs(a.length - b.length) > max) return max + 1;
        var previous2 = null;
        var previous = [];
        for (var j = 0; j <= b.length; j++) previous.push(j);
        for (var i = 1; i <= a.length; i++) {
            var current = [i];
            var rowMin = i;
            for (j = 1; j <= b.length; j++) {
                var cost = a[i - 1] === b[j - 1] ? 0 : 1;
                var value = Math.min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + cost);
                if (previous2 && i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) value = Math.min(value, previous2[j - 2] + 1);
                current.push(value);
                if (value < rowMin) rowMin = value;
            }

            if (rowMin > max) return max + 1;
            previous2 = previous;
            previous = current;
        }

        return previous[b.length];
    }

    function prepare(index) {
        return index.items.map(function (item, position) {
            var title = item[0];
            var qualified = item[4] || '';
            var name = normalize(stripGenerics(title));
            return {
                title: title,
                url: item[1],
                kind: item[2],
                package: item[3] >= 0 ? index.packages[item[3]] : null,
                qualifiedName: qualified,
                targetFrameworks: item[5] || '',
                position: position,
                titleKey: normalize(title),
                nameKey: name,
                fullKey: normalize(stripGenerics(qualified)) || name,
                segments: name.split('.'),
                tokens: tokenize(qualified || title),
                acronym: acronym(title)
            };
        });
    }

    // Scores: exact title 100, exact qualified name 95, qualified name suffix 90, exact member name 85, prefix 80,
    // exact containing type name 70, every query token prefixes a name token 60, acronym 50, substring 40, small typo 20
    function score(entry, query, queryTokens) {
        var q = normalize(stripGenerics(query));
        if (!q) return 0;
        if (entry.titleKey === normalize(query) || entry.nameKey === q) return 100;
        if (entry.fullKey === q) return 95;
        if (q.indexOf('.') > 0 && entry.fullKey.length > q.length && entry.fullKey.slice(-q.length - 1) === '.' + q) return 90;
        if (entry.segments[entry.segments.length - 1] === q) return 85;
        if (entry.nameKey.indexOf(q) === 0) return 80;
        if (entry.segments.indexOf(q) >= 0) return 70;
        if (queryTokens.length > 0 && queryTokens.every(function (qt) { return entry.tokens.some(function (t) { return t.indexOf(qt) === 0; }); })) return 60;
        if (q.length >= 2 && q.indexOf(' ') < 0 && q.indexOf('.') < 0 && entry.acronym.indexOf(q) === 0) return 50;
        if (entry.fullKey.indexOf(q) >= 0 || entry.nameKey.indexOf(q) >= 0) return 40;
        if (q.length >= 4 && q.indexOf(' ') < 0) {
            var max = q.length >= 8 ? 2 : 1;
            var last = entry.segments[entry.segments.length - 1];
            if (distance(q, last, max) <= max) return 20;
        }

        return 0;
    }

    function compare(a, b) {
        if (a.score !== b.score) return b.score - a.score;
        var ka = KIND_PRIORITY.hasOwnProperty(a.entry.kind) ? KIND_PRIORITY[a.entry.kind] : 4;
        var kb = KIND_PRIORITY.hasOwnProperty(b.entry.kind) ? KIND_PRIORITY[b.entry.kind] : 4;
        if (ka !== kb) return ka - kb;
        if (a.entry.title.length !== b.entry.title.length) return a.entry.title.length - b.entry.title.length;
        if (a.entry.title !== b.entry.title) return a.entry.title < b.entry.title ? -1 : 1;
        return a.entry.url < b.entry.url ? -1 : a.entry.url > b.entry.url ? 1 : 0;
    }

    function createEngine(index) {
        if (!index || index.version !== 2 || !Array.isArray(index.items)) throw new Error('Invalid search index');
        var entries = prepare(index);
        return {
            count: entries.length,
            search: function (query, limit) {
                var queryTokens = tokenize(query);
                var matches = [];
                for (var i = 0; i < entries.length; i++) {
                    var s = score(entries[i], query, queryTokens);
                    if (s > 0) matches.push({ entry: entries[i], score: s });
                }

                matches.sort(compare);
                return { total: matches.length, results: matches.slice(0, limit || MAX_RESULTS).map(function (m) { return Object.assign({ score: m.score }, m.entry); }) };
            }
        };
    }

    global.DocgenSearch = { createEngine: createEngine, tokenize: tokenize, distance: distance };

    if (typeof document === 'undefined') return;

    function init() {
        var root = document.querySelector('[data-search]');
        if (!root) return;
        var input = root.querySelector('input[type=search]');
        var list = root.querySelector('[role=listbox]');
        var status = root.querySelector('[role=status]');
        var basePath = root.getAttribute('data-base-path') || './';
        var indexUrl = root.getAttribute('data-index');
        var engine = null;
        var loading = null;
        var active = -1;
        var timer = 0;
        root.hidden = false;

        function setStatus(text) { status.textContent = text; }

        function load() {
            if (!loading) {
                setStatus('Loading the search index…');
                loading = fetch(indexUrl, { credentials: 'same-origin' })
                    .then(function (response) { if (!response.ok) throw new Error('HTTP ' + response.status); return response.json(); })
                    .then(function (index) { engine = createEngine(index); return engine; })
                    .catch(function (error) { loading = null; throw error; });
            }

            return loading;
        }

        function close() {
            list.hidden = true;
            list.textContent = '';
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            active = -1;
        }

        function setActive(position) {
            var options = list.querySelectorAll('[role=option]');
            if (!options.length) return;
            active = (position + options.length) % options.length;
            for (var i = 0; i < options.length; i++) options[i].setAttribute('aria-selected', i === active ? 'true' : 'false');
            input.setAttribute('aria-activedescendant', options[active].id);
            options[active].scrollIntoView({ block: 'nearest' });
        }

        function render(query) {
            var result = engine.search(query, MAX_RESULTS);
            list.textContent = '';
            active = -1;
            input.removeAttribute('aria-activedescendant');
            result.results.forEach(function (item, i) {
                var option = document.createElement('li');
                option.id = 'search-result-' + i;
                option.setAttribute('role', 'option');
                option.setAttribute('aria-selected', 'false');
                var link = document.createElement('a');
                link.href = basePath + item.url;
                link.tabIndex = -1;
                var title = document.createElement('span');
                title.className = 'search-title';
                title.textContent = item.title;
                var context = document.createElement('span');
                context.className = 'search-context';
                context.textContent = [item.kind, item.package, item.targetFrameworks].filter(Boolean).join(' · ');
                link.appendChild(title);
                link.appendChild(context);
                option.appendChild(link);
                option.addEventListener('mousedown', function (e) { e.preventDefault(); });
                list.appendChild(option);
            });
            list.hidden = result.results.length === 0;
            input.setAttribute('aria-expanded', result.results.length ? 'true' : 'false');
            if (result.total === 0) setStatus('No results for “' + query + '”.');
            else if (result.total > result.results.length) setStatus(result.total + ' results, showing the first ' + result.results.length + '.');
            else setStatus(result.total + (result.total === 1 ? ' result.' : ' results.'));
        }

        function update() {
            var query = input.value.trim();
            if (!query) { close(); setStatus(''); return; }
            load().then(function () { if (input.value.trim() === query) render(query); })
                .catch(function () { close(); setStatus('The search index cannot be loaded. Use the navigation instead.'); });
        }

        input.addEventListener('focus', function () { load().then(function () { if (!input.value) setStatus(''); }, function () { setStatus('The search index cannot be loaded. Use the navigation instead.'); }); });
        input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(update, 100); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(active + 1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(active - 1); }
            else if (e.key === 'Enter') {
                var options = list.querySelectorAll('[role=option] a');
                var target = options[active >= 0 ? active : 0];
                if (target) { e.preventDefault(); window.location.href = target.href; }
            }
            else if (e.key === 'Escape') { input.value = ''; close(); setStatus(''); }
        });
        input.addEventListener('blur', function () { setTimeout(close, 150); });
        root.querySelector('form').addEventListener('submit', function (e) { e.preventDefault(); });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})(typeof globalThis !== 'undefined' ? globalThis : this);
