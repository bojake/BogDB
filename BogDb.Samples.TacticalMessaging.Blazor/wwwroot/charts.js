// Chart.js 4 helpers for Tactical Messaging sample
window.renderBarChart = function (canvasId, labels, datasets, options) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;
    if (ctx._chartInstance) ctx._chartInstance.destroy();
    ctx._chartInstance = new Chart(ctx, {
        type: 'bar',
        data: { labels, datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { legend: { labels: { color: '#9ba3c0', font: { family: 'Inter' } } } },
            scales: {
                x: { ticks: { color: '#5a6380', font: { family: 'Inter', size: 11 } }, grid: { color: 'rgba(255,255,255,0.04)' } },
                y: { ticks: { color: '#5a6380', font: { family: 'Inter', size: 11 } }, grid: { color: 'rgba(255,255,255,0.04)' } }
            },
            ...options
        }
    });
};

window.renderDoughnutChart = function (canvasId, labels, data, colors) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;
    if (ctx._chartInstance) ctx._chartInstance.destroy();
    ctx._chartInstance = new Chart(ctx, {
        type: 'doughnut',
        data: { labels, datasets: [{ data, backgroundColor: colors, borderWidth: 0 }] },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { legend: { position: 'right', labels: { color: '#9ba3c0', font: { family: 'Inter', size: 11 }, padding: 12 } } },
            cutout: '65%'
        }
    });
};

window.destroyChart = function (canvasId) {
    const ctx = document.getElementById(canvasId);
    if (ctx && ctx._chartInstance) { ctx._chartInstance.destroy(); ctx._chartInstance = null; }
};
