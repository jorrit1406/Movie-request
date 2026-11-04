/* Request Movies — script client léger (aucun secret, aucune logique métier).
 * Affiche ce que le serveur fournit et transmet les actions de l’utilisateur.
 * Deux zones : résultats « Disponible à la demande » (page de recherche) et panneau de statut (fiche). */
(function () {
    'use strict';

    if (window.__requestMoviesLoaded) { return; }
    window.__requestMoviesLoaded = true;

    var BASE = 'RequestMovies';
    var textPage = null;
    var REPORT_CATEGORIES = [
        ['playback', 'Playback error'],
        ['nosound', 'No sound'],
        ['avproblem', 'Audio/video problem'],
        ['subtitles', 'Missing or incorrect subtitles'],
        ['quality', 'Poor quality'],
        ['wrongversion', 'Wrong version'],
        ['wronglanguage', 'Wrong language'],
        ['wrongitem', 'Wrong movie or episode'],
        ['incomplete', 'Incomplete content'],
        ['other', 'Other']
    ];

    // ------------------------------------------------------------------ utilitaires

    function call(method, path, params, body) {
        var opts = { type: method, url: ApiClient.getUrl(BASE + '/' + path, params || undefined), dataType: 'json' };
        if (body) {
            opts.data = JSON.stringify(body);
            opts.contentType = 'application/json';
        }
        return ApiClient.ajax(opts);
    }

    function errorMessage(e) {
        if (e && typeof e.json === 'function') {
            return e.json().then(function (j) {
                return (j && (j.message || j.Message)) || 'Une erreur est survenue.';
            }, function () { return 'Une erreur est survenue.'; });
        }
        return Promise.resolve('An error occurred.');
    }

    function el(tag, attrs, children) {
        var node = document.createElement(tag);
        Object.keys(attrs || {}).forEach(function (k) {
            if (k === 'class') { node.className = attrs[k]; }
            else if (k === 'text') { node.textContent = attrs[k]; }
            else if (k.indexOf('on') === 0) { node.addEventListener(k.slice(2), attrs[k]); }
            else { node.setAttribute(k, attrs[k]); }
        });
        (children || []).forEach(function (c) { if (c) { node.appendChild(c); } });
        return node;
    }

    function debounce(fn, ms) {
        var t = null;
        return function () {
            var args = arguments, self = this;
            clearTimeout(t);
            t = setTimeout(function () { fn.apply(self, args); }, ms);
        };
    }

    function injectStyle() {
        if (document.getElementById('rm-style')) { return; }
        var css = [
            '.rm-section{margin:1.5em 0;padding:0 3.3%}',
            '.rm-section h2{margin:0 0 .6em;font-size:1.3em}',
            '.rm-grid{display:flex;flex-wrap:wrap;gap:1em}',
            '.rm-card{width:200px;display:flex;flex-direction:column;gap:.4em}',
            '.rm-card img,.rm-noposter{width:100%;aspect-ratio:2/3;object-fit:cover;border-radius:.4em;background:rgba(127,127,127,.25)}',
            '.rm-title{font-weight:600}.rm-meta{opacity:.7;font-size:.9em}',
            '.rm-badge{display:inline-block;padding:.15em .6em;border-radius:1em;background:rgba(127,127,127,.3);font-size:.85em}',
            '.rm-badge.available{background:#2e7d32;color:#fff}.rm-badge.failed,.rm-badge.declined{background:#b71c1c;color:#fff}',
            '.rm-badge.importPending,.rm-badge.partial{background:#ef6c00;color:#fff}',
            '.rm-actions{display:flex;flex-wrap:wrap;gap:.5em;align-items:center;margin-top:.3em}',
            '.rm-btn{cursor:pointer;border:0;border-radius:.3em;padding:.5em .9em;font:inherit;background:#00a4dc;color:#fff}',
            '.rm-btn.secondary{background:rgba(127,127,127,.35);color:inherit}',
            '.rm-btn[disabled]{opacity:.5;cursor:default}',
            '.rm-select,.rm-input{font:inherit;padding:.4em;border-radius:.3em;max-width:100%}',
            '.rm-panel{margin:1em 3.3%;padding:.8em 1em;border-radius:.5em;background:rgba(127,127,127,.15)}',
            '.rm-seasons{display:flex;flex-direction:column;gap:.3em;margin:.5em 0}',
            '.rm-note{opacity:.8;font-size:.9em}.rm-msg{margin-top:.4em}.rm-msg.err{color:#ef5350}',
            '.rm-form{display:flex;flex-direction:column;gap:.5em;margin-top:.6em;max-width:28em}',
            '.rm-text-page{position:fixed;inset:0;z-index:10000;overflow:auto;background:var(--theme-body-bg,#101010);padding:2em;box-sizing:border-box}',
            '.rm-text-page-inner{max-width:52em;margin:0 auto}',
            '.rm-text-header{display:flex;justify-content:space-between;align-items:center;gap:1em}',
            '.rm-text-entry{padding:1em;margin:.75em 0;border-radius:.5em;background:rgba(127,127,127,.15)}',
            '.rm-text-entry-title{font-size:1.15em;font-weight:600}',
            '.rm-text-entry-meta{opacity:.7;font-size:.9em;margin-top:.5em}',
            '.rm-text-entry-actions{display:flex;align-items:center;gap:.6em;margin-top:.6em}',
            '.rm-text-vote-count{min-width:5em}',
            '.rm-text-form{max-width:none;padding:1em 0}',
            '.rm-text-nav .navMenuOptionText{display:inline}'
        ].join('');
        document.head.appendChild(el('style', { id: 'rm-style', text: css }));
    }

    function badge(state, label) { return el('span', { class: 'rm-badge ' + state, text: label }); }

    function qualitySelect(qualities, predicate) {
        var select = el('select', { class: 'rm-select', 'aria-label': 'Quality' });
        qualities.forEach(function (q) {
            if (!predicate || predicate(q)) { select.appendChild(el('option', { value: q.id, text: q.label })); }
        });
        return select;
    }

    function setMsg(container, text, isError) {
        var m = container.querySelector('.rm-msg');
        if (!m) { m = el('div', { class: 'rm-msg' }); container.appendChild(m); }
        m.className = 'rm-msg' + (isError ? ' err' : '');
        m.textContent = text;
    }

    function ensureTextPage() {
        if (textPage) { return textPage; }
        textPage = el('div', { class: 'rm-text-page', hidden: '' });
        var inner = el('div', { class: 'rm-text-page-inner' });
        var close = el('button', {
            class: 'rm-btn secondary', type: 'button', text: 'Close',
            onclick: function () { textPage.hidden = true; }
        });
        inner.appendChild(el('div', { class: 'rm-text-header' }, [
            el('h1', { text: 'Shared request list' }),
            close
        ]));
        inner.appendChild(el('p', { class: 'rm-note', text: 'Everyone with a Jellyfin account can view this shared list and add titles.' }));
        var status = el('p', { class: 'rm-note rm-text-status', 'aria-live': 'polite' });

        var form = el('form', { class: 'rm-form rm-text-form' });
        var title = el('input', { class: 'rm-input', type: 'text', maxlength: '120', required: 'required', placeholder: 'Title' });
        var notes = el('textarea', { class: 'rm-input', rows: '3', maxlength: '500', placeholder: 'Notes (optional)' });
        var submit = el('button', { class: 'rm-btn', type: 'submit', text: 'Add to list' });
        var list = el('div', { class: 'rm-text-list', 'aria-live': 'polite' });
        form.appendChild(title);
        form.appendChild(notes);
        form.appendChild(submit);
        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var value = title.value.trim();
            if (!value) { setMsg(form, 'Enter a title.', true); return; }
            submit.disabled = true;
            call('POST', 'TextRequests', null, { title: value, notes: notes.value.trim() }).then(function () {
                title.value = '';
                notes.value = '';
                setMsg(form, 'Added to the list.', false);
                loadTextRequests(list);
            }, function (error) {
                errorMessage(error).then(function (message) { setMsg(form, message, true); });
            }).then(function () { submit.disabled = false; }, function () { submit.disabled = false; });
        });
        inner.appendChild(status);
        inner.appendChild(form);
        inner.appendChild(list);
        textPage.appendChild(inner);
        document.body.appendChild(textPage);
        return textPage;
    }

    function renderTextRequests(list, requests) {
        list.textContent = '';
        if (!requests.length) {
            list.appendChild(el('p', { class: 'rm-note', text: 'The list is empty.' }));
            return;
        }

        var isAdmin = ApiClient.isCurrentUserAdministrator && ApiClient.isCurrentUserAdministrator();
        requests.forEach(function (request) {
            var entry = el('article', { class: 'rm-text-entry' });
            entry.appendChild(el('div', { class: 'rm-text-entry-title', text: request.title }));
            if (request.notes) { entry.appendChild(el('p', { text: request.notes })); }
            var date = new Date(request.createdAtUtc);
            var metadata = request.requestedBy + ' · ' + (isNaN(date.getTime()) ? '' : date.toLocaleString());
            entry.appendChild(el('div', { class: 'rm-text-entry-meta', text: metadata }));
            var actions = el('div', { class: 'rm-text-entry-actions' });
            var voteCount = el('span', {
                class: 'rm-text-vote-count',
                text: request.voteCount + (request.voteCount === 1 ? ' vote' : ' votes')
            });
            var vote = el('button', {
                class: 'rm-btn' + (request.hasVoted ? ' secondary' : ''),
                type: 'button',
                text: request.hasVoted ? 'Remove my vote' : 'Vote',
                'aria-pressed': request.hasVoted ? 'true' : 'false',
                onclick: function () {
                    vote.disabled = true;
                    call('POST', 'TextRequests/' + encodeURIComponent(request.id) + '/Vote').then(function () {
                        loadTextRequests(list);
                    }, function (error) {
                        errorMessage(error).then(function (message) { setMsg(entry, message, true); });
                        vote.disabled = false;
                    });
                }
            });
            actions.appendChild(vote);
            actions.appendChild(voteCount);
            if (isAdmin) {
                actions.appendChild(el('button', {
                    class: 'rm-btn secondary', type: 'button', text: 'Remove',
                    onclick: function () {
                        call('DELETE', 'TextRequests/' + encodeURIComponent(request.id)).then(function () {
                            loadTextRequests(list);
                        }, function (error) {
                            errorMessage(error).then(function (message) { setMsg(entry, message, true); });
                        });
                    }
                }));
            }
            entry.appendChild(actions);
            list.appendChild(entry);
        });
    }

    function loadTextRequests(list) {
        list.textContent = 'Loading list…';
        call('GET', 'TextRequests').then(function (requests) {
            renderTextRequests(list, requests);
        }, function (error) {
            list.textContent = '';
            list.appendChild(el('p', { class: 'rm-note err', text: 'Could not load the list.' }));
            errorMessage(error).then(function (message) {
                list.appendChild(el('p', { class: 'rm-note err', text: message }));
            });
        });
    }

    function showTextList() {
        var page = ensureTextPage();
        page.hidden = false;
        var status = page.querySelector('.rm-text-status');
        var form = page.querySelector('.rm-text-form');
        var list = page.querySelector('.rm-text-list');
        status.hidden = false;
        status.hidden = true;
        form.hidden = false;
        loadTextRequests(list);
    }

    function ensureTextNavigation() {
        var drawer = document.querySelector('#navDrawer .mainDrawer-scrollContainer')
            || document.querySelector('.mainDrawer-scrollContainer')
            || document.querySelector('#navDrawer');
        if (!drawer || drawer.querySelector('[data-rm-nav]')) { return; }
        var link = document.createElement('a');
        link.className = 'navMenuOption lnk rm-text-nav';
        link.href = '#';
        link.dataset.rmNav = 'true';
        link.appendChild(el('span', { class: 'material-icons navMenuOptionIcon', text: 'playlist_add' }));
        link.appendChild(el('span', { class: 'navMenuOptionText', text: 'Request list' }));
        link.addEventListener('click', function (event) {
            event.preventDefault();
            showTextList();
        });
        drawer.appendChild(link);
    }

    // ------------------------------------------------------------------ on-demand search

    var searchSeq = 0;

    function findSearchInput() {
        return document.querySelector('.searchFields input, #searchPage input, input.searchfields-txtSearch, input[type="search"]');
    }

    function onSearchPage() { return /#!?\/search/.test(location.hash); }

    function ensureSearch() {
        if (!onSearchPage()) { return; }
        var input = findSearchInput();
        if (!input || input.__rm) { return; }
        input.__rm = true;
        var run = debounce(function () { runSearch(input); }, 450);
        input.addEventListener('input', run);
        if (input.value && input.value.trim().length >= 2) { runSearch(input); }
    }

    function resultsHost(input) {
        var page = input.closest('.page, [data-role="page"], .view') || document.body;
        var host = page.querySelector('.rm-section[data-rm="search"]');
        if (!host) {
            host = el('div', { class: 'rm-section', 'data-rm': 'search' });
            page.appendChild(host);
        }
        return host;
    }

    function runSearch(input) {
        var q = (input.value || '').trim();
        var host = resultsHost(input);
        var seq = ++searchSeq;
        if (q.length < 2) { host.textContent = ''; return; }
        call('GET', 'Search', { query: q }).then(function (res) {
            if (seq !== searchSeq) { return; }
            renderSearch(host, res);
        }, function () {
            if (seq !== searchSeq) { return; }
            host.textContent = '';
        });
    }

    function renderSearch(host, res) {
        host.textContent = '';
        if (!res.seerrAvailable) {
            host.appendChild(el('p', { class: 'rm-note', text: 'On-demand results are temporarily unavailable.' }));
            return;
        }
        if (!res.results.length) { return; }
        host.appendChild(el('h2', { text: 'Available on demand' }));
        var grid = el('div', { class: 'rm-grid' });
        res.results.forEach(function (item) { grid.appendChild(renderCard(item, res)); });
        host.appendChild(grid);
        if (!res.requestsAllowed && res.blockedReason) {
            host.appendChild(el('p', { class: 'rm-note', text: res.blockedReason }));
        }
    }

    function posterUrl(path) {
        return path ? ApiClient.getUrl(BASE + '/Poster', { path: path }) : null;
    }

    function renderCard(item, ctx) {
        var url = posterUrl(item.poster);
        var meta = [item.mediaType === 'tv' ? 'Series' : 'Movie', item.year].filter(Boolean).join(' · ');
        var card = el('div', { class: 'rm-card' }, [
            url ? el('img', { src: url, alt: '', loading: 'lazy' }) : el('div', { class: 'rm-noposter' }),
            el('div', { class: 'rm-title', text: item.title }),
            el('div', { class: 'rm-meta', text: meta }),
            el('div', {}, [badge(item.state, item.stateLabel)])
        ]);
        if (item.canRequest) {
            var actions = el('div', { class: 'rm-actions' });
            var select = ctx.qualities.length > 1 ? qualitySelect(ctx.qualities) : null;
            if (select) { actions.appendChild(select); }
            var btn = el('button', {
                class: 'rm-btn', type: 'button',
                text: item.mediaType === 'tv' ? 'Choose seasons' : 'Request',
                onclick: function () {
                    if (item.mediaType === 'tv') { openSeasons(card, item, select ? select.value : 'hd'); }
                    else { sendRequest(card, btn, { mediaType: 'movie', tmdbId: item.tmdbId, quality: select ? select.value : 'hd' }); }
                }
            });
            actions.appendChild(btn);
            card.appendChild(actions);
        }
        return card;
    }

    function sendRequest(container, btn, body, onDone) {
        btn.disabled = true;
        call('POST', 'Request', null, body).then(function (r) {
            setMsg(container, r.message || 'Request submitted.', false);
            if (onDone) { onDone(); }
        }, function (e) {
            errorMessage(e).then(function (m) { setMsg(container, m, true); btn.disabled = false; });
        });
    }

    function openSeasons(card, item, quality) {
        var old = card.querySelector('.rm-seasons-box');
        if (old) { old.remove(); return; }
        var box = el('div', { class: 'rm-seasons-box' });
        card.appendChild(box);
        renderSeasonPicker(box, item.mediaType, item.tmdbId, quality);
    }

    // The season picker is also used on series detail pages.
    function renderSeasonPicker(box, mediaType, tmdbId, quality) {
        box.textContent = 'Chargement…';
        call('GET', 'State', { mediaType: mediaType, tmdbId: tmdbId }).then(function (st) {
            box.textContent = '';
            var q = st.qualities.filter(function (x) { return x.id === quality; })[0] || st.qualities[0];
            var list = el('div', { class: 'rm-seasons' });
            var checks = [];
            (q.seasons || []).forEach(function (s) {
                var cb = el('input', { type: 'checkbox', id: 'rm-s-' + tmdbId + '-' + s.number });
                cb.disabled = !s.canRequest;
                cb.checked = s.canRequest;
                checks.push({ cb: cb, n: s.number });
                var extra = s.episodes > 0 ? ' (' + s.episodes + (s.episodeCount ? '/' + s.episodeCount : '') + ' episodes)' : '';
                list.appendChild(el('label', {}, [cb, el('span', { text: ' Season ' + s.number + ' — ' + s.stateLabel + extra })]));
            });
            box.appendChild(list);
            var btn = el('button', {
                class: 'rm-btn', type: 'button', text: 'Request',
                onclick: function () {
                    var seasons = checks.filter(function (c) { return c.cb.checked && !c.cb.disabled; }).map(function (c) { return c.n; });
                    if (!seasons.length) { setMsg(box, 'Select at least one season.', true); return; }
                    sendRequest(box, btn, { mediaType: mediaType, tmdbId: tmdbId, quality: q.id, seasons: seasons });
                }
            });
            if (q.canRequest) { box.appendChild(btn); }
            else { box.appendChild(el('div', { class: 'rm-note', text: st.blockedReason || 'Nothing to request for this quality.' })); }
        }, function () { box.textContent = 'Status unavailable.'; });
    }

    // ------------------------------------------------------------------ details: status, requests, and reports

    function detailsId() {
        var m = /[?&]id=([0-9a-fA-F]+)/.exec(location.hash);
        return /#!?\/details/.test(location.hash) && m ? m[1] : null;
    }

    function ensureDetails() {
        var id = detailsId();
        if (!id) { return; }
        var page = document.querySelector('.itemDetailPage:not(.hide)');
        if (!page || (page.__rmId === id && page.querySelector('.rm-panel'))) { return; }
        page.__rmId = id;
        var userId = ApiClient.getCurrentUserId();
        ApiClient.getItem(userId, id).then(function (item) {
            if (page.__rmId !== id) { return; }
            var type = item.Type;
            var info = { season: null, episode: null };
            if (type === 'Episode') { info.season = item.ParentIndexNumber; info.episode = item.IndexNumber; }
            if (type === 'Season') { info.season = item.IndexNumber; }
            if (type === 'Movie' || type === 'Series') { return { item: item, info: info }; }
            if ((type === 'Episode' || type === 'Season') && item.SeriesId) {
                return ApiClient.getItem(userId, item.SeriesId).then(function (s) { return { item: s, info: info }; });
            }
            return null;
        }).then(function (ctx) {
            if (!ctx || page.__rmId !== id) { return; }
            var tmdb = ctx.item.ProviderIds && (ctx.item.ProviderIds.Tmdb || ctx.item.ProviderIds.tmdb);
            if (!tmdb) { return; }
            var mediaType = ctx.item.Type === 'Series' ? 'tv' : 'movie';
            buildPanel(page, mediaType, parseInt(tmdb, 10), ctx.info);
        }).catch(function () { /* l’interface native reste intacte en cas d’erreur */ });
    }

    function buildPanel(page, mediaType, tmdbId, info) {
        var old = page.querySelector('.rm-panel');
        if (old) { old.remove(); }
        var panel = el('div', { class: 'rm-panel' });
        var host = page.querySelector('.detailPagePrimaryContainer') || page.querySelector('.detailPageContent') || page;
        host.appendChild(panel);
        refreshPanel(panel, mediaType, tmdbId, info);
    }

    function refreshPanel(panel, mediaType, tmdbId, info) {
        call('GET', 'State', { mediaType: mediaType, tmdbId: tmdbId }).then(function (st) {
            panel.textContent = '';
            var head = el('div', { class: 'rm-actions' }, [badge(st.state, st.stateLabel)]);
            st.qualities.slice(1).forEach(function (q) {
                if (q.state !== 'none') { head.appendChild(el('span', { class: 'rm-note', text: q.label + ' : ' + q.stateLabel })); }
            });
            panel.appendChild(head);

            if (!st.seerrAvailable) {
                panel.appendChild(el('div', { class: 'rm-note', text: 'Requests are temporarily unavailable.' }));
            } else if (!st.requestsAllowed && st.blockedReason) {
                panel.appendChild(el('div', { class: 'rm-note', text: st.blockedReason }));
            } else {
                var requestable = st.qualities.filter(function (q) { return q.canRequest; });
                if (requestable.length) { panel.appendChild(requestControls(panel, st, requestable, mediaType, tmdbId, info)); }
            }
            panel.appendChild(reportControls(mediaType, tmdbId, info));
        }, function () { panel.remove(); });
    }

    function requestControls(panel, st, requestable, mediaType, tmdbId, info) {
        var wrap = el('div', {});
        var actions = el('div', { class: 'rm-actions' });
        var select = qualitySelect(st.qualities, function (q) { return q.canRequest; });
        if (requestable.length > 1) { actions.appendChild(select); }
        var btn = el('button', {
            class: 'rm-btn', type: 'button',
            text: mediaType === 'tv' ? 'Choose seasons' : 'Request',
            onclick: function () {
                var quality = requestable.length > 1 ? select.value : requestable[0].id;
                if (mediaType === 'tv') {
                    var box = wrap.querySelector('.rm-seasons-box');
                    if (box) { box.remove(); return; }
                    box = el('div', { class: 'rm-seasons-box' });
                    wrap.appendChild(box);
                    renderSeasonPicker(box, mediaType, tmdbId, quality);
                } else {
                    sendRequest(wrap, btn, { mediaType: mediaType, tmdbId: tmdbId, quality: quality }, function () {
                        setTimeout(function () { refreshPanel(panel, mediaType, tmdbId, info); }, 1200);
                    });
                }
            }
        });
        actions.appendChild(btn);
        wrap.appendChild(actions);
        return wrap;
    }

    function reportControls(mediaType, tmdbId, info) {
        var wrap = el('div', { class: 'rm-report' });
        var toggle = el('button', {
            class: 'rm-btn secondary', type: 'button', text: 'Report a problem',
            onclick: function () {
                var f = wrap.querySelector('.rm-form');
                if (f) { f.remove(); return; }
                wrap.appendChild(reportForm(mediaType, tmdbId, info));
            }
        });
        wrap.style.marginTop = '.8em';
        wrap.appendChild(toggle);
        return wrap;
    }

    function reportForm(mediaType, tmdbId, info) {
        var form = el('div', { class: 'rm-form' });
        var cat = el('select', { class: 'rm-select', 'aria-label': 'Problem type' });
        REPORT_CATEGORIES.forEach(function (c) { cat.appendChild(el('option', { value: c[0], text: c[1] })); });
        form.appendChild(cat);

        var season = null, episode = null;
        if (mediaType === 'tv') {
            season = el('input', { class: 'rm-input', type: 'number', min: '1', placeholder: 'Season (optional)' });
            episode = el('input', { class: 'rm-input', type: 'number', min: '1', placeholder: 'Episode (optional)' });
            if (info && info.season) { season.value = info.season; }
            if (info && info.episode) { episode.value = info.episode; }
            form.appendChild(season);
            form.appendChild(episode);
        }

        var comment = el('textarea', { class: 'rm-input', rows: '3', maxlength: '1000', placeholder: 'Comment (optional)' });
        form.appendChild(comment);
        var send = el('button', {
            class: 'rm-btn', type: 'button', text: 'Submit',
            onclick: function () {
                send.disabled = true;
                call('POST', 'Report', null, {
                    mediaType: mediaType,
                    tmdbId: tmdbId,
                    category: cat.value,
                    season: season && season.value ? parseInt(season.value, 10) : null,
                    episode: episode && episode.value ? parseInt(episode.value, 10) : null,
                    comment: comment.value
                }).then(function (r) { setMsg(form, r.message, false); }, function (e) {
                    errorMessage(e).then(function (m) { setMsg(form, m, true); send.disabled = false; });
                });
            }
        });
        form.appendChild(send);
        return form;
    }

    // ------------------------------------------------------------------ démarrage

    var tick = debounce(function () {
        if (!window.ApiClient || !ApiClient.accessToken || !ApiClient.accessToken()) { return; }
        try {
            ensureTextNavigation();
            ensureSearch();
            ensureDetails();
        } catch (e) { /* ne jamais casser l’interface native */ }
    }, 250);

    function start() {
        injectStyle();
        new MutationObserver(tick).observe(document.body, { childList: true, subtree: true });
        window.addEventListener('hashchange', tick);
        document.addEventListener('viewshow', tick);
        tick();
    }

    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', start); }
    else { start(); }
})();
