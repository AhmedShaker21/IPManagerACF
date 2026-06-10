(function () {
    "use strict";

    var state = { search: "", status: "", page: 1 };
    var searchEl = document.getElementById("search");
    if (searchEl) state.search = searchEl.value || "";
    var activeFilter = document.querySelector("#filters button.active");
    if (activeFilter) state.status = activeFilter.dataset.status || "";

    // ---- clock -----------------------------------------------------------
    var clock = document.getElementById("clock");
    function tick() {
        if (!clock) return;
        var d = new Date();
        clock.textContent = d.toLocaleTimeString([], { hour12: false });
    }
    tick(); setInterval(tick, 1000);

    // ---- helpers ---------------------------------------------------------
    function debounce(fn, ms) {
        var t; return function () { clearTimeout(t); var a = arguments, c = this; t = setTimeout(function () { fn.apply(c, a); }, ms); };
    }
    function timeAgo(iso) {
        var s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
        if (s < 60) return Math.floor(s) + "s ago";
        if (s < 3600) return Math.floor(s / 60) + "m ago";
        return Math.floor(s / 3600) + "h ago";
    }
    var ICONS = {
        IpUsed: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M12 5v14M5 12h14"/></svg>',
        IpFreed: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M20 6 9 17l-5-5"/></svg>',
        ConflictDetected: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>',
        InternetAccessStarted: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 0 1 0 18 15 15 0 0 1 0-18Z"/></svg>'
    };

    // ---- AJAX table ------------------------------------------------------
    var tableWrap = document.getElementById("tableWrap");
    function refreshTable() {
        if (!tableWrap) return;
        var q = "?search=" + encodeURIComponent(state.search) + "&status=" + encodeURIComponent(state.status) + "&page=" + state.page;
        fetch("/Dashboard/Table" + q, { headers: { "X-Requested-With": "fetch" } })
            .then(function (r) { return r.text(); })
            .then(function (html) { tableWrap.innerHTML = html; bindRows(); });
    }
    function bindRows() {
        tableWrap && tableWrap.querySelectorAll("tr.clickable[data-device]").forEach(function (tr) {
            tr.addEventListener("click", function () { window.location = "/Devices/Details/" + tr.dataset.device; });
        });
        tableWrap && tableWrap.querySelectorAll(".pager a[data-page]").forEach(function (a) {
            a.addEventListener("click", function (e) { e.preventDefault(); state.page = parseInt(a.dataset.page, 10); refreshTable(); });
        });
    }
    bindRows();

    if (searchEl) {
        searchEl.addEventListener("input", debounce(function () {
            state.search = searchEl.value.trim(); state.page = 1; refreshTable();
        }, 250));
    }
    var filters = document.getElementById("filters");
    if (filters) {
        filters.addEventListener("click", function (e) {
            var b = e.target.closest("button"); if (!b) return;
            filters.querySelectorAll("button").forEach(function (x) { x.classList.remove("active"); });
            b.classList.add("active");
            state.status = b.dataset.status || ""; state.page = 1; refreshTable();
        });
    }

    // ---- live scope panel ------------------------------------------------
    var livePanel = document.getElementById("livePanel");
    function snapshotStatuses() {
        var map = {};
        livePanel && livePanel.querySelectorAll(".cell").forEach(function (c) { map[c.dataset.ip] = c.dataset.st + "|" + c.dataset.online; });
        return map;
    }
    function refreshLive() {
        if (!livePanel) return;
        var before = snapshotStatuses();
        fetch("/Dashboard/LivePanel", { headers: { "X-Requested-With": "fetch" } })
            .then(function (r) { return r.text(); })
            .then(function (html) {
                livePanel.innerHTML = html;
                bindCells();
                livePanel.querySelectorAll(".cell").forEach(function (c) {
                    var key = c.dataset.st + "|" + c.dataset.online;
                    if (before[c.dataset.ip] !== undefined && before[c.dataset.ip] !== key) {
                        c.classList.add("flash");
                        setTimeout(function () { c.classList.remove("flash"); }, 900);
                    }
                });
            });
    }

    // ---- scope cell tooltip + click -------------------------------------
    var tip = document.getElementById("tip");
    function bindCells() {
        livePanel && livePanel.querySelectorAll(".cell").forEach(function (c) {
            c.addEventListener("mousemove", function (e) {
                if (!tip) return;
                var host = c.dataset.host, mac = c.dataset.mac, st = c.dataset.st;
                tip.innerHTML = '<div class="ip">' + c.dataset.ip + '</div>' +
                    '<div class="row">' + st + (host ? " · " + host : "") + '</div>' +
                    (mac ? '<div class="row">' + mac + '</div>' : "") +
                    (c.dataset.online === "true" ? '<div class="row" style="color:var(--online)">internet active</div>' : "");
                tip.classList.add("show");
                var x = e.clientX + 14, y = e.clientY + 14;
                if (x + 250 > window.innerWidth) x = e.clientX - 230;
                tip.style.left = x + "px"; tip.style.top = y + "px";
            });
            c.addEventListener("mouseleave", function () { tip && tip.classList.remove("show"); });
            if (c.dataset.device) {
                c.addEventListener("click", function () { window.location = "/Devices/Details/" + c.dataset.device; });
            }
        });
    }
    bindCells();

    // ---- notifications: bell, drawer, toasts ----------------------------
    var bell = document.getElementById("bell");
    var bellCount = document.getElementById("bellCount");
    var drawer = document.getElementById("drawer");
    var scrim = document.getElementById("scrim");
    var feed = document.getElementById("feed");
    var toasts = document.getElementById("toasts");
    var unread = 0;

    function setUnread(n) {
        unread = n;
        if (!bellCount) return;
        bellCount.textContent = n > 99 ? "99+" : n;
        bellCount.classList.toggle("show", n > 0);
    }
    function loadFeed() {
        fetch("/Dashboard/Notifications?take=30").then(function (r) { return r.json(); }).then(function (data) {
            setUnread(data.unread);
            if (!feed) return;
            if (!data.items.length) { feed.innerHTML = '<div class="empty" style="padding:40px">Nothing yet.</div>'; return; }
            feed.innerHTML = data.items.map(function (n) {
                return '<div class="note ' + n.type + '"><div class="ic">' + (ICONS[n.type] || "") + '</div>' +
                    '<div><div class="t">' + n.title + '</div><div class="m">' + n.message + '</div>' +
                    '<div class="ago">' + timeAgo(n.createdAt) + '</div></div></div>';
            }).join("");
        });
    }
    function openDrawer() { drawer && drawer.classList.add("show"); scrim && scrim.classList.add("show"); loadFeed(); }
    function closeDrawer() { drawer && drawer.classList.remove("show"); scrim && scrim.classList.remove("show"); }

    bell && bell.addEventListener("click", openDrawer);
    scrim && scrim.addEventListener("click", closeDrawer);
    document.getElementById("closeDrawer") && document.getElementById("closeDrawer").addEventListener("click", closeDrawer);
    document.getElementById("markRead") && document.getElementById("markRead").addEventListener("click", function () {
        fetch("/Dashboard/MarkRead", { method: "POST" }).then(function () { setUnread(0); });
    });

    function toast(n) {
        if (!toasts) return;
        var el = document.createElement("div");
        el.className = "toast " + n.type;
        el.innerHTML = '<div class="ic">' + (ICONS[n.type] || "") + '</div><div><div class="t">' + n.title + '</div><div class="m">' + n.message + '</div></div>';
        toasts.appendChild(el);
        setTimeout(function () { el.style.transition = "opacity .3s"; el.style.opacity = "0"; setTimeout(function () { el.remove(); }, 320); }, 4200);
        while (toasts.children.length > 4) toasts.removeChild(toasts.firstChild);
    }

    // ---- SignalR ---------------------------------------------------------
    var conn = document.getElementById("conn");
    var connText = document.getElementById("connText");
    function setConn(ok, text) { if (conn) conn.classList.toggle("down", !ok); if (connText) connText.textContent = text; }

    var refreshLiveDebounced = debounce(function () { refreshLive(); refreshTable(); }, 350);

    if (window.signalR) {
        var hub = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/network")
            .withAutomaticReconnect()
            .build();

        hub.on("notify", function (n) { setUnread(unread + 1); toast(n); if (drawer && drawer.classList.contains("show")) loadFeed(); });
        hub.on("stateChanged", function () { refreshLiveDebounced(); });
        hub.onreconnecting(function () { setConn(false, "reconnecting…"); });
        hub.onreconnected(function () { setConn(true, "live"); });
        hub.onclose(function () { setConn(false, "offline"); });

        hub.start().then(function () { setConn(true, "live"); loadFeed(); })
            .catch(function () { setConn(false, "offline"); });
    } else {
        setConn(false, "no realtime");
        loadFeed();
    }
})();
