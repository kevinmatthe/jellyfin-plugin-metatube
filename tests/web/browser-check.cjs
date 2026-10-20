// Offline Chromium checks against a neutral Jellyfin-shaped fixture. No npm dependencies.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');

const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'tests/fixtures');
const movie = JSON.parse(fs.readFileSync(path.join(fixtures, 'metadata_movie.json'))).data;
const firstPage = JSON.parse(fs.readFileSync(path.join(fixtures, 'metadata_review_page.json'))).data;
const requests = [];
const persisted = new Set();
let sourceCalls = 0;
let failReviews = false;
let delay = 0;
let javdbStatus = null;
const idA = '0123456789abcdef0123456789abcdef';
const idB = '1123456789abcdef0123456789abcdef';
const idC = '2123456789abcdef0123456789abcdef';
const shell = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="/jellyfin/MetaTube/metadata.css"><style>
body{margin:0;background:#181818;color:#eee;font:16px sans-serif}.hide{display:none!important}
.itemDetailPage{padding:16px;max-width:1200px;margin:auto}</style></head><body>
<main id="views"></main><script>
window.fixtureToken='fixture-token';window.ApiClient={accessToken:()=>window.fixtureToken,
serverId:()=> 'fixture-server',getUrl:p=>location.origin+'/jellyfin/'+p};
window.addDetails=(key)=>{const d=document.createElement('div');d.className='itemDetailPage';d.dataset.fixture=key;
const anchor=document.createElement('div');anchor.className='itemDetailsGroup';anchor.textContent='Movie details';d.append(anchor);
document.querySelector('#views').append(d);return d};
</script><script defer src="/jellyfin/MetaTube/metadata.js"></script></body></html>`;
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://fixture');
  if (url.pathname.endsWith('/metadata.js') || url.pathname.endsWith('/metadata.css')) {
    const extension = path.extname(url.pathname);
    res.setHeader('Content-Type', extension === '.js' ? 'application/javascript' : 'text/css');
    res.end(fs.readFileSync(path.join(root, 'Jellyfin.Plugin.MetaTube/Web/metadata' + extension)));
  } else if (/\/MetaTube\/Items\/[^/]+\/Metadata$/.test(url.pathname)) {
    requests.push({ url: url.href, auth: req.headers.authorization });
    if (delay) await new Promise(resolve => setTimeout(resolve, delay));
    const offset = Number(url.searchParams.get('offset'));
    const key = url.pathname + ':' + offset;
    const refresh = url.searchParams.get('refresh') === 'true';
    const cached = persisted.has(key) && !refresh;
    if (!cached) { sourceCalls++; if (!failReviews) persisted.add(key); }
    const review = { ...(offset === 0 ? firstPage.items[0] : { ...firstPage.items[0], id: 'page-2', comment: 'Second neutral review' }), source:'JAVDB' };
    const reviews = failReviews ? null : { ...firstPage, items: [review], offset, has_more: offset === 0, cached };
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify({ ...movie, reviews, review_status: failReviews ? 'unavailable' : 'available',
      review_provider: 'JAVDB', javdb_status: javdbStatus, metadata_status: 'available' }));
  } else { res.setHeader('Content-Type', 'text/html'); res.end(shell); }
});

let browser;
let socket;
let profile;
async function main() {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  profile = fs.mkdtempSync(path.join(os.tmpdir(), 'metatube-browser-'));
  const executable = process.env.CHROMIUM_PATH || '/root/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
  browser = spawn(executable, ['--headless', '--no-sandbox', '--disable-gpu', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe'] });
  const debuggerUrl = await new Promise((resolve, reject) => {
    let output = '';
    browser.once('error', reject);
    browser.once('exit', code => reject(new Error('Chromium exited ' + code + ': ' + output)));
    browser.stderr.on('data', chunk => {
      output += chunk;
      const match = output.match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) resolve(match[1]);
    });
  });
  socket = new WebSocket(debuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0;
  const pending = new Map();
  socket.onmessage = ({data}) => {
    const message = JSON.parse(data);
    const call = pending.get(message.id);
    if (!call) return;
    pending.delete(message.id);
    if (message.error) call.reject(new Error(JSON.stringify(message.error)));
    else call.resolve(message.result);
  };
  function send(method, params = {}, sessionId) {
    return new Promise((resolve, reject) => {
      const id = ++sequence; pending.set(id, {resolve, reject});
      socket.send(JSON.stringify({id, method, params, sessionId}));
    });
  }
  const {targetId} = await send('Target.createTarget', {url:'about:blank'});
  const {sessionId} = await send('Target.attachToTarget', {targetId, flatten:true});
  const call = (method, params) => send(method, params, sessionId);
  async function evaluate(expression) {
    const result = await call('Runtime.evaluate', {expression, returnByValue:true, awaitPromise:true});
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  }
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  async function until(expression, name) {
    for (let i=0;i<100;i++) { if (await evaluate(expression)) { console.log('PASS ' + name); return; } await sleep(50); }
    throw new Error(name + '\nDOM: ' + await evaluate('document.body.innerText'));
  }
  const click = label => evaluate(`Array.from(document.querySelectorAll('.mt-action')).find(b=>b.textContent===${JSON.stringify(label)})?.click()`);
  await call('Page.enable');
  await call('Emulation.setDeviceMetricsOverride', {width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await call('Page.navigate', {url:`http://127.0.0.1:${server.address().port}/jellyfin/web/index.html#!/home`});
  await until('!!window.MetaTubeMetadata', 'script loads under Jellyfin BaseUrl');
  await sleep(350);
  assert.equal(requests.length, 0, 'home page does not request metadata');
  await evaluate(`addDetails('A');location.hash='!/details?id=${idA}&serverId=fixture-server'`);
  await until(`document.querySelectorAll('.mt-review').length===1`, 'entering details automatically loads reviews');
  assert.equal(requests.length,1);
  assert.equal(requests[0].auth,'MediaBrowser Token="fixture-token"');
  assert.equal(await evaluate(`document.querySelectorAll('.mt-metadata script').length`),0);
  assert.match(await evaluate(`document.querySelector('.mt-review-text').textContent`), /<script>/);
  console.log('PASS external review HTML is displayed as text');
  assert.ok(await evaluate(`Array.from(document.querySelectorAll('.mt-counts')).some(node=>node.textContent==='2 likes')`), 'JAVDB reaction counts retain like semantics');
  await evaluate(`document.querySelector('.itemDetailsGroup').replaceChildren(document.createTextNode('Rerendered details'))`);
  await sleep(300); assert.equal(requests.length,1, 'detail rerenders do not repeat requests');
  await click('More reviews');
  await until(`document.querySelectorAll('.mt-review').length===2`, 'explicit pagination appends reviews');
  failReviews=true;
  javdbStatus='rate_limited';
  await click('Refresh reviews');
  await until(`Array.from(document.querySelectorAll('.mt-action')).some(b=>b.textContent==='Retry')`, 'failed review refresh offers retry');
  await until(`document.querySelector('.mt-metadata').textContent.includes('JAVDB has paused requests')`, 'source rate limiting is explained without an automatic retry');
  assert.equal(await evaluate(`document.querySelectorAll('.mt-review').length`),2,'failed refresh retains displayed reviews');
  failReviews=false;
  javdbStatus=null;
  await click('Retry');
  await until(`document.querySelectorAll('.mt-review').length===1 && !document.querySelector('[aria-busy="true"]')`, 'retry recovers failed refresh');
  assert.equal(new URL(requests.at(-1).url).searchParams.get('refresh'),'true','retry preserves refresh intent');
  failReviews=true;
  await click('More reviews');
  await until(`Array.from(document.querySelectorAll('.mt-action')).some(b=>b.textContent==='Retry')`, 'failed next page offers retry');
  assert.equal(await evaluate(`document.querySelectorAll('.mt-review').length`),1);
  failReviews=false;
  await click('Retry');
  await until(`document.querySelectorAll('.mt-review').length===2`, 'retry resumes the failed offset');
  assert.equal(new URL(requests.at(-1).url).searchParams.get('offset'),'10');
  // Jellyfin retains old views in the DOM and hides them after asynchronous navigation.
  await evaluate(`location.hash='!/details?id=${idB}&serverId=fixture-server'`);
  await sleep(250);
  await evaluate(`document.querySelector('[data-fixture="A"]').classList.add('hide');addDetails('B')`);
  await until(`!!document.querySelector('[data-fixture="B"] [data-metatube-panel="${idB}"] .mt-review')`, 'panel moves from retained hidden view into the current visible view');
  assert.equal(await evaluate(`document.querySelectorAll('.mt-metadata').length`),1);
  const callsBeforeReturn=sourceCalls;
  await evaluate(`location.hash='!/home';document.querySelectorAll('.itemDetailPage').forEach(d=>d.classList.add('hide'))`);
  await until(`!document.querySelector('.mt-metadata')`, 'leaving details removes the panel');
  await evaluate(`document.querySelector('[data-fixture="A"]').classList.remove('hide');location.hash='!/details?id=${idA}&serverId=fixture-server'`);
  await until(`!!document.querySelector('.mt-review')`, 'returning to an item reads saved first-page data');
  assert.equal(sourceCalls,callsBeforeReturn,'saved pages avoid another upstream fetch');
  for(const width of [375,1440]) {
    await call('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:width<600});
    assert.equal(await evaluate(`document.documentElement.scrollWidth<=window.innerWidth`),true,'layout fits viewport '+width);
    assert.equal(await evaluate(`Array.from(document.querySelectorAll('.mt-action')).every(b=>b.getBoundingClientRect().height>=44)`),true);
    if (process.env.METATUBE_SCREENSHOT_DIR) {
      const {data} = await call('Page.captureScreenshot', {format:'png',captureBeyondViewport:true});
      fs.mkdirSync(process.env.METATUBE_SCREENSHOT_DIR,{recursive:true});
      fs.writeFileSync(path.join(process.env.METATUBE_SCREENSHOT_DIR,`metadata-${width}.png`),Buffer.from(data,'base64'));
    }
  }
  console.log('PASS mobile and desktop layouts fit and controls have 44px targets');
  delay=600;
  await evaluate(`location.hash='!/details?id=${idC}&serverId=fixture-server'`);
  await sleep(200);
  await evaluate(`location.hash='!/home'`);
  await sleep(850);
  assert.equal(await evaluate(`document.querySelectorAll('.mt-metadata').length`),0,'late results cannot remount previous item');
  delay=0;
  const countBeforeWrongServer=requests.length;
  await evaluate(`location.hash='!/details?id=${idA}&serverId=another-server'`);
  await sleep(400);
  assert.equal(requests.length,countBeforeWrongServer,'a different server route never uses current session');
  console.log('PASS navigation aborts stale results and rejects mismatched server IDs');
  await evaluate(`window.fixtureToken='';location.hash='!/details?id=${idA}&serverId=fixture-server'`);
  await sleep(300);
  assert.equal(await evaluate(`document.querySelectorAll('.mt-metadata').length`),0,'logged-out users do not mount a panel');
  console.log('All offline browser checks passed.');
}
main().catch(error=>{console.error(error);process.exitCode=1;}).finally(async()=>{
  const exited = browser && browser.exitCode===null ? new Promise(resolve=>browser.once('exit',resolve)) : Promise.resolve();
  if(socket?.readyState===WebSocket.OPEN) socket.send(JSON.stringify({id:999999,method:'Browser.close'}));
  else browser?.kill();
  await exited;
  socket?.close(); server.closeAllConnections();
  await new Promise(resolve=>server.close(resolve));
  if(profile) await fs.promises.rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:100});
});
