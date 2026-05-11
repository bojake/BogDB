// wwwroot/charts.js  — Chart.js 4 helpers callable from Blazor JS interop

window._bogdbCharts = {};

export function createChart(canvasId, type, labels, datasets, options) {
    const existing = window._bogdbCharts[canvasId];
    if (existing) { existing.destroy(); }

    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    window._bogdbCharts[canvasId] = new Chart(ctx, {
        type,
        data: { labels, datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: 600 },
            plugins: {
                legend: { labels: { color: '#e2e8f0', font: { family: 'Inter' } } },
                tooltip: { backgroundColor: 'rgba(15,23,42,0.95)', titleColor: '#7c3aed', bodyColor: '#e2e8f0' },
            },
            scales: type !== 'radar' && type !== 'pie' && type !== 'doughnut' ? {
                x: { ticks: { color: '#94a3b8' }, grid: { color: 'rgba(255,255,255,0.05)' } },
                y: { ticks: { color: '#94a3b8' }, grid: { color: 'rgba(255,255,255,0.05)' } },
            } : {},
            ...options,
        },
    });
}

export function destroyChart(canvasId) {
    if (window._bogdbCharts[canvasId]) {
        window._bogdbCharts[canvasId].destroy();
        delete window._bogdbCharts[canvasId];
    }
}
