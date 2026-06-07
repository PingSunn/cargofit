'use strict';
/* CargoFit web product editor — vanilla JS, no build step.
   Round-trips products.json with full fidelity (mirrors LayerPatternEditor.ToSection). */

const COLOR_NORMAL = '#3b82f6';
const COLOR_ROTATED = '#f97316';

// ── State ──────────────────────────────────────────────────────────────────
const state = {
  products: [],     // internal editor model (see parseProduct)
  index: -1,        // selected product
  handle: null,     // FileSystemFileHandle (FS Access path)
  canWrite: false,  // true once a handle is open (Save writes back)
};

const supportsFS = typeof window.showOpenFilePicker === 'function';

// ── Tiny DOM helper ────────────────────────────────────────────────────────
function el(tag, props, ...kids) {
  const n = document.createElement(tag);
  if (props) for (const k in props) {
    if (props[k] == null || props[k] === false) continue;
    if (k === 'class') n.className = props[k];
    else if (k === 'dataset') Object.assign(n.dataset, props[k]);
    else if (k.startsWith('on')) n.addEventListener(k.slice(2), props[k]);
    else if (k === 'html') n.innerHTML = props[k];
    else n.setAttribute(k, props[k]);
  }
  for (const c of kids) if (c != null) n.append(c.nodeType ? c : document.createTextNode(c));
  return n;
}
const $ = (id) => document.getElementById(id);

// ── Model conversion (JSON ⇄ editor) ───────────────────────────────────────
function parseSection(j) {
  if (j && j.Pinwheel === true)
    return { pinwheel: true, subRows: [{ rows: 2, cols: 4, rotated: false }] };
  if (j && Array.isArray(j.SubRows) && j.SubRows.length > 0)
    return { pinwheel: false, subRows: j.SubRows.map(s => ({ rows: s.Rows | 0, cols: s.Cols | 0, rotated: !!s.Rotated })) };
  return { pinwheel: false, subRows: [{ rows: (j?.Rows) | 0, cols: (j?.Cols) | 0, rotated: !!(j?.Rotated) }] };
}

// Mirrors LayerPatternEditor.SectionRow.ToSection() exactly so desktop reads it back cleanly.
function serializeSection(s) {
  if (s.pinwheel) return { Rows: 0, Cols: 0, Rotated: false, SubRows: null, Pinwheel: true };
  if (s.subRows.length === 1) {
    const r = s.subRows[0];
    return { Rows: r.rows, Cols: r.cols, Rotated: r.rotated, SubRows: null };
  }
  return {
    Rows: 0, Cols: 0, Rotated: false,
    SubRows: s.subRows.map(r => ({ Rows: r.rows, Cols: r.cols, Rotated: r.rotated })),
  };
}

const KNOWN_KEYS = new Set(['Description', 'Content', 'PackSize', 'WeightPerBoxKg',
  'W', 'L', 'H', 'PatternA', 'PatternB', 'MaxLayers', 'CondoCount']);

function parseProduct(j) {
  const extra = {};
  for (const k in j) if (!KNOWN_KEYS.has(k)) extra[k] = j[k];
  return {
    Description: j.Description ?? '',
    Content: j.Content ?? '',
    PackSize: j.PackSize ?? '',
    WeightPerBoxKg: +j.WeightPerBoxKg || 0,
    W: +j.W || 0, L: +j.L || 0, H: +j.H || 0,
    MaxLayers: j.MaxLayers | 0,
    CondoCount: j.CondoCount | 0,
    A: Array.isArray(j.PatternA) ? j.PatternA.map(parseSection) : [],
    B: Array.isArray(j.PatternB) ? j.PatternB.map(parseSection) : [],
    _extra: Object.keys(extra).length ? extra : null,
  };
}

function serializeProduct(p) {
  const out = {
    Description: p.Description ?? '',
    Content: p.Content ?? '',
    PackSize: p.PackSize ?? '',
    WeightPerBoxKg: +p.WeightPerBoxKg || 0,
    W: +p.W || 0, L: +p.L || 0, H: +p.H || 0,
    PatternA: p.A.map(serializeSection),
    PatternB: p.B.map(serializeSection),
    MaxLayers: p.MaxLayers | 0,
    CondoCount: p.CondoCount | 0,
  };
  if (p._extra) for (const k in p._extra) if (!(k in out)) out[k] = p._extra[k];
  return out;
}

