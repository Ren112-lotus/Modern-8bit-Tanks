mergeInto(LibraryManager.library, {
    TanksGetLaunchOptions: function () {
        var hash = window.location.hash;
        if (!hash.startsWith('#tanks=')) return 0;
        try {
            var raw = decodeURIComponent(hash.substring(7));
            var bytes = lengthBytesUTF8(raw) + 1;
            var ptr = _malloc(bytes);
            stringToUTF8(raw, ptr, bytes);
            return ptr;
        } catch (_) { return 0; }
    },
    TanksNotifyRoom: function (kind, value) {
        if (window.parent === window) return;
        var origin;
        try { origin = new URL(document.referrer).origin; } catch (_) { return; }
        if (origin !== 'https://modern-8bit-tanks-multiplayer.renren112.chatgpt.site' &&
            origin !== 'http://127.0.0.1:5173') return;
        window.parent.postMessage({ type: 'tanks-session', kind: UTF8ToString(kind), value: UTF8ToString(value) },
            origin);
    }
});
