let contentWarningDetected = false;
let obsIntegrationAttempted = false;

const handleContentWarning = async (detectionMethod = 'unknown') => {
    if (contentWarningDetected) return;

    contentWarningDetected = true;
    console.log(`[Cliparino] Content warning detected via ${detectionMethod}`);

    try {
        const response = await fetch('/api/content-warning', {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({
                detectionMethod: detectionMethod,
                timestamp: new Date().toISOString()
            })
        });

        const result = await response.json();

        if (result && result.obsAutomation === true) {
            showContentWarningNotification('OBS automation active');
        } else {
            showContentWarningNotification('Manual interaction required');
        }
    } catch (error) {
        console.log('[Cliparino] Content warning notification failed:', error);
        showContentWarningNotification('Manual interaction required');
    }
};

const showContentWarningNotification = (method = 'detected') => {
    const notification = document.createElement('div');

    notification.id = 'content-warning-notification';
    notification.style.cssText = `
        position: absolute;
        top: 10%;
        right: 5%;
        background: rgba(255, 184, 9, 0.9);
        color: #042239;
        padding: 15px;
        border-radius: 5px;
        font-family: 'Open Sans', sans-serif;
        font-size: 1.1em;
        z-index: 1000;
        max-width: 320px;
        box-shadow: 0 4px 8px rgba(0,0,0,0.3);
        text-align: center;
    `;

    if (method === 'OBS automation active') {
        notification.innerHTML = `
            <strong>✓ Content Warning Handled</strong><br>
            <small>OBS automation is processing...</small>
        `;
        notification.style.background = 'rgba(76, 175, 80, 0.9)';
        notification.style.color = 'white';
    } else {
        notification.innerHTML = `
            <strong>⚠ Content Warning</strong><br>
            Right-click Browser Source in OBS<br>
            → Select "Interact"<br>
            → Click through warning<br>
            <small>Usually only needed once per session</small>
        `;
    }

    document.body.appendChild(notification);

    setTimeout(() => {
        if (notification.parentNode) {
            notification.parentNode.removeChild(notification);
        }
    }, 12000);
};

const detectContentWarning = () => {
    const iframe = document.getElementById('clip-iframe');

    if (!iframe) return;

    try {
        const iframeDoc = iframe.contentWindow.document;
        const warningSelectors = [
            '.content-warning-overlay',
            '.content-classification-gate-overlay',
            '.content-gate-overlay',
            '.mature-content-overlay',
            '[data-a-target="content-classification-gate-overlay"]'
        ];

        for (const selector of warningSelectors) {
            const element = iframeDoc.querySelector(selector);

            if (element) {
                handleContentWarning('DOM-access');
                return;
            }
        }
    } catch (e) {

    }

    if (iframe.src.includes('error=') || iframe.src.includes('warning=')) {
        handleContentWarning('URL-parameter');
        return;
    }

    const checkDelay = () => {
        if (!iframe.contentWindow || iframe.contentWindow.location.href === 'about:blank') {
            handleContentWarning('loading-delay');
        }
    };

    setTimeout(checkDelay, 4000);
};

const iframe = document.getElementById('clip-iframe');

iframe.addEventListener('load', () => {
    setTimeout(detectContentWarning, 1000);
    setTimeout(detectContentWarning, 3000);
});

if (window.obsstudio) {
    console.log('[Cliparino] OBS Browser Source environment detected');
}

window.addEventListener('message', (event) => {
    if (event.origin !== 'https://clips.twitch.tv') return;

    if (event.data && (event.data.type === 'mature-content-gate' || event.data.type === 'content-warning')) {
        handleContentWarning('postMessage');
    }
});

setTimeout(detectContentWarning, 2000);