function serializeAll() {
  return JSON.stringify(state.products.map(serializeProduct), null, 2);
}

// ── Layer geometry (compose sections left-to-right) ────────────────────────
// Returns { boxes:[{x,y,w,d,rotated,sec,sub}], width, depth }. Mirrors the
// desktop preview: cols→X, rows→Y within a section; sections placed along X.
function buildLayer(pattern, W, L) {
  const boxes = [];
  let xOff = 0, maxDepth = 0;
  pattern.forEach((s, si) => {
    if (s.pinwheel) {
      const side = W + L;
      const motif = [
        { dx: 0, dy: 0, rot: true }, { dx: L, dy: 0, rot: false },
        { dx: W, dy: L, rot: true }, { dx: 0, dy: W, rot: false },
      ];
      for (const m of motif) {
        const bw = m.rot ? L : W, bd = m.rot ? W : L;
        boxes.push({ x: xOff + m.dx, y: m.dy, w: bw, d: bd, rotated: m.rot, sec: si, sub: -1 });
      }
      xOff += side; maxDepth = Math.max(maxDepth, side);
      return;
    }
    let secW = 0, drawY = 0;
    for (const r of s.subRows) {
      const bw = r.rotated ? L : W;
      secW = Math.max(secW, r.cols * bw);
    }
    s.subRows.forEach((r, ri) => {
      const bw = r.rotated ? L : W, bd = r.rotated ? W : L;
      for (let rr = 0; rr < r.rows; rr++)
        for (let cc = 0; cc < r.cols; cc++)
          boxes.push({ x: xOff + cc * bw, y: drawY + rr * bd, w: bw, d: bd, rotated: r.rotated, sec: si, sub: ri });
      drawY += r.rows * bd;
    });
    xOff += secW; maxDepth = Math.max(maxDepth, drawY);
  });
  return { boxes, width: xOff, depth: maxDepth };
}

// ── 2D SVG preview (click box to rotate its sub-row) ───────────────────────
const SVG_NS = 'http://www.w3.org/2000/svg';
function render2D(host, patKey) {
  const p = state.products[state.index];
  const pattern = p[patKey];
  host.replaceChildren();
  const layer = buildLayer(pattern, p.W || 1, p.L || 1);
  if (!layer.boxes.length) { host.append(el('div', { class: 'none' }, 'ยังไม่มีกล่องใน pattern นี้')); return; }

  const pad = 2;
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', `${-pad} ${-pad} ${layer.width + pad * 2} ${layer.depth + pad * 2}`);
  svg.setAttribute('width', Math.min(layer.width * 4, 520));

  for (const b of layer.boxes) {
    const r = document.createElementNS(SVG_NS, 'rect');
    r.setAttribute('x', b.x + 0.4); r.setAttribute('y', b.y + 0.4);
    r.setAttribute('width', Math.max(b.w - 0.8, 0.1)); r.setAttribute('height', Math.max(b.d - 0.8, 0.1));
    r.setAttribute('rx', 0.8);
    r.setAttribute('fill', b.rotated ? COLOR_ROTATED : COLOR_NORMAL);
    r.setAttribute('stroke', '#fff'); r.setAttribute('stroke-width', 0.4);
    if (b.sub >= 0) {
      r.style.cursor = 'pointer';
      r.addEventListener('click', () => {
        pattern[b.sec].subRows[b.sub].rotated = !pattern[b.sec].subRows[b.sub].rotated;
        refreshPreviews();
      });
    }
    svg.append(r);
  }
  host.append(svg);
}

// ── 3D stack preview (three.js, hand-rolled orbit) ─────────────────────────
const three = {
  renderer: null, scene: null, camera: null, group: null,
  target: new THREE.Vector3(), radius: 600, theta: 0.9, phi: 1.0, need: true,
};

