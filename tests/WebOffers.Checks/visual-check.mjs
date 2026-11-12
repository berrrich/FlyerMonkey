// Run from the repository root after exporting --visual-fixture.
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createRequire } from 'node:module';
// Node 22+ has WebSocket built in; Node 20 needs --expose-internals.
const WebSocket = globalThis.WebSocket ?? createRequire(import.meta.url)('internal/deps/undici/undici').WebSocket;
const output = resolve('.artifacts/mission010a-visual');
const label = process.argv[2] ?? 'fixture';
const expected = JSON.parse(await readFile(`${output}/expected.json`, 'utf8'));
const server = createServer(async (req, res) => {
  try {
    const image = req.url.includes('product-placeholder.png');
    const data = await readFile(image ? resolve('FlyerMonkey/FlyerMonkey.Shared/wwwroot/images/product-placeholder.png') : `${output}/offers.html`);
    res.setHeader('Content-Type', image ? 'image/png' : 'text/html; charset=utf-8');
    res.end(data);
  } catch { res.writeHead(404); res.end(); }
});
await new Promise(r => server.listen(0, '127.0.0.1', r));
const browser = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', [
  '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${output}/chrome-profile`, '--no-first-run', '--no-default-browser-check', 'about:blank'
], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
let socket;
try {
  const endpoint = await new Promise((done, reject) => {
    let stderr = '';
    browser.stderr.on('data', data => { stderr += data; const match = stderr.match(/DevTools listening on (ws:\/\/[^\s]+)/); if (match) done(match[1]); });
    browser.on('error', reject);
    browser.on('exit', code => reject(new Error(`Chrome exited ${code}: ${stderr}`)));
  });
  const debugOrigin = endpoint.replace('ws:', 'http:').split('/devtools/')[0];
  const target = await (await fetch(`${debugOrigin}/json/new?about:blank`, {method: 'PUT'})).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise(r => socket.addEventListener('open', r, { once: true }));
  let id = 0;
  const pending = new Map();
  socket.addEventListener('message', event => { const m = JSON.parse(event.data); if (pending.has(m.id)) { const {done, reject} = pending.get(m.id); pending.delete(m.id); m.error ? reject(new Error(JSON.stringify(m.error))) : done(m.result); } });
  const send = (method, params = {}, sessionId) => new Promise((done, reject) => {
    const messageId = ++id;
    const timeout = setTimeout(() => reject(new Error(`Timed out: ${method}`)), 15000);
    pending.set(messageId, {done: result => { clearTimeout(timeout); done(result); }, reject: error => { clearTimeout(timeout); reject(error); }});
    socket.send(JSON.stringify({id: messageId, method, params, sessionId}));
  });
  const call = (method, params) => send(method, params);
  await call('Page.enable');
  const reports = [];
  for (const width of [1920, 900, 390]) {
    await call('Emulation.setDeviceMetricsOverride', {width, height: 1100, deviceScaleFactor: 1, mobile: false});
    await call('Page.navigate', {url: label==='compact' ? 'http://127.0.0.1:8842/' : `http://127.0.0.1:${server.address().port}/`});
    await new Promise(r => setTimeout(r, 1800));
    const {result} = await call('Runtime.evaluate', {awaitPromise: true, returnByValue: true, expression: `(async () => {
      await document.fonts.ready;
      const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height};};
      const visible=e=>e.getClientRects().length>0;
      const errors=[];
      const all=[...document.querySelectorAll('.deal')];
      const catalogs=[...document.querySelectorAll('.retailer')].filter(visible);
      const cards=all.filter(visible).map(card=>{
        const unit=rect(card.querySelector(".unit-price")),priceRow=rect(card.querySelector(".price-row")),badge=card.querySelector(".promotion");
        const bounds=rect(card),image=rect(card.querySelector('.product-image')),copy=rect(card.querySelector('.product-name')),price=rect(card.querySelector('.price')),button=rect(card.querySelector('.view-offer'));
        if(image.right>copy.x || price.y<copy.bottom-1 || button.y<price.bottom-1 || button.bottom>bounds.bottom || button.right>bounds.right) errors.push('Overlapping or overflowing card');
        if([...card.querySelectorAll('*')].some(e=>['absolute','fixed'].includes(getComputedStyle(e).position))) errors.push('Positioned card content');
        if(badge) {
          const style=getComputedStyle(badge);
          if(style.fontSize!=='13.6px' || style.padding!=='4px 8px' || style.fontWeight!=='700') errors.push('Badge styling changed');
        }
        if(unit.y<priceRow.bottom-1 || button.y<unit.bottom-1) errors.push("Unit spacing");
        if(Math.abs(price.x-copy.x)>1 || (badge && Math.abs(rect(badge).right-priceRow.right)>1)) errors.push("Price/badge horizontal alignment");
                const slots=getComputedStyle(card).gridTemplateRows.split(' ');
        if(slots.length!==5 || slots.join(' ')!==getComputedStyle(all.find(visible)).gridTemplateRows) errors.push('Content slots differ');
        if(getComputedStyle(card).overflow!=='visible') errors.push('Card-level overflow clipping');
        const title=card.querySelector('.product-name');
        if(title.title!==title.textContent || title.getAttribute('aria-label')!==title.textContent || getComputedStyle(title).webkitLineClamp!=='2') errors.push('Name truncation/accessibility');
        if(card.querySelector('.retailer-name')) errors.push('Retailer text inside card');
        if(image.width!==(innerWidth<=600?64:72) || image.height!==80) errors.push('Image resized');
        if(badge && (rect(badge).bottom>priceRow.bottom+1 || rect(badge).y<priceRow.y-1 || rect(badge).x<price.right)) errors.push('Badge outside reserved price slot');
        return {slots,unit,priceRow,id:Number(card.querySelector('.view-offer').getAttribute('href').split('/').pop()),bounds,image,copy,price,button};
      });
      if(innerWidth>600) {
        if(catalogs.length!==2 || !catalogs[0].classList.contains('woolworths') || !catalogs[1].classList.contains('coles')) errors.push('Retailer order');
        if(Math.abs(rect(catalogs[0]).y-rect(catalogs[1]).y)>1 || rect(catalogs[0]).right>rect(catalogs[1]).x) errors.push('Retailers not side by side');
        for(const catalog of catalogs) {
          const grid=catalog.querySelector('.retailer-cards'),columns=getComputedStyle(grid).gridTemplateColumns.split(' ').length;
          if(columns!==(innerWidth>=1200?2:1)) errors.push('Wrong cards per retailer row');
          if(getComputedStyle(catalog).backgroundColor!=='rgb(255, 255, 255)') errors.push('Colored retailer gutter');
          if(columns===2) {
            const [a,b]=[...grid.querySelectorAll('.deal')].map(rect);
            let e=document.elementFromPoint((a.right+b.x)/2,a.y+10),color='rgba(0, 0, 0, 0)';
            while(e && color==='rgba(0, 0, 0, 0)') {color=getComputedStyle(e).backgroundColor;e=e.parentElement;}
            if(color!=='rgb(255, 255, 255)') errors.push('Non-white card gap');
          }
        }
        const rows=Object.groupBy(cards,c=>Math.round(c.bounds.y));
        const perRow=innerWidth>=1200?2:1;
        const retailerCards=catalogs.map(c=>[...c.querySelectorAll('.deal')]);
        for(let i=0;i<Math.max(...retailerCards.map(c=>c.length));i+=perRow) {
          const row=retailerCards.flatMap(c=>c.slice(i,i+perRow)).map(rect);
          if(Math.max(...row.map(c=>c.y))-Math.min(...row.map(c=>c.y))>1) errors.push('Card tops not aligned across retailers');
        }
        for(const row of Object.values(rows)) {
          for(const key of ['bottom']) if(Math.max(...row.map(c=>c.bounds[key]))-Math.min(...row.map(c=>c.bounds[key]))>1) errors.push('Card bottoms not aligned');
          if(Math.max(...row.map(c=>c.priceRow.y))-Math.min(...row.map(c=>c.priceRow.y))>1 || Math.max(...row.map(c=>c.unit.y))-Math.min(...row.map(c=>c.unit.y))>1) errors.push('Price/unit rows not aligned');
          if(Math.max(...row.map(c=>c.price.y))-Math.min(...row.map(c=>c.price.y))>1) errors.push('Prices not aligned');
          if(Math.max(...row.map(c=>c.button.bottom))-Math.min(...row.map(c=>c.button.bottom))>1) errors.push('Buttons not aligned');
        }
      } else {
        if(catalogs.length!==1 || !visible(document.querySelector('.retailer-selector'))) errors.push('Phone selector visibility');
      }
            const sample=all.find(visible),tracked=['.price-row','.unit-price','.view-offer'];
      const before=tracked.map(s=>rect(sample.querySelector(s)).y);
      const name=sample.querySelector('.product-name'),oldName=name.textContent;
      name.textContent='Very long product name with many words and a seasonal family multipack description '.repeat(8);
      if(rect(name).height>44 || name.scrollHeight<=name.clientHeight || tracked.some((s,i)=>Math.abs(rect(sample.querySelector(s)).y-before[i])>1)) errors.push('Long name escapes its two-line slot');
      name.textContent=oldName;
      const fields=[...sample.querySelectorAll('.product-name,.pack-category,.price,.promotion,.unit-price')];
      const saved=fields.map(e=>e.innerHTML);
      fields.forEach(e=>e.textContent='');
      if(tracked.some((s,i)=>Math.abs(rect(sample.querySelector(s)).y-before[i])>1)) errors.push('Missing fields shift lower slots');
      fields.forEach((e,i)=>e.innerHTML=saved[i]);
      if(document.documentElement.scrollWidth>innerWidth) errors.push('Horizontal overflow');
      return {width:innerWidth,errors,cards,rendered:all.length,visible:cards.length,allIds:all.map(c=>Number(c.querySelector('.view-offer').getAttribute('href').split('/').pop()))};
    })()`});
    reports.push(result.value);
    if(result.value.rendered!==expected.length || expected.some(id=>!result.value.allIds.includes(id))) result.value.errors.push('Returned offer missing');
    const screenshot = await call('Page.captureScreenshot', {format: 'png', captureBeyondViewport: true});
    await writeFile(`${output}/${label}-${width}.png`, Buffer.from(screenshot.data, 'base64'));
    console.log(`${width}px (${result.value.rendered} cards): ${result.value.errors.length ? [...new Set(result.value.errors)].join(', ') : 'PASS geometry, alignment, white gaps, all links, responsive columns'}`);
    if(width===390 && label==='compact' && !result.value.errors.length) {
      const switched=await call('Runtime.evaluate', {awaitPromise:true,returnByValue:true,expression:`(async()=>{
        const selector=document.querySelector('select');
        selector.value='Coles'; selector.dispatchEvent(new Event('change',{bubbles:true}));
        for(let i=0;i<50;i++) {
          await new Promise(r=>setTimeout(r,100));
          const catalogs=[...document.querySelectorAll('.retailer')].filter(e=>e.getClientRects().length);
          if(catalogs.length===1 && catalogs[0].classList.contains('coles')) return {switched:true,count:catalogs[0].querySelectorAll('.deal').length,overflow:document.documentElement.scrollWidth>innerWidth};
        }
        return {switched:false};
      })()`});
      if(!switched.result.value.switched || switched.result.value.count!==18 || switched.result.value.overflow) result.value.errors.push('Phone catalog switch failed');
      console.log('Phone retailer switch:', JSON.stringify(switched.result.value));
      const switchedImage=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});
      await writeFile(`${output}/compact-390-coles.png`,Buffer.from(switchedImage.data,'base64'));
    }
    if(result.value.errors.length) break; // Acceptance failure: stop and report.
  }
  await writeFile(`${output}/${label}-geometry.json`, JSON.stringify(reports, null, 2));
  if(reports.some(r=>r.errors.length)) process.exitCode=1;
  await send('Browser.close');
} finally {
  socket?.close(); browser.kill(); server.close();
}





