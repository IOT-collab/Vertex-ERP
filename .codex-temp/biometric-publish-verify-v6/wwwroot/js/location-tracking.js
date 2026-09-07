(() => {
    const mapNode = document.getElementById('employeeMap');
    if (!mapNode || typeof L === 'undefined') return;

    const sampleRoute = [
        [28.6286, 77.3649], [28.6261, 77.3594], [28.6218, 77.3520],
        [28.6165, 77.3444], [28.6129, 77.3357], [28.6047, 77.3274],
        [28.5975, 77.3202], [28.5901, 77.3172]
    ];
    let route = sampleRoute;
    let employeeName = 'Arjun Kumar';
    try {
        const stored = JSON.parse(localStorage.getItem('vertex-attendance-session'));
        if (stored?.points?.length) {
            route = stored.points.map(point => [point.latitude, point.longitude]);
            employeeName = stored.employee || employeeName;
            document.querySelector('.map-toolbar h2').textContent = `${employeeName}'s route`;
            document.querySelector('.detail-profile h2').textContent = employeeName;
            document.getElementById('distanceTotal').textContent = `${routeDistance(route).toFixed(2)} km`;
        }
    } catch { /* Sample data remains available for the UI preview. */ }

    const map = L.map(mapNode, { zoomControl: false }).setView(route[0], 13);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors'
    }).addTo(map);
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    const line = L.polyline(route, { color: '#246bfd', weight: 5, opacity: .9, lineCap: 'round' }).addTo(map);
    L.circleMarker(route[0], { radius: 7, color: '#fff', weight: 3, fillColor: '#12b981', fillOpacity: 1 }).addTo(map).bindPopup('<strong>Punch in</strong><br>Workday started here');
    const currentIcon = L.divIcon({ className: 'custom-current-marker', iconSize: [18, 18], iconAnchor: [9, 9] });
    L.marker(route.at(-1), { icon: currentIcon }).addTo(map).bindPopup(`<strong>${employeeName}</strong><br>Current / last recorded location`).openPopup();
    if (route.length > 1) map.fitBounds(line.getBounds(), { padding: [45, 45] });

    function routeDistance(points) {
        const rad = value => value * Math.PI / 180;
        let metres = 0;
        for (let i = 1; i < points.length; i++) {
            const [aLat, aLon] = points[i - 1], [bLat, bLon] = points[i];
            const dLat = rad(bLat - aLat), dLon = rad(bLon - aLon);
            const a = Math.sin(dLat / 2) ** 2 + Math.cos(rad(aLat)) * Math.cos(rad(bLat)) * Math.sin(dLon / 2) ** 2;
            metres += 6371e3 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        }
        return metres / 1000;
    }
})();