function init3D() {
  const host = $('prev3d');
  const r = new THREE.WebGLRenderer({ antialias: true });
  r.setPixelRatio(window.devicePixelRatio || 1);
  r.setClearColor(0x0f172a, 1);
  host.append(r.domElement);
  // setSize(..., false) keeps the draw buffer at w×dpr but sets no CSS size, so the
  // canvas would otherwise lay out at buffer-pixel width (2× on retina) and overflow
  // the page. Pin its display size to the container; the buffer stays high-res.
  Object.assign(r.domElement.style, { display: 'block', width: '100%', height: '100%' });
  const scene = new THREE.Scene();
  const cam = new THREE.PerspectiveCamera(45, 1, 1, 20000);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x334155, 0.9));
  const dir = new THREE.DirectionalLight(0xffffff, 0.7); dir.position.set(1, 2, 1.3); scene.add(dir);
  three.renderer = r; three.scene = scene; three.camera = cam;

  // hand-rolled orbit
  let dragging = false, px = 0, py = 0;
  host.addEventListener('pointerdown', e => { dragging = true; px = e.clientX; py = e.clientY; host.setPointerCapture(e.pointerId); });
  host.addEventListener('pointermove', e => {
    if (!dragging) return;
    three.theta -= (e.clientX - px) * 0.01;
    three.phi = Math.max(0.15, Math.min(Math.PI - 0.15, three.phi - (e.clientY - py) * 0.01));
    px = e.clientX; py = e.clientY; three.need = true;
  });
  const stop = () => { dragging = false; };
  host.addEventListener('pointerup', stop); host.addEventListener('pointercancel', stop);
  host.addEventListener('wheel', e => {
    e.preventDefault();
    three.radius = Math.max(80, Math.min(8000, three.radius * (1 + Math.sign(e.deltaY) * 0.1)));
    three.need = true;
  }, { passive: false });

  new ResizeObserver(() => { resize3D(); three.need = true; }).observe(host);
  resize3D();

  function loop() {
    if (three.need) { updateCamera(); r.render(scene, cam); three.need = false; }
    requestAnimationFrame(loop);
  }
  loop();
}

function resize3D() {
  const host = $('prev3d');
  const w = host.clientWidth || 600, h = host.clientHeight || 340;
  three.renderer.setSize(w, h, false);
  three.camera.aspect = w / h; three.camera.updateProjectionMatrix();
}

function updateCamera() {
  const { radius: R, theta, phi, target } = three;
  three.camera.position.set(
    target.x + R * Math.sin(phi) * Math.cos(theta),
    target.y + R * Math.cos(phi),
    target.z + R * Math.sin(phi) * Math.sin(theta));
  three.camera.lookAt(target);
}

function render3D() {
  if (!three.renderer) return;
  const p = state.products[state.index];
  if (three.group) { three.scene.remove(three.group); disposeGroup(three.group); }
  const group = new THREE.Group();
  three.group = group; three.scene.add(group);

  const W = p.W || 1, L = p.L || 1, H = p.H || 1;
  const layers = +$('layerSlider').value;
  const matN = new THREE.MeshStandardMaterial({ color: COLOR_NORMAL, roughness: 0.65 });
  const matR = new THREE.MeshStandardMaterial({ color: COLOR_ROTATED, roughness: 0.65 });
  const edgeMat = new THREE.LineBasicMaterial({ color: 0x0f172a, transparent: true, opacity: 0.35 });

  let totalW = 0, totalD = 0;
  for (let li = 0; li < layers; li++) {
    const pattern = (li % 2 === 0) ? p.A : p.B;
    const layer = buildLayer(pattern, W, L);
    if (!layer.boxes.length) continue;
    totalW = Math.max(totalW, layer.width); totalD = Math.max(totalD, layer.depth);
    for (const b of layer.boxes) {
      const geo = new THREE.BoxGeometry(Math.max(b.w - 0.5, 0.1), H - 0.5, Math.max(b.d - 0.5, 0.1));
      const mesh = new THREE.Mesh(geo, b.rotated ? matR : matN);
      // world: footprint x→X, footprint y(depth)→Z, layer height→Y(up)
      mesh.position.set(b.x + b.w / 2, li * H + H / 2, b.y + b.d / 2);
      group.add(mesh);
      group.add(new THREE.LineSegments(new THREE.EdgesGeometry(geo), edgeMat).translateX(mesh.position.x).translateY(mesh.position.y).translateZ(mesh.position.z));
    }
  }
  // centre the model
  group.position.set(-totalW / 2, 0, -totalD / 2);
  three.target.set(0, (layers * H) / 2, 0);
  three.radius = Math.max(totalW, totalD, layers * H) * 1.8 + 150;
  three.need = true;
}

