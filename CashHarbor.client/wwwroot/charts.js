// Progressive enhancement only. All monetary calculations and authorization stay on the server.
for (const chart of document.querySelectorAll('[data-chart]')) {
  const svg = chart.querySelector('[data-chart-svg]');
  const points = [...chart.querySelectorAll('.chart-datum')];
  if (!svg || !points.length) continue;
  chart.classList.add('charts-ready');
  const cursor = chart.querySelector('.chart-cursor');
  const tooltip = chart.querySelector('.chart-tooltip');
  const toggles = [...chart.querySelectorAll('[data-series-toggle]')];
  let index = points.length - 1;
  let last = -1;
  const show = (next, force = false) => {
    index = Math.max(0, Math.min(points.length - 1, next));
    if (index === last && !force) return;
    last = index;
    const point = points[index]; const x = Number(point.dataset.x);
    cursor.toggleAttribute('hidden', false); tooltip.toggleAttribute('hidden', false);
    cursor.querySelector('line').setAttribute('x1', x); cursor.querySelector('line').setAttribute('x2', x);
    cursor.querySelector('circle').setAttribute('cx', x); cursor.querySelector('circle').setAttribute('cy', point.dataset.y0);
    cursor.querySelector('circle').toggleAttribute('hidden', toggles.length > 0 && !toggles[0].checked);
    chart.querySelector('[data-tooltip-date]').textContent = point.dataset.label;
    chart.querySelector('[data-chart-date]').textContent = point.dataset.label;
    for (let i = 0; i < 3; i++) {
      const value = point.dataset[`value${i}`] ?? '';
      const name = chart.dataset[`series${i}`] ?? '';
      const target = chart.querySelector(`[data-tooltip-value="${i}"]`);
      const readout = chart.querySelector(`[data-readout-value="${i}"]`);
      const enabled = !toggles.length || toggles.find(t => Number(t.dataset.seriesToggle) === i)?.checked;
      target.textContent = enabled && value ? `${name.length > 23 ? name.slice(0, 22) + '…' : name} : ${value}` : '';
      if (readout) readout.textContent = value;
    }
  };
  const selectPoint = event => {
    const matrix = svg.getScreenCTM(); if (!matrix) return;
    const position = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    let nearest = 0; let distance = Infinity;
    points.forEach((p, i) => { const d = Math.abs(Number(p.dataset.x) - position.x); if (d < distance) { distance = d; nearest = i; } });
    show(nearest);
  };
  svg.addEventListener('pointermove', selectPoint);
  svg.addEventListener('pointerdown', selectPoint);
  svg.addEventListener('pointerleave', () => { cursor.toggleAttribute('hidden', true); tooltip.toggleAttribute('hidden', true); last = -1; });
  svg.addEventListener('focus', () => show(index, true));
  svg.addEventListener('blur', () => { cursor.toggleAttribute('hidden', true); tooltip.toggleAttribute('hidden', true); last = -1; });
  svg.addEventListener('keydown', event => {
    const next = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? points.length - 1 : null;
    if (next !== null) { event.preventDefault(); show(next, true); }
  });
  for (const toggle of toggles) toggle.addEventListener('change', () => {
    if (!toggles.some(t => t.checked)) toggle.checked = true;
    const key = toggle.dataset.seriesToggle;
    chart.querySelector(`[data-chart-series="${key}"]`).toggleAttribute("hidden", !toggle.checked);
    chart.querySelector(`[data-chart-series-legend="${key}"]`)?.toggleAttribute("hidden", !toggle.checked);
    chart.querySelector(`[data-readout-series="${key}"]`).hidden = !toggle.checked;
    show(index, true);
  });
  chart.querySelector('[data-download-chart]')?.addEventListener('click', event => {
    const copy = svg.cloneNode(true);
    const originals = [...svg.querySelectorAll('*')]; const clones = [...copy.querySelectorAll('*')];
    const attributes = ['fill', 'stroke', 'stroke-width', 'stroke-dasharray', 'opacity', 'font-size', 'font-family', 'font-weight'];
    originals.forEach((original, i) => { const style = getComputedStyle(original); for (const attr of attributes) clones[i].setAttribute(attr, style.getPropertyValue(attr)); });
    copy.querySelectorAll('.chart-datum,.chart-cursor,.chart-tooltip,[hidden]').forEach(el => el.remove());
    copy.removeAttribute('tabindex'); copy.removeAttribute('data-chart-svg'); copy.setAttribute('viewBox', '0 0 980 286'); copy.removeAttribute('preserveAspectRatio'); copy.setAttribute('xmlns', 'http://www.w3.org/2000/svg'); copy.setAttribute('width', '980'); copy.setAttribute('height', '286');
    const background = document.createElementNS('http://www.w3.org/2000/svg', 'rect'); background.setAttribute('width', '980'); background.setAttribute('height', '286'); background.setAttribute('fill', '#17191d'); copy.prepend(background);
    const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(copy)], { type: 'image/svg+xml;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url; link.download = event.currentTarget.dataset.filename; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
}
for (const select of document.querySelectorAll('[data-period-select]')) select.addEventListener('change', () => {
  const custom = select.closest('form').querySelector('[data-custom-dates]'); custom.hidden = select.value !== 'custom';
  custom.querySelectorAll('input').forEach(input => { input.required = select.value === 'custom'; });
});
