(function (root, factory) {
    'use strict';
    if (typeof module === 'object' && module.exports) module.exports = factory(null);
    else if (!root.MetaTubeMetadata) {
        root.MetaTubeMetadata = factory(root);
        root.MetaTubeMetadata.start();
    }
}(typeof window !== 'undefined' ? window : null, function (win) {
    'use strict';
    const pageSize = 10;
    function routeQuery(location) {
        const route = location.hash ? location.hash.replace(/^#!?/, '') : (location.pathname || '');
        return new URLSearchParams(route.includes('?') ? route.slice(route.indexOf('?') + 1) : (location.search || ''));
    }
    function matchesServer(location, serverId) {
        const requested = routeQuery(location).get('serverId');
        return !requested || requested === serverId;
    }
    function itemIdFromLocation(location) {
        const route = location.hash ? location.hash.replace(/^#!?/, '') : (location.pathname || '');
        if (!/^\/?(?:details|itemdetails)(?:\?|\/|$)/i.test(route)) return null;
        const query = routeQuery(location);
        const id = query.get('id') || query.get('Id');
        return id && /^(?:[a-f\d]{32}|[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12})$/i.test(id) ? id.toLowerCase() : null;
    }
    function formatRating(rating) {
        return rating && Number.isFinite(rating.value) && Number.isFinite(rating.scale_max) &&
            rating.scale_max > 0 && rating.value >= 0 && rating.value <= rating.scale_max
            ? Number(rating.value.toFixed(1)) + ' / ' + rating.scale_max : null;
    }
    function mergeReviews(first, next) {
        const seen = new Set();
        return [...first, ...next].filter(review => {
            if (!review || typeof review.comment !== 'string') return false;
            const key = JSON.stringify([review.source || '', review.id || [review.author, review.date, review.comment]]);
            if (seen.has(key)) return false;
            seen.add(key);
            return true;
        });
    }
    if (!win) return { itemIdFromLocation, matchesServer, formatRating, mergeReviews };

    const doc = win.document;
    const script = doc.currentScript;
    const basePath = script ? new URL(script.src, win.location.href).pathname.split('/MetaTube/')[0] : '';
    const zh = (doc.documentElement.lang || win.navigator.language || '').toLowerCase().startsWith('zh');
    const labels = zh ? {
        heading: '来源评价', reviews: '评论', refresh: '刷新评论', more: '更多评论', retry: '重试',
        loading: '正在读取评价…', empty: '暂无已公开的评论', unsupported: '此来源暂不提供评论',
        unavailable: '评论暂时不可用，已保留先前读取的资料', metadataUnavailable: '评分资料暂时不可用', error: '暂时无法读取评价', votes: '人评分',
        comments: '条评论', loaded: '已加载', cached: '已保存', source: '来源', anonymous: '匿名', helpful: '人觉得有用', likes: '人点赞'
    } : {
        heading: 'Source ratings', reviews: 'Reviews', refresh: 'Refresh reviews', more: 'More reviews', retry: 'Retry',
        loading: 'Loading ratings and reviews…', empty: 'No public reviews available', unsupported: 'Reviews are not supported by this source',
        unavailable: 'Reviews are temporarily unavailable; previously loaded data is preserved', metadataUnavailable: 'Rating metadata is temporarily unavailable', error: 'Ratings are temporarily unavailable',
        votes: 'ratings', comments: 'reviews', loaded: 'Loaded', cached: 'Saved', source: 'Source', anonymous: 'Anonymous', helpful: 'found this helpful', likes: 'likes'
    };
    const javdbMessages = zh ? {
        not_found: 'JAVDB 未找到唯一匹配条目，或后端尚未启用此来源。',
        rate_limited: 'JAVDB 已限流，后端已暂停访问；先前读取的评论会保留。',
        blocked: 'JAVDB 拒绝访问，后端已暂停请求。',
        authentication_required: 'JAVDB 需要有效登录，请检查后端 CLI 会话。',
        number_unavailable: '缺少可核实的番号，未请求 JAVDB。',
        unavailable: 'JAVDB 暂时不可用，先前读取的资料会保留。'
    } : {
        not_found: 'No unique JAVDB match was found, or the backend provider is disabled.',
        rate_limited: 'JAVDB has paused requests after rate limiting; previously loaded reviews are preserved.',
        blocked: 'JAVDB refused access; backend requests are paused.',
        authentication_required: 'JAVDB requires a valid session. Check the backend CLI login.',
        number_unavailable: 'No verified movie number is available; JAVDB was not requested.',
        unavailable: 'JAVDB is temporarily unavailable; previously loaded data is preserved.'
    };
    let state = null;
    let scheduled = false;
    function element(tag, className, text) {
        const node = doc.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = String(text);
        return node;
    }
    function button(text, action) {
        const node = element('button', 'mt-action', text);
        node.type = 'button';
        node.addEventListener('click', action);
        return node;
    }
    function currentClient() { return win.ApiClient; }
    function currentToken() {
        try { return currentClient()?.accessToken?.() || ''; } catch (_) { return ''; }
    }
    function visibleDetails() {
        return Array.from(doc.querySelectorAll('.itemDetailPage')).find(node =>
            !node.closest('.hide, [hidden]') && node.getClientRects().length > 0);
    }
    function mount(current) {
        const details = visibleDetails();
        if (!details) return false;
        const target = details.querySelector('.itemDetailsGroup') || details.querySelector('.detailSection');
        if (!target) return false;
        if (current.panel?.isConnected && details.contains(current.panel) && target.parentNode === current.panel.parentNode) return true;
        current.panel?.remove();
        current.panel = element('section', 'mt-metadata');
        current.panel.dataset.metatubePanel = current.id;
        current.panel.setAttribute('aria-label', labels.heading);
        target.insertAdjacentElement('afterend', current.panel);
        render(current);
        return true;
    }
    function message(text) {
        const node = element('p', 'mt-message', text);
        node.setAttribute('role', 'status');
        return node;
    }
    function countText(count, suffix) {
        return Number.isSafeInteger(count) && count >= 0 ? count.toLocaleString() + ' ' + suffix : null;
    }
    function render(current) {
        const panel = current.panel;
        if (!panel?.isConnected || current !== state) return;
        const header = element('header', 'mt-header');
        header.append(element('h2', 'mt-title', labels.heading));
        if (current.data) {
            const refresh = button(labels.refresh, () => load(current, 0, true));
            refresh.disabled = current.loading;
            header.append(refresh);
        }
        panel.replaceChildren(header);
        panel.setAttribute('aria-busy', String(current.loading));
        const data = current.data;
        if (data) {
            if (data.metadata_status === 'unavailable') panel.append(message(labels.metadataUnavailable));
            if (javdbMessages[data.javdb_status]) panel.append(message(javdbMessages[data.javdb_status]));
            const ratings = element('div', 'mt-rating-grid');
            for (const rating of Array.isArray(data.ratings) ? data.ratings : []) {
                const value = formatRating(rating);
                if (!value) continue;
                const card = element('article', 'mt-rating');
                card.append(element('div', 'mt-source', rating.source || data.provider || labels.source));
                card.append(element('strong', 'mt-score', value));
                const counts = [countText(rating.vote_count, labels.votes), countText(rating.review_count, labels.comments)].filter(Boolean);
                if (counts.length) card.append(element('div', 'mt-counts', counts.join(' · ')));
                const buckets = (Array.isArray(rating.distribution) ? rating.distribution : []).filter(bucket =>
                    bucket && Number.isFinite(bucket.value) && Number.isSafeInteger(bucket.count) && bucket.count >= 0);
                const max = Math.max(1, ...buckets.map(bucket => bucket.count));
                if (buckets.length) {
                    const distribution = element('div', 'mt-distribution');
                    for (const bucket of buckets) {
                        const row = element('div', 'mt-bucket');
                        const meter = element('meter');
                        meter.min = 0; meter.max = max; meter.value = bucket.count;
                        meter.setAttribute('aria-label', bucket.value + ': ' + bucket.count);
                        row.append(element('span', '', bucket.value), meter, element('span', '', bucket.count.toLocaleString()));
                        distribution.append(row);
                    }
                    card.append(distribution);
                }
                ratings.append(card);
            }
            if (ratings.childNodes.length) panel.append(ratings);
            const tags = element('div', 'mt-tags');
            const seenTags = new Set();
            for (const tag of Array.isArray(data.tags) ? data.tags : []) {
                if (!tag || typeof tag.name !== 'string' || !tag.name.trim()) continue;
                const key = tag.name.trim().toLowerCase();
                if (seenTags.has(key)) continue;
                seenTags.add(key);
                const chip = element('span', 'mt-tag', tag.name.trim());
                chip.title = [tag.category, tag.source].filter(Boolean).join(' · ');
                tags.append(chip);
            }
            if (tags.childNodes.length) panel.append(tags);
            const reviewsHeader = element('div', 'mt-review-heading');
            reviewsHeader.append(element('h3', '', labels.reviews));
            if (data.review_provider) reviewsHeader.append(element('span', 'mt-source', data.review_provider));
            if (current.reviews.length) reviewsHeader.append(element('span', 'mt-counts', labels.loaded + ' ' + current.reviews.length));
            panel.append(reviewsHeader);
            for (const review of current.reviews) {
                const card = element('article', 'mt-review');
                const byline = element('div', 'mt-review-byline');
                byline.append(element('strong', '', review.author || labels.anonymous));
                if (review.source || data.provider) byline.append(element('span', '', review.source || data.provider));
                const score = formatRating({ value: review.score, scale_max: review.scale_max });
                if (score) byline.append(element('span', 'mt-review-score', score));
                if (review.date && Number.isFinite(Date.parse(review.date))) {
                    const date = element('time', '', new Date(review.date).toLocaleDateString());
                    date.dateTime = review.date; byline.append(date);
                }
                card.append(byline);
                if (review.title) card.append(element('h4', '', review.title));
                card.append(element('p', 'mt-review-text', review.comment));
                const helpful = countText(review.helpful_count,
                    String(review.source).toUpperCase() === 'JAVDB' ? labels.likes : labels.helpful);
                if (helpful) card.append(element('div', 'mt-counts', helpful));
                panel.append(card);
            }
            if (data.review_status === 'unsupported') panel.append(message(labels.unsupported));
            else if (data.review_status !== 'available') panel.append(message(labels.unavailable));
            else if (!current.reviews.length && !current.loading) panel.append(message(labels.empty));
            const page = data.reviews;
            if (page?.fetched_at && Number.isFinite(Date.parse(page.fetched_at)))
                panel.append(element('p', 'mt-counts', labels.cached + ' · ' + new Date(page.fetched_at).toLocaleString()));
            if (page?.has_more && !current.error) {
                const more = button(labels.more, () => load(current, page.offset + page.limit, false));
                more.disabled = current.loading;
                panel.append(more);
            }
        }
        if (current.error) {
            panel.append(message(labels.error));
            panel.append(button(labels.retry, () => load(current, current.pendingOffset, current.pendingRefresh)));
        }
        if (current.loading) panel.append(message(labels.loading));
    }
    async function load(current, offset, refresh) {
        if (current !== state || current.loading) return;
        current.loading = true; current.error = false; current.pendingOffset = offset; current.pendingRefresh = refresh;
        render(current);
        try {
            const path = 'MetaTube/Items/' + encodeURIComponent(current.id) + '/Metadata';
            const address = currentClient()?.getUrl ? currentClient().getUrl(path) : basePath + '/' + path;
            const url = new URL(address, win.location.href);
            if (url.origin !== win.location.origin) throw new Error('Metadata requires the current server');
            url.searchParams.set('offset', offset); url.searchParams.set('limit', pageSize);
            if (refresh) url.searchParams.set('refresh', 'true');
            const token = currentToken();
            if (!token || !/^[A-Za-z0-9._-]+$/.test(token)) throw new Error('No current session');
            const response = await win.fetch(url.href, {
                signal: current.abort.signal, credentials: 'same-origin', cache: 'no-store',
                headers: { Authorization: 'MediaBrowser Token="' + token + '"' }
            });
            if (current !== state) return;
            if (response.status === 404) { current.unsupported = true; current.panel?.remove(); return; }
            if (!response.ok) throw new Error('Metadata request failed');
            const data = await response.json();
            if (current !== state || current.abort.signal.aborted) return;
            const previous = current.data;
            if (data.metadata_status === 'unavailable' && previous) {
                data.ratings = previous.ratings; data.tags = previous.tags;
            }
            if (data.review_status === 'available' && Array.isArray(data.reviews?.items)) {
                current.reviews = mergeReviews(offset === 0 ? [] : current.reviews, data.reviews.items);
            } else {
                data.reviews = previous?.reviews || null;
                current.error = data.review_status !== 'unsupported';
            }
            current.data = data;
        } catch (error) {
            if (current !== state || current.abort.signal.aborted) return;
            current.error = true;
        } finally {
            if (current === state) { current.loading = false; render(current); }
        }
    }
    function sync() {
        scheduled = false;
        const token = currentToken();
        let serverId;
        try { serverId = currentClient()?.serverId?.(); } catch (_) { /* Not connected to a server. */ }
        const id = token && matchesServer(win.location, serverId) ? itemIdFromLocation(win.location) : null;
        if (state?.id !== id || (state && state.token !== token)) {
            state?.abort.abort(); state?.panel?.remove();
            state = id ? { id, token, abort: new AbortController(), panel: null, data: null, reviews: [], loading: false } : null;
        }
        if (!state || state.unsupported) return;
        if (mount(state) && !state.requested) { state.requested = true; load(state, 0, false); }
    }
    function schedule() {
        if (scheduled) return;
        scheduled = true;
        win.setTimeout(sync, 100);
    }
    function start() {
        win.addEventListener('hashchange', schedule);
        win.addEventListener('popstate', schedule);
        doc.addEventListener('viewshow', schedule);
        new MutationObserver(schedule).observe(doc.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'hidden'] });
        // Also observe session changes and clients that replace routes without a hash event.
        win.setInterval(schedule, 1500);
        schedule();
    }
    return { start, itemIdFromLocation, matchesServer, formatRating, mergeReviews };
}));