function disposeGroup(g) {
  g.traverse(o => { if (o.geometry) o.geometry.dispose(); });
}

// ── Editor rendering ───────────────────────────────────────────────────────
function spin(value, min, onChange) {
  const valEl = el('span', { class: 'val' }, String(value));
  const dec = el('button', { type: 'button', onclick: () => set(value - 1) }, '−');
  const inc = el('button', { type: 'button', onclick: () => set(value + 1) }, '+');
  function set(v) { value = Math.max(min, v); valEl.textContent = String(value); onChange(value); }
  return el('span', { class: 'spin' }, dec, valEl, inc);
}

function renderPattern(patKey) {
  const host = $('pattern' + patKey);
  const p = state.products[state.index];
  const pattern = p[patKey];
  host.replaceChildren();

  pattern.forEach((sec, si) => {
    const head = el('div', { class: 'section-head' },
      el('span', { class: 'stitle' }, `Section ${si + 1}`),
      el('div', { class: 'sbtns' },
        el('button', {
          class: 'btn ghost sm', type: 'button',
          onclick: () => { sec.pinwheel = !sec.pinwheel; renderPattern(patKey); refreshPreviews(); }
        }, sec.pinwheel ? '▦ กังหัน: เปิด' : '▦ กังหัน: ปิด'),
        el('button', {
          class: 'btn danger sm', type: 'button',
          onclick: () => { pattern.splice(si, 1); renderPattern(patKey); refreshPreviews(); }
        }, '✕ ลบ'),
      ));

    const body = el('div', { class: 'subrows' });
    if (sec.pinwheel) {
      body.append(el('div', { class: 'pin-badge' }, 'กังหัน 4 กล่อง — footprint (W+L)×(W+L) · ปิดเพื่อกลับเป็นกริด'));
    } else {
      sec.subRows.forEach((sub, ri) => {
        const orient = el('button', {
          class: 'toggle' + (sub.rotated ? ' rot' : ''), type: 'button',
          onclick: () => { sub.rotated = !sub.rotated; renderPattern(patKey); refreshPreviews(); }
        }, sub.rotated ? '↺ หมุน' : '→ ปกติ');
        const del = el('button', {
          class: 'btn danger sm', type: 'button', disabled: sec.subRows.length <= 1 ? '' : null,
          onclick: () => { if (sec.subRows.length > 1) { sec.subRows.splice(ri, 1); renderPattern(patKey); refreshPreviews(); } }
        }, '−');
        body.append(el('div', { class: 'subrow' },
          el('span', { class: 'lab' }, 'แถว'), spin(sub.rows, 1, v => { sub.rows = v; refreshPreviews(); }),
          el('span', { class: 'lab' }, 'คอลัมน์'), spin(sub.cols, 1, v => { sub.cols = v; refreshPreviews(); }),
          orient, del));
      });
      body.append(el('button', {
        class: 'btn ghost sm', type: 'button',
        onclick: () => {
          const cols = sec.subRows[0] ? sec.subRows[0].cols : 2;
          sec.subRows.push({ rows: 1, cols, rotated: false });
          renderPattern(patKey); refreshPreviews();
        }
      }, '+ เพิ่มแถวย่อย'));
    }

    host.append(el('div', { class: 'section' }, head, body));
  });
}

function refreshPreviews() {
  if (state.index < 0) return;
  render2D($('prevA'), 'A');
  render2D($('prevB'), 'B');
  render3D();
}

// ── Product list + selection ───────────────────────────────────────────────
function renderProductList() {
  const host = $('productList');
  const q = $('search').value.trim().toLowerCase();
  host.replaceChildren();
  state.products.forEach((p, i) => {
    const hay = `${p.Description} ${p.Content} ${p.PackSize}`.toLowerCase();
    if (q && !hay.includes(q)) return;
    host.append(el('div', {
      class: 'prod-item' + (i === state.index ? ' active' : ''),
      onclick: () => selectProduct(i),
    },
      el('div', { class: 'pname' }, p.Description || '(ไม่มีชื่อ)'),
      el('div', { class: 'pmeta' }, `${p.Content || ''} · ${p.PackSize || ''}`)));
  });
}

