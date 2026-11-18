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
    for (const route of ['/', '/comptes', '/comptes?type=epargne', '/comptes?vue=commun', '/comptes?vue=personnel', '/operations', '/objectifs', '/echeances', '/prets', ...(fixture.loanRoutes ?? []), '/parametres', '/budget', '/projection', '/marches', '/marches?kind=action', '/marches?kind=crypto', '/?espace=couple', '/operations?espace=couple', '/objectifs?espace=couple', '/echeances?espace=couple', '/comptes?type=epargne&espace=couple', '/prets?espace=couple', '/connexion']) {
      await send('Page.navigate', { url: fixture.origin + route });
      await delay(500);
      for (let i = 0; i < 40; i++) { const r = await send('Runtime.evaluate', { expression: 'document.readyState === "complete"', returnByValue: true }); if (r.result.value) break; await delay(100); }
      if (route === '/comptes?type=epargne') {
        await send('Runtime.evaluate', { expression: 'if(document.querySelector(".account-card details")) document.querySelector(".account-card details").open = true' });
      }
      if (route === '/parametres') {
        const reminder = await send('Runtime.evaluate', { expression: `(() => {document.querySelector('.smtp-settings').open=true;const checkbox=document.querySelector('#rappels input[type="checkbox"]');return checkbox.getBoundingClientRect().width<=24 && document.querySelector('[name="smtpPassword"]').value==='';})()`, returnByValue:true });
        if(!reminder.result.value) throw new Error('Reminder form styling or password exposure failed');
      }
      if(route==='/echeances'){
        const transfer=await send('Runtime.evaluate',{expression:`(()=>{const button=[...document.querySelectorAll('.modal-launch button')].find(b=>b.textContent.includes('Programmer un virement'));if(!button)return false;button.click();const form=document.querySelector('dialog[open] form[action="/actions/scheduled-transfer"]');const ok=!!form?.querySelector('select[name="destination"]')&&!!form?.querySelector('[name="__RequestVerificationToken"]');document.querySelector('dialog[open] .modal-close')?.click();return ok;})()`,returnByValue:true});if(!transfer.result.value)throw new Error('Scheduled transfer modal missing');
      }
      if (['/comptes','/prets','/operations','/objectifs','/echeances','/budget'].includes(new URL(fixture.origin+route).pathname)) {
        const modal = await send('Runtime.evaluate', { expression: `(()=>{const trigger=document.querySelector('.modal-launch button');if(!trigger)return {skip:true};trigger.click();const dialog=document.querySelector('dialog[open]');const form=dialog?.querySelector('form[method="post"]');const rect=dialog?.getBoundingClientRect();return {ok:!!dialog&&!!form?.querySelector('[name="__RequestVerificationToken"]')&&!!form?.querySelector('[name="espace"]')&&rect.width<=innerWidth&&dialog.contains(document.activeElement),title:dialog?.querySelector('h2')?.textContent};})()`, returnByValue:true });
        if(modal.exceptionDetails || (!modal.result.value?.skip&&!modal.result.value?.ok))throw new Error('Creation modal failed '+route+': '+JSON.stringify(modal));
        if(!modal.result.value.skip){
          if(route==='/comptes'){const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(path.join(screenshots,`${width}-account-modal.png`),Buffer.from(shot.data,'base64'));}
          await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});
          const closed=await send('Runtime.evaluate',{expression:'!document.querySelector("dialog[open]")',returnByValue:true});if(!closed.result.value)throw new Error('Escape did not close modal');
        }
        if(route==='/comptes'){
          const headerOpen=await send('Runtime.evaluate',{expression:`(()=>{const link=document.querySelector('.page-header a[href*="#ajouter"]');link.click();const open=!!document.querySelector('dialog[open] form[action="/actions/account-add"]');document.querySelector('dialog[open] .modal-close')?.click();return open;})()`,returnByValue:true});if(!headerOpen.result.value)throw new Error('Account creation header button navigates instead of opening dialog');
          const details=await send('Runtime.evaluate',{expression:`(()=>{const button=document.querySelector('.account-quick-actions button');if(!button)return true;button.click();const open=!!document.querySelector('dialog[open] .chart-data');document.querySelector('dialog[open] .modal-close')?.click();return open;})()`,returnByValue:true});if(!details.result.value)throw new Error('Account history dialog missing');
        }
      }
      if (route === '/projection') {
        const live = await send('Runtime.evaluate', { expression: `(()=>{const input=document.querySelector('[data-project="years"]');input.value='41';input.dispatchEvent(new Event('input',{bubbles:true}));const text=document.querySelector('[data-projection-contributed]').textContent;return {ok:text.includes('74') && document.querySelector('[data-projection-warning]').textContent.includes('actualisé'),text,warning:document.querySelector('[data-projection-warning]').textContent,invalid:[...document.querySelector('[data-projection]').elements].filter(x=>x.validity&&!x.validity.valid).map(x=>({name:x.name,value:x.value,message:x.validationMessage}))};})()`, returnByValue:true });
        if(!live.result.value.ok) throw new Error('Live projection for 41 years failed: '+JSON.stringify(live.result.value));
      }
      if (route === '/') {
        const anchors = await send('Runtime.evaluate', { expression: `(()=>{const link=document.querySelector('.heading-actions a');const url=new URL(link.href);return url.hash==='#ajouter' && ['personnel','couple'].includes(url.searchParams.get('espace'));})()`, returnByValue:true });
        if(!anchors.result.value) throw new Error('Account creation link lost its space or form anchor');
      }
      if (route === '/') {
        const interaction = await send('Runtime.evaluate', { expression: `(() => {
          if(document.querySelector('[data-period-select]').value !== '6m') throw new Error('Default period mismatch');
          const chart=document.querySelector('[data-chart].charts-ready');
          if(!chart) throw new Error('Chart enhancement missing');
          const svg=chart.querySelector('[data-chart-svg]'), points=[...chart.querySelectorAll('.chart-datum')];
          svg.dispatchEvent(new KeyboardEvent('keydown',{key:'Home',bubbles:true}));
          if(chart.querySelector('[data-chart-date]').textContent!==points[0].dataset.label) throw new Error('Keyboard cursor mismatch');
          svg.dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}));
          if(chart.querySelector('[data-readout-value="0"]').textContent!==points.at(-1).dataset.value0) throw new Error('Exact value mismatch');
          const rect=svg.getBoundingClientRect();
          svg.dispatchEvent(new PointerEvent('pointerdown',{clientX:rect.left,clientY:rect.top+50,pointerType:'touch',bubbles:true}));
          if(chart.querySelector('[data-chart-date]').textContent!==points[0].dataset.label) throw new Error('Touch cursor mismatch');
          const toggles=[...chart.querySelectorAll('[data-series-toggle]')];
          if(toggles.length>1){toggles[0].click();if(!chart.querySelector('[data-chart-series="0"]').hasAttribute('hidden'))throw new Error('Series toggle failed');toggles[0].click();}
          const select=document.querySelector('[data-period-select]');select.value='custom';select.dispatchEvent(new Event('change'));if(document.querySelector('[data-custom-dates]').hidden)throw new Error('Custom dates missing');select.value='6m';select.dispatchEvent(new Event('change'));
          svg.dispatchEvent(new Event('pointerleave'));return true;
        })()`, returnByValue: true });
        if(interaction.exceptionDetails || !interaction.result.value) throw new Error('Dashboard interaction failed: '+JSON.stringify(interaction));
        const exported = await send('Runtime.evaluate', { expression: `(async()=>{
          const original=URL.createObjectURL;let captured;
          URL.createObjectURL=blob=>{captured=blob;return original(blob)};
          try{document.querySelector('[data-download-chart]').click();const text=await captured.text();const doc=new DOMParser().parseFromString(text,'image/svg+xml');
          if(doc.querySelector('parsererror,script,.chart-datum,.chart-cursor')||doc.documentElement.getAttribute('viewBox')!=='0 0 980 286'||!doc.querySelector('text'))throw new Error('Invalid SVG export');return true;}finally{URL.createObjectURL=original}
        })()`, awaitPromise:true, returnByValue:true });
        if(exported.exceptionDetails || !exported.result.value) throw new Error('SVG export failed: '+JSON.stringify(exported));
      }
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
