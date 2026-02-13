let currentClipId = null;
let pollingInterval;

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

const updateUI = (data) => {
    const playerContainer = document.getElementById('player-container');
    const idleContainer = document.getElementById('idle-container');
    const iframe = document.getElementById('clip-iframe');

    if (data.state === 'Playing' || data.state === 'Loading' || data.state === 'Cooldown') {
        // Bring player to front by hiding idle screen
        playerContainer.style.zIndex = '2';
        idleContainer.style.zIndex = '1';
        idleContainer.style.display = 'none';

        if (data.currentClip && data.currentClip.id !== currentClipId) {
            loadClip(data.currentClip);
        }
    } else {
        // Show idle screen on top
        playerContainer.style.zIndex = '1';
        idleContainer.style.zIndex = '2';
        idleContainer.style.display = 'flex';
        currentClipId = null;
        if (iframe.src !== 'about:blank') {
            iframe.src = 'about:blank';
        }
    }
};

const loadClip = (clip) => {
    currentClipId = clip.id;
    const iframe = document.getElementById('clip-iframe');
    const streamerGame = document.getElementById('streamer-game');
    const clipTitle = document.getElementById('clip-title');
    const clipCreator = document.getElementById('clip-creator');

    console.log(`[Cliparino] Loading clip:`, clip);
    console.log(`[Cliparino] Duration: ${clip.durationSeconds}s`);

    // Update text overlays
    streamerGame.textContent = `${clip.broadcaster.display_name} doin' a heckin' ${clip.gameName} stream`;
    clipTitle.textContent = clip.title;
    clipCreator.textContent = `by ${clip.creator.display_name}`;

    // Build embed URL
    const parents = ['localhost', '127.0.0.1', window.location.hostname];
    const parentParams = parents.map(p => `parent=${p}`).join('&');

    // Unmuted autoplay - requires user interaction once per session
    const embedUrl = `https://clips.twitch.tv/embed?clip=${clip.id}&autoplay=true&muted=false&${parentParams}`;

    console.log(`[Cliparino] Embed URL: ${embedUrl}`);
    iframe.src = embedUrl;
};

// Apply settings on load and refresh every 30 seconds
applySettings();
setInterval(applySettings, 30000);

// Initial poll and start interval
pollStatus();
pollingInterval = setInterval(pollStatus, 1000);

// Interaction nudge handling
document.body.addEventListener('click', () => {
    console.log('[Cliparino] User interaction detected');
    const nudge = document.getElementById('interaction-nudge');
    if (nudge) nudge.style.display = 'none';
});

// Show nudge if we detect we're in OBS, and it's been a while without interaction
if (window.obsstudio) {
    setTimeout(() => {
        const nudge = document.getElementById('interaction-nudge');
        const idleContainer = document.getElementById('idle-container');
        if (nudge && idleContainer && idleContainer.style.display !== 'none') {
            nudge.style.display = 'block';
        }
    }, 5000);
}