function bindField(id, key, isNum) {
  const inp = $(id);
  inp.oninput = () => {
    const p = state.products[state.index];
    p[key] = isNum ? (+inp.value || 0) : inp.value;
    if (['W', 'L', 'H', 'WeightPerBoxKg'].includes(key)) updateCbm();
    if (['W', 'L', 'H'].includes(key)) refreshPreviews();
  };
}

function updateCbm() {
  const p = state.products[state.index];
  $('cbmHint').textContent = `CBM: ${((p.W * p.L * p.H) / 1e6).toFixed(6)} m³`;
}

function selectProduct(i) {
  state.index = i;
  const p = state.products[i];
  $('emptyState').hidden = true;
  $('editorBody').hidden = false;
  $('f_desc').value = p.Description; $('f_content').value = p.Content; $('f_pack').value = p.PackSize;
  $('f_weight').value = p.WeightPerBoxKg; $('f_w').value = p.W; $('f_l').value = p.L; $('f_h').value = p.H;
  $('f_maxlayers').value = p.MaxLayers; $('f_condo').value = p.CondoCount;
  const slider = $('layerSlider');
  slider.value = p.MaxLayers > 0 ? Math.min(p.MaxLayers, 20) : 6;
  $('layerCount').textContent = slider.value;
  updateCbm();
  renderPattern('A'); renderPattern('B');
  renderProductList();
  refreshPreviews();
}

// ── Loading / file ops ─────────────────────────────────────────────────────
function loadJsonText(text) {
  let data;
  try { data = JSON.parse(text); } catch (e) { return banner('ไฟล์ JSON ไม่ถูกต้อง: ' + e.message, 'err'); }
  if (!Array.isArray(data)) return banner('รูปแบบไฟล์ไม่ใช่ array ของสินค้า', 'err');
  state.products = data.map(parseProduct);
  state.index = -1;
  $('editorBody').hidden = true; $('emptyState').hidden = false;
  renderProductList();
  if (state.products.length) selectProduct(0);
  banner(`โหลด ${state.products.length} สินค้า`, 'ok');
}

async function verifyPermission(handle, write) {
  const opts = { mode: write ? 'readwrite' : 'read' };
  if ((await handle.queryPermission(opts)) === 'granted') return true;
  return (await handle.requestPermission(opts)) === 'granted';
}

async function openFile() {
  if (!supportsFS) { $('importInput').click(); return; }
  try {
    const [handle] = await window.showOpenFilePicker({
      types: [{ description: 'JSON', accept: { 'application/json': ['.json'] } }],
    });
    state.handle = handle; state.canWrite = true;
    await idbSet('handle', handle);
    const file = await handle.getFile();
    loadJsonText(await file.text());
    setFileStatus(handle.name); $('saveBtn').disabled = false;
  } catch (e) { if (e.name !== 'AbortError') banner('เปิดไฟล์ไม่สำเร็จ: ' + e.message, 'err'); }
}

async function reopenStored() {
  try {
    if (!state.handle) return;
    if (!(await verifyPermission(state.handle, true))) return banner('ไม่ได้รับสิทธิ์เข้าถึงไฟล์', 'warn');
    state.canWrite = true;
    const file = await state.handle.getFile();
    loadJsonText(await file.text());
    setFileStatus(state.handle.name); $('saveBtn').disabled = false;
  } catch (e) { banner('เปิดไฟล์เดิมไม่สำเร็จ: ' + e.message, 'err'); }
}

async function save() {
  const text = serializeAll();
  if (state.handle && state.canWrite) {
    try {
      if (!(await verifyPermission(state.handle, true))) return banner('ไม่ได้รับสิทธิ์เขียนไฟล์', 'warn');
      const w = await state.handle.createWritable();
      await w.write(text); await w.close();
      banner('บันทึกแล้ว ✓ (กลับไปที่แอป CargoFit ให้ window focus เพื่อรีโหลด)', 'ok');
    } catch (e) { banner('บันทึกไม่สำเร็จ: ' + e.message, 'err'); }
  } else {
    download(text); banner('ไม่มีไฟล์ที่เปิดไว้ — ดาวน์โหลดเป็น products.json แทน', 'warn');
  }
}

