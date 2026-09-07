(() => {
    const root = document.querySelector('.employee-home');
    if (!root) return;

    const key = 'vertex-attendance-session';
    const $ = id => document.getElementById(id);
    let watchId = null;
    let timerId = null;
    let session = readSession();

    function readSession() {
        try { return JSON.parse(localStorage.getItem(key)) || null; } catch { return null; }
    }
    function saveSession() { localStorage.setItem(key, JSON.stringify(session)); }
    function tickClock() {
        const now = new Date();
        $('liveClock').textContent = now.toLocaleTimeString([], { hour12: true });
        $('liveDate').textContent = now.toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'short' });
        if (session?.active) {
            const mins = Math.floor((Date.now() - new Date(session.punchIn).getTime()) / 60000);
            $('workingTime').textContent = `${String(Math.floor(mins / 60)).padStart(2, '0')}h ${String(mins % 60).padStart(2, '0')}m`;
        }
    }
    function positionReceived(position) {
        const point = { latitude: position.coords.latitude, longitude: position.coords.longitude, accuracy: Math.round(position.coords.accuracy), time: new Date().toISOString() };
        if (!session.points) session.points = [];
        const last = session.points.at(-1);
        if (!last || distanceMeters(last, point) >= 10 || Date.now() - new Date(last.time).getTime() >= 60000) session.points.push(point);
        session.lastPoint = point;
        saveSession();
        $('permissionNotice').hidden = true;
        render();
    }
    function positionError(error) {
        $('permissionNotice').hidden = false;
        const messages = { 1: 'Location permission was denied. Please allow it in browser settings.', 2: 'Your location is currently unavailable.', 3: 'Location request timed out. Please try again.' };
        $('permissionNotice').querySelector('span').textContent = messages[error.code] || 'Could not get your location.';
        if (session && !session.points?.length) { session = null; localStorage.removeItem(key); }
        render();
    }
    function distanceMeters(a, b) {
        const rad = n => n * Math.PI / 180, r = 6371e3;
        const dLat = rad(b.latitude - a.latitude), dLon = rad(b.longitude - a.longitude);
        const x = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.latitude)) * Math.cos(rad(b.latitude)) * Math.sin(dLon / 2) ** 2;
        return 2 * r * Math.atan2(Math.sqrt(x), Math.sqrt(1 - x));
    }
    function startWatch() {
        if (!navigator.geolocation) return positionError({ code: 2 });
        if (watchId !== null) navigator.geolocation.clearWatch(watchId);
        watchId = navigator.geolocation.watchPosition(positionReceived, positionError, { enableHighAccuracy: true, maximumAge: 15000, timeout: 20000 });
    }
    function punchIn() {
        if (!navigator.geolocation) return positionError({ code: 2 });
        session = { active: true, employee: root.dataset.employee, punchIn: new Date().toISOString(), points: [] };
        render();
        startWatch();
    }
    function punchOut() {
        if (!session?.active) return;
        session.active = false;
        session.punchOut = new Date().toISOString();
        if (watchId !== null) navigator.geolocation.clearWatch(watchId);
        watchId = null;
        saveSession();
        render();
    }
    function render() {
        const active = !!session?.active;
        $('punchInButton').disabled = active;
        $('punchOutButton').disabled = !active;
        $('attendanceTitle').textContent = active ? 'You are punched in' : session?.punchOut ? 'Workday completed' : 'Ready to start your workday?';
        $('attendanceHelp').textContent = active ? 'Your location is being recorded securely until you punch out.' : session?.punchOut ? `Punched out at ${formatTime(session.punchOut)}. Location tracking is off.` : 'Punch in will request your location permission. Tracking stays active until you punch out.';
        $('locationStatus').classList.toggle('active', active);
        $('locationStatus').querySelector('span:last-child').textContent = active ? 'Live location tracking active' : 'Location is not being tracked';
        const point = session?.lastPoint;
        $('lastLocation').textContent = point ? `${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)}` : 'Not available';
        $('trackedPoints').textContent = `${session?.points?.length || 0} locations`;
        renderTimeline();
    }
    function formatTime(value) { return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); }
    function renderTimeline() {
        const timeline = $('timeline');
        if (!session) return;
        timeline.className = 'activity-list';
        let html = `<div class="activity-event"><i class="fa-solid fa-right-to-bracket"></i><div><strong>Punched in</strong><span>Location tracking started</span></div><time>${formatTime(session.punchIn)}</time></div>`;
        if (session.lastPoint) html += `<div class="activity-event"><i class="fa-solid fa-location-crosshairs"></i><div><strong>${session.active ? 'Live location updated' : 'Last known location'}</strong><span>Accuracy ±${session.lastPoint.accuracy} metres</span></div><time>${formatTime(session.lastPoint.time)}</time></div>`;
        if (session.punchOut) html += `<div class="activity-event"><i class="fa-solid fa-arrow-right-from-bracket"></i><div><strong>Punched out</strong><span>Location tracking stopped</span></div><time>${formatTime(session.punchOut)}</time></div>`;
        timeline.innerHTML = html;
    }
    $('punchInButton').addEventListener('click', punchIn);
    $('punchOutButton').addEventListener('click', punchOut);
    $('retryLocation').addEventListener('click', () => session?.active ? startWatch() : punchIn());
    window.addEventListener('beforeunload', () => watchId !== null && navigator.geolocation.clearWatch(watchId));
    tickClock(); timerId = setInterval(tickClock, 1000); render();
    if (session?.active) startWatch();
})();
