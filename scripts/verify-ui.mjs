// Optional local browser QA. Only use with the synthetic fixture produced by CashHarbor.Tests.
import { spawn } from 'node:child_process';
import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import path from 'node:path';
const [fixturePath] = process.argv.slice(2);
const fixture = JSON.parse(await readFile(fixturePath, 'utf8'));
const directory = path.dirname(fixturePath);
const profile = path.join(directory, 'chrome-profile');
const screenshots = path.join(directory, 'screenshots');
await mkdir(profile, { recursive: true }); await mkdir(screenshots, { recursive: true });
const chrome = spawn(process.env.CASHHARBOR_BROWSER, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let socket;
try {
  const portFile = path.join(profile, 'DevToolsActivePort');
  for (let i = 0; i < 100; i++) { try { await access(portFile); break; } catch { await delay(100); } }
  const port = (await readFile(portFile, 'utf8')).split('\n')[0].trim();
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  let id = 0; const pending = new Map(); const failures = [];
  socket.addEventListener('message', event => {
    const msg = JSON.parse(event.data);
    if (msg.id) { const p = pending.get(msg.id); pending.delete(msg.id); if (msg.error) p.reject(new Error(JSON.stringify(msg.error))); else p.resolve(msg.result); }
    if (msg.method === 'Runtime.exceptionThrown') failures.push(msg.params.exceptionDetails.text);
    if (msg.method === 'Log.entryAdded' && msg.params.entry.level === 'error') failures.push(msg.params.entry.text);
  });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const key = ++id;
    const timeout = setTimeout(() => { pending.delete(key); reject(new Error(`CDP timeout: ${method}`)); }, 15000);
    pending.set(key, { resolve: r => { clearTimeout(timeout); resolve(r); }, reject: e => { clearTimeout(timeout); reject(e); } });
    socket.send(JSON.stringify({ id: key, method, params }));
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Log.enable'); await send('Network.enable');
  await send('Security.setIgnoreCertificateErrors', { ignore: true });
  await send('Network.setCookies', { cookies: fixture.cookies });
  for (const width of [1440, 390]) {
    await send('Emulation.setDeviceMetricsOverride', { width, height: width === 390 ? 844 : 1000, deviceScaleFactor: 1, mobile: width === 390 });
    for (const route of ['/', '/comptes', '/comptes?type=epargne', '/comptes?vue=commun', '/comptes?vue=personnel', '/operations', '/objectifs', '/echeances', '/parametres', '/connexion']) {
      await send('Page.navigate', { url: fixture.origin + route });
      await delay(500);
      for (let i = 0; i < 40; i++) { const r = await send('Runtime.evaluate', { expression: 'document.readyState === "complete"', returnByValue: true }); if (r.result.value) break; await delay(100); }
      const result = await send('Runtime.evaluate', { expression: 'JSON.stringify({width:innerWidth,scroll:document.documentElement.scrollWidth,h1:document.querySelector("h1")?.textContent,cream:getComputedStyle(document.documentElement).getPropertyValue("--accent").trim(),styles:[...document.querySelectorAll("link")].map(x=>x.href)})', returnByValue: true });
      const state = JSON.parse(result.result.value);
      if (state.scroll > state.width + 1) throw new Error(`Horizontal overflow at ${route} / ${width}: ${JSON.stringify(state)}`);
      if (!state.h1 || state.h1.includes('Votre espace privé') || state.cream !== '#ef4444') throw new Error(`Page did not render with styles: ${route} / ${width}: ${JSON.stringify(state)}`);
      const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
      await writeFile(path.join(screenshots, `${width}-${route === '/' ? 'dashboard' : route.slice(1).replace(/[^a-z0-9-]/gi, '-')}.png`), Buffer.from(shot.data, 'base64'));
      console.log(`UI PASS ${width}px ${route}`);
    }
  }
  if (failures.length) throw new Error(`Browser errors: ${failures.join('\n')}`);
  console.log(`Screenshots: ${screenshots}`);
} finally {
  socket?.close(); chrome.kill();
}
