let currentClipId = null;
let pollingInterval;
let nudgeTimeout;

const applySettings = async () => {
    try {
        const response = await fetch('/api/settings');
        const settings = await response.json();
        const root = document.documentElement.style;

        root.setProperty('--player-width', settings.width + 'px');
        root.setProperty('--player-height', settings.height + 'px');
        root.setProperty('--info-text-color', settings.infoTextColor);
        root.setProperty('--info-bg-color', settings.infoBackgroundColor);
        root.setProperty('--info-font-family', settings.infoFontFamily);
    } catch (error) {
        console.error('[Cliparino] Failed to fetch settings:', error);
    }
};

const pollStatus = async () => {
    try {
        const response = await fetch('/api/status');
        const data = await response.json();

        updateUI(data);
    } catch (error) {
        console.error('[Cliparino] Failed to fetch status:', error);
    }
};

const destroyPlayer = () => {
    const container = document.getElementById('clip-player');
    if (container) container.innerHTML = '';
};

const updateUI = (data) => {
    const playerContainer = document.getElementById('player-container');
    const idleContainer = document.getElementById('idle-container');

    if (data.state === 'Playing' || data.state === 'Loading' || data.state === 'Cooldown') {
        playerContainer.style.zIndex = '2';
        idleContainer.style.zIndex = '1';
        idleContainer.style.display = 'none';

        if (nudgeTimeout) {
            clearTimeout(nudgeTimeout);
            nudgeTimeout = null;
        }

        if (data.currentClip && data.currentClip.id !== currentClipId) {
            loadClip(data.currentClip);
        }
    } else {
        playerContainer.style.zIndex = '1';
        idleContainer.style.zIndex = '2';
        idleContainer.style.display = 'flex';
        currentClipId = null;
        destroyPlayer();
    }
};

const loadClip = async (clip) => {
    // Set immediately to block re-entrant calls from the polling loop
    currentClipId = clip.id;

    const streamerGame = document.getElementById('streamer-game');
    const clipTitle = document.getElementById('clip-title');
    const clipCreator = document.getElementById('clip-creator');

    if (streamerGame) streamerGame.textContent = `${clip.broadcaster.display_name} doin' a heckin' ${clip.gameName} stream`;
    if (clipTitle) clipTitle.textContent = clip.title;
    if (clipCreator) clipCreator.textContent = `by ${clip.creator.display_name}`;

    console.log(`[Cliparino] Loading clip ${clip.id} (${clip.durationSeconds}s)`);

    destroyPlayer();

    // Primary path: Twitch's own signed download URL (own clips only — falls through for shoutout clips)
    let downloadUrl = null;
    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 2500);
        const resp = await fetch(`/api/clip/${encodeURIComponent(clip.id)}/download-url`, {signal: controller.signal});
        clearTimeout(timeoutId);
        if (resp.ok) downloadUrl = (await resp.json()).url || null;
    } catch (err) {
        console.warn(`[Cliparino] Download URL fetch failed for ${clip.id}:`, err);
    }

    if (downloadUrl) {
        console.log('[Cliparino] Using signed download URL');
        appendVideoElement(downloadUrl, clip);
        return;
    }

    // Secondary fallback: Try to derive direct URL from thumbnail (legacy hack)
    if (clip.thumbnailUrl) {
        const derivedUrl = clip.thumbnailUrl.replace(/-preview-\d+x\d+\.jpg$/, '.mp4');
        if (derivedUrl !== clip.thumbnailUrl) {
            console.log('[Cliparino] Using derived URL from thumbnail');
            appendVideoElement(derivedUrl, clip);
            return;
        }
    }

    // Direct path failed or unavailable, go straight to iframe
    console.log('[Cliparino] direct video URLs unavailable, using Twitch embed iframe');
    appendClipIframe(clip);
};

const appendVideoElement = (src, clip) => {
    const video = document.createElement('video');
    video.playsInline = true;
    video.src = src;
    video.id = 'video-element';
    const playerDiv = document.getElementById('clip-player');
    if (playerDiv) playerDiv.appendChild(video);

    // Timeout to fallback to iframe if video fails to play/load
    const videoTimeout = setTimeout(() => {
        console.warn('[Cliparino] Video playback timed out after 3s, falling back to iframe');
        destroyPlayer();
        appendClipIframe(clip);
    }, 3000);

    video.addEventListener('playing', () => {
        console.log('[Cliparino] Video playback started');
        clearTimeout(videoTimeout);
    }, {once: true});

    video.addEventListener('error', (e) => {
        clearTimeout(videoTimeout);
        console.warn('[Cliparino] Video element error, falling back to iframe:', video.error);
        destroyPlayer();
        appendClipIframe(clip);
    }, {once: true});

    video.muted = false;
    video.volume = 1.0;
    video.play().catch((err) => {
        console.warn('[Cliparino] Autoplay failed:', err);
        if (!sessionStorage.getItem('cliparino_interacted')) {
            const nudge = document.getElementById('interaction-nudge');
            if (nudge) nudge.style.display = 'block';
        }
    });
};

const appendClipIframe = (clip) => {
    // Twitch.Player SDK does not support the `clip` parameter — clips require the raw iframe embed.
    const parents = [...new Set(['localhost', '127.0.0.1', window.location.hostname])];
    const playerDiv = document.getElementById('clip-player');
    if (!playerDiv) return;

    playerDiv.innerHTML = '';

    const iframe = document.createElement('iframe');
    // Adding volume=1 and migration=true (skips content warnings) to improve reliability in OBS
    iframe.src = `https://clips.twitch.tv/embed?clip=${clip.id}&parent=${parents.join('&parent=')}&autoplay=true&muted=false&volume=1&migration=true`;
    iframe.allow = 'autoplay; fullscreen';
    iframe.style.cssText = 'width:100%;height:100%;border:none;';
    playerDiv.appendChild(iframe);

    // Bonus attempt: unmute directly if OBS CEF permits cross-origin access
    iframe.addEventListener('load', () => {
        try {
            // Some versions of the player respond to postMessage for unmuting
            iframe.contentWindow.postMessage({method: 'setMuted', args: [false]}, '*');
            iframe.contentWindow.postMessage({method: 'setVolume', args: [1.0]}, '*');

            const video = iframe.contentWindow.document.querySelector('video');
            if (video) {
                video.muted = false;
                video.volume = 1.0;
            }
        } catch (_) {
        }
    });
};

applySettings();
setInterval(applySettings, 30000);

pollStatus();
pollingInterval = setInterval(pollStatus, 1000);

document.body.addEventListener('click', () => {
    sessionStorage.setItem('cliparino_interacted', 'true');
    const nudge = document.getElementById('interaction-nudge');
    if (nudge) nudge.style.display = 'none';
});