function download(text) {
  const a = el('a', { href: URL.createObjectURL(new Blob([text], { type: 'application/json' })), download: 'products.json' });
  document.body.append(a); a.click(); a.remove();
}

function setFileStatus(name) { $('fileStatus').textContent = '📄 ' + name; }

let bannerTimer;
function banner(msg, kind) {
  const b = $('banner'); b.textContent = msg; b.className = 'banner ' + kind; b.hidden = false;
  clearTimeout(bannerTimer); bannerTimer = setTimeout(() => { b.hidden = true; }, 5000);
}

// ── Minimal IndexedDB key/val (persist the file handle) ────────────────────
function idb() {
  return new Promise((res, rej) => {
    const r = indexedDB.open('cargofit-editor', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('kv');
    r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error);
  });
}
async function idbSet(k, v) { const db = await idb(); return new Promise((res, rej) => { const t = db.transaction('kv', 'readwrite'); t.objectStore('kv').put(v, k); t.oncomplete = res; t.onerror = () => rej(t.error); }); }
async function idbGet(k) { const db = await idb(); return new Promise((res, rej) => { const t = db.transaction('kv', 'readonly'); const q = t.objectStore('kv').get(k); q.onsuccess = () => res(q.result); q.onerror = () => rej(q.error); }); }

// ── Wire up ────────────────────────────────────────────────────────────────
function newProduct() {
  state.products.push(parseProduct({
    Description: 'สินค้าใหม่', Content: '', PackSize: '', WeightPerBoxKg: 0,
    W: 25, L: 38, H: 20, PatternA: [{ Rows: 2, Cols: 4, Rotated: false, SubRows: null }],
    PatternB: [{ Rows: 2, Cols: 4, Rotated: false, SubRows: null }], MaxLayers: 0, CondoCount: 0,
  }));
  renderProductList(); selectProduct(state.products.length - 1);
}

function deleteProduct() {
  if (state.index < 0) return;
  if (!confirm('ลบสินค้านี้?')) return;
  state.products.splice(state.index, 1);
  state.index = -1;
  $('editorBody').hidden = true; $('emptyState').hidden = false;
  renderProductList();
  if (state.products.length) selectProduct(Math.min(state.index < 0 ? 0 : state.index, state.products.length - 1));
}

function init() {
  init3D();
  $('openBtn').onclick = openFile;
  $('saveBtn').onclick = save;
  $('importBtn').onclick = () => $('importInput').click();
  $('importInput').onchange = async (e) => {
    const f = e.target.files[0]; if (!f) return;
    state.handle = null; state.canWrite = false; $('saveBtn').disabled = true;
    loadJsonText(await f.text()); setFileStatus(f.name + ' (อ่านอย่างเดียว — ใช้ ส่งออก เพื่อบันทึก)');
    e.target.value = '';
  };
  $('exportBtn').onclick = () => download(serializeAll());
  $('search').oninput = renderProductList;
  $('addProductBtn').onclick = newProduct;
  $('deleteProductBtn').onclick = deleteProduct;
  ['desc:Description:0', 'content:Content:0', 'pack:PackSize:0', 'weight:WeightPerBoxKg:1',
    'w:W:1', 'l:L:1', 'h:H:1', 'maxlayers:MaxLayers:1', 'condo:CondoCount:1'].forEach(spec => {
      const [id, key, n] = spec.split(':'); bindField('f_' + id, key, n === '1');
    });
  $('layerSlider').oninput = () => { $('layerCount').textContent = $('layerSlider').value; render3D(); };

  // Offer to reopen the last file (handle persisted in IndexedDB).
  idbGet('handle').then(h => {
    if (h) { state.handle = h; banner(`พบไฟล์ล่าสุด: ${h.name} — กด "เปิด products.json" เพื่อโหลด`, 'warn'); $('openBtn').onclick = () => reopenStored().catch(() => openFile()); $('openBtn').textContent = `เปิด ${h.name}`; }
  }).catch(() => { });

  if (!supportsFS) banner('เบราว์เซอร์นี้ไม่รองรับการเขียนไฟล์ตรง — ใช้ นำเข้า/ส่งออก แทน (แนะนำ Chrome/Edge)', 'warn');
}

document.addEventListener('DOMContentLoaded', init);
