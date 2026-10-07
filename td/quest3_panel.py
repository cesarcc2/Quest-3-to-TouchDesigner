# Quest 3 Depth panel for TouchDesigner: a Kinect TOP stand-in.
#
# HOW TO USE
#   1. In TouchDesigner, create a Text DAT (Tab > DAT > Text) in your network.
#   2. Paste this whole file into it.
#   3. Right-click the Text DAT and choose "Run Script".
#   4. A component called "quest3" appears. Select it and open its parameters (P key).
#      - Page "Quest 3": check Quest IP, then pulse "Find Quest" to pick the NDI source.
#      - Page "Kinect": the same options as the Kinect TOP (Active, Camera Resolution,
#        Near Depth Mode, Mirror Image, Too Close / Too Far / Unknown Value).
#   5. Wherever a tutorial uses the Kinect TOP's output (Image = Depth), use quest3's output instead.
#   Remember: the timeline must be PLAYING (bottom bar) for anything to update.
#
# Depth values follow the Kinect TOP Depth convention: 0..1 linear, 1.0 = 8.191 m, near = dark.
# Depth Scale 'Detail' (default): the Quest spends all 8-bit levels on Min..Max Depth and this
# panel converts back to Kinect units in 32-bit float, about 4x finer than plain Kinect scale.

QUEST_IP = '192.168.1.151'   # the headset's Wi-Fi IP (also editable later on the panel)
MODELS_DIR = r'E:\Documents\PROJETOS\Programacao\Quest 3 to TouchDesigner\pc\models'
SITE_DIR = r'C:\Users\cesar\AppData\Local\quest3-ai\td-site'   # onnxruntime-directml for Python 3.11

SYNC_CALLBACKS = r'''
# Sends panel parameters to the Quest app over OSC (UDP 9000).
NEAR = (0.2, 1.2)      # Near Depth Mode range in meters (~4 mm steps in Detail mode)
DEFAULT = (0.5, 3.0)   # normal range in meters (~10 mm steps in Detail mode)

def osc(address, value):
    op('oscout1').sendOSC(address, [value])

def send(name):
    p = parent().par
    if name == 'Camerares':       osc('/res', int(p.Camerares.eval()))
    elif name == 'Mirrorimage':   osc('/mirror', int(p.Mirrorimage.eval()))
    elif name == 'Tooclosevalue': osc('/tooclose', float(p.Tooclosevalue.eval()))
    elif name == 'Toofarvalue':   osc('/toofar', float(p.Toofarvalue.eval()))
    elif name == 'Unknownvalue':  osc('/unknown', float(p.Unknownvalue.eval()))
    elif name == 'Fps':           osc('/fps', int(p.Fps.eval()))
    elif name == 'Mindepth':      osc('/min', float(p.Mindepth.eval()))
    elif name == 'Maxdepth':      osc('/max', float(p.Maxdepth.eval()))
    elif name == 'Eye':           osc('/eye', int(p.Eye.eval()))
    elif name == 'Lowpower':      osc('/lowpower', int(p.Lowpower.eval()))
    elif name == 'Depthscale':
        mode = p.Depthscale.eval()
        ai = mode == 'ai'
        osc('/encoding', 2 if mode in ('detail', 'ai') else 0)
        osc('/kinectscale', 1 if mode == 'kinect' else 0)
        osc('/invert', 1)
        osc('/color', 1 if ai else 0)    # color camera only when needed (battery)
        osc('/align', 1 if ai else 0)    # depth resampled to the color camera's view

def sync_all():
    for n in ('Depthscale', 'Camerares', 'Mirrorimage', 'Tooclosevalue', 'Toofarvalue',
              'Unknownvalue', 'Fps', 'Mindepth', 'Maxdepth', 'Eye', 'Lowpower'):
        send(n)
    osc('/status', 0)

def find_quest():
    ndi = op('ndiin1')
    found = [n for n in ndi.par.name.menuNames if 'Quest3_Depth' in n]
    color = [n for n in ndi.par.name.menuNames if 'Quest3_Color' in n]
    if color:
        parent().par.Ndicolor = color[0]
        op('ndiin_color').par.name.expr = ''
        op('ndiin_color').par.name = color[0]     # direct value (no expression to fail)
    if found:
        parent().par.Ndisource = found[0]
        print('quest3: using NDI source', found[0], '| color:', color[0] if color else 'not found (only needed for AI Detail)')
    else:
        print('quest3: no Quest3_Depth source found yet. Is the app running? Try again in a few seconds.')

def onValueChange(par, prev):
    p = parent().par
    if par.name == 'Neardepthmode':
        lo, hi = NEAR if par.eval() else DEFAULT
        p.Mindepth = lo      # triggers their own sends
        p.Maxdepth = hi
    elif par.name in ('Active', 'Questip'):
        if p.Active.eval(): sync_all()
    elif p.Active.eval():
        send(par.name)
        if par.name == 'Depthscale' and par.eval() == 'ai':
            # The color stream only starts now; look for its NDI source a few times while it appears
            for frames in (60, 150, 300, 600):
                run("op('{}').module.find_color()".format(me.path), delayFrames=frames, fromOP=me)
    return

def find_color():
    p = parent().par
    if p.Ndicolor.eval():
        return
    color = [n for n in op('ndiin1').par.name.menuNames if 'Quest3_Color' in n]
    if color:
        p.Ndicolor = color[0]
        op('ndiin_color').par.name.expr = ''
        op('ndiin_color').par.name = color[0]     # direct value (no expression to fail)
        print('quest3: using NDI color source', color[0])

def onPulse(par):
    if par.name == 'Resync':      sync_all()
    elif par.name == 'Findquest': find_quest()
    return
'''

DECODE_GLSL = r'''
// Detail mode decoder: Quest gray codes -> Kinect depth units (meters / 8.191), 32-bit float.
// Codes: 0 unknown, 5 too close, 10 too far, 16..255 = Min..Max. Left-edge black|white patch
// at vertical center calibrates any level shift from NDI's YUV conversion.
uniform vec4 uMode;   // x: 1 = decode Detail, 0 = pass through; y: alpha = valid; z: stretch Min..Max
uniform vec4 uRange;  // x: min m, y: max m
uniform vec4 uVals;   // x: too close, y: too far, z: unknown
out vec4 fragColor;
void main() {
    vec4 c = texture(sTD2DInputs[0], vUV.st);
    if (uMode.x < 0.5) { fragColor = TDOutputSwizzle(c); return; }
    float b = texture(sTD2DInputs[0], vec2(0.0125, 0.5)).r;
    float w = texture(sTD2DInputs[0], vec2(0.0375, 0.5)).r;
    float v = clamp((c.r - b) / max(w - b, 1e-3), 0.0, 1.0) * 255.0;
    float d;
    if (vUV.s < 0.05 && abs(vUV.t - 0.5) < 0.0125) d = uVals.z;   // hide calibration patch
    else if (v < 2.5)  d = uVals.z;
    else if (v < 7.5)  d = uVals.x;
    else if (v < 13.0) d = uVals.y;
    else if (uMode.z > 0.5) d = clamp((v - 16.0) / 239.0, 0.0, 1.0);   // Output Range: Min..Max stretched
    else d = (uRange.x + clamp((v - 16.0) / 239.0, 0.0, 1.0) * (uRange.y - uRange.x)) / 8.191;
    float valid = (v >= 13.0 && !(vUV.s < 0.05 && abs(vUV.t - 0.5) < 0.0125)) ? 1.0 : 0.0;
    fragColor = TDOutputSwizzle(vec4(d, d, d, uMode.y > 0.5 ? valid : 1.0));  // alpha = valid (AI fit only)
}
'''

AI_CALLBACKS = r'''
# AI depth bridge. The worker lives in a standalone Python module (sys.modules['_quest3_ai'])
# built from WORKER_SRC, so TouchDesigner rebuilding this DAT never wipes the running thread's code.
# One worker thread owns at most one DirectML session (two at once can crash TD).
import os, sys, types, zlib
import numpy as np

WORKER_SRC = '#' + """
import os, sys, time, threading, gc
import numpy as np

MEAN = np.array([0.485, 0.456, 0.406], np.float32).reshape(3, 1, 1)
STD = np.array([0.229, 0.224, 0.225], np.float32).reshape(3, 1, 1)
SIZE = 518   # Depth Anything's native input size (multiple of 14)
S = {'want': None, 'site': None, 'thread': None, 'color': None, 'depth': None, 'result': None,
     'a': None, 'b': None, 'status': 'off', 'shown': None, 'fit': '', 'cooks': 0, 'retire': False}
LOCK = threading.Lock()

def _resize(a, h, w, nearest=False):
    # numpy-only resize (bilinear, or nearest for masks); a is (H, W) or (H, W, C)
    H, W = a.shape[:2]
    if (H, W) == (h, w):
        return a.astype(np.float32, copy=False)
    ys = (np.arange(h) + 0.5) * H / h - 0.5
    xs = (np.arange(w) + 0.5) * W / w - 0.5
    if nearest:
        return a[np.ix_(np.clip(np.rint(ys), 0, H - 1).astype(int), np.clip(np.rint(xs), 0, W - 1).astype(int))]
    y0 = np.clip(np.floor(ys).astype(int), 0, H - 1); y1 = np.minimum(y0 + 1, H - 1)
    x0 = np.clip(np.floor(xs).astype(int), 0, W - 1); x1 = np.minimum(x0 + 1, W - 1)
    wy = np.clip(ys - y0, 0, 1).astype(np.float32)[:, None]
    wx = np.clip(xs - x0, 0, 1).astype(np.float32)[None, :]
    if a.ndim == 3:
        wy, wx = wy[..., None], wx[..., None]
    top = a[np.ix_(y0, x0)]; top += (a[np.ix_(y0, x1)] - top) * wx
    bot = a[np.ix_(y1, x0)]; bot += (a[np.ix_(y1, x1)] - bot) * wx
    return (top + (bot - top) * wy).astype(np.float32)

def _fit(disp, depth):
    # Range matching: map the AI's 5th..95th percentile spread onto the Quest's real inverse-depth
    # spread over the same pixels. Unlike per-pixel least squares, this keeps the AI's full detail
    # (no regression flattening when the soft Quest depth only roughly matches the image).
    if not S.get('calibrate', True):
        S['fit'] = 'calibration off'
        S['a'] = None
    elif depth is None:
        S['fit'] = 'no Quest depth frame'
    else:
        depth = _resize(depth, disp.shape[0], disp.shape[1], nearest=True)
        m = depth[::4, ::4, 0] * 8.191
        sel = (depth[::4, ::4, 3] > 0.5) & (m > 0.05)
        x, y = disp[::4, ::4][sel], 1.0 / m[sel]
        S['fit'] = 'valid px %d/%d' % (x.size, sel.size)
        if x.size > 200:
            d_lo, d_hi = np.percentile(x, (5, 95))
            y_lo, y_hi = np.percentile(y, (5, 95))
            if d_hi - d_lo > 1e-6 and y_hi > y_lo:
                a = (y_hi - y_lo) / (d_hi - d_lo)
                b = y_lo - a * d_lo
                if S['a'] is None:
                    S['a'], S['b'] = float(a), float(b)
                else:
                    S['a'] += 0.2 * (a - S['a'])
                    S['b'] += 0.2 * (b - S['b'])
    if S['a'] is None:
        lo, hi = np.percentile(disp, (2, 98))   # uncalibrated: relative depth, encoded < -1
        return (-1.0 - np.clip((disp - lo) / max(hi - lo, 1e-6), 0, 1)).astype(np.float32)
    inv = S['a'] * disp + S['b']
    return np.where(inv > 1e-3, 1.0 / np.maximum(inv, 1e-3), 0.0).astype(np.float32)   # 0 = unknown

def _load(model_path, site_dir):
    if site_dir and site_dir not in sys.path:
        sys.path.append(site_dir)          # appended: TouchDesigner's own numpy wins
    import onnxruntime as ort
    return ort.InferenceSession(model_path, providers=['DmlExecutionProvider', 'CPUExecutionProvider'])

def _wait_for_previous_worker():
    global PREV
    t = PREV.S.get('thread') if PREV is not None else None
    deadline = time.time() + 15
    while t is not None and t.is_alive() and time.time() < deadline and PREV.S.get('status') != 'off':
        time.sleep(0.05)
    PREV = None

def _worker():
    S['status'] = 'waiting for previous model to unload...'
    _wait_for_previous_worker()
    sess, current, frames, t0 = None, None, 0, time.time()
    while True:
        if S['retire']:
            sess = None
            gc.collect()
            S['status'] = 'off'
            return
        want = S['want']
        if want != current:
            if sess is not None:
                S['status'] = 'unloading...'
                sess = None
                gc.collect()
            current = want
            with LOCK:
                S.update(result=None, a=None, b=None, color=None)
            if want:
                S['status'] = 'loading %s...' % os.path.basename(want)
                try:
                    sess = _load(want, S['site'])
                    inp = sess.get_inputs()[0]
                    dtype = np.float16 if 'float16' in inp.type else np.float32
                    S['status'] = 'running: %s on %s, waiting for frames' % (os.path.basename(want), sess.get_providers()[0])
                    frames, t0 = 0, time.time()
                except Exception as e:
                    sess = None
                    S['status'] = 'load failed: %s' % str(e).splitlines()[0][:200]
            else:
                S['status'] = 'off'
        if sess is None:
            time.sleep(0.05)
            continue
        with LOCK:
            color, depth = S['color'], S['depth']
            S['color'] = None
        if color is None:
            time.sleep(0.002)
            continue
        try:
            h, w = color.shape[:2]
            small = _resize(np.flipud(color[:, :, :3]), SIZE, SIZE)            # TD rows are bottom-up
            x = ((np.ascontiguousarray(small.transpose(2, 0, 1)) - MEAN) / STD)[None].astype(dtype)
            disp = sess.run(None, {inp.name: x})[0][0].astype(np.float32)
            disp = np.flipud(_resize(disp, max(14, round(SIZE * h / w)), SIZE))
            meters = _fit(disp, depth)
        except Exception as e:
            S['status'] = 'frame error: %s: %s' % (type(e).__name__, str(e).splitlines()[0][:160])
            time.sleep(0.5)
            continue
        with LOCK:
            if S['want'] == current:
                S['result'] = meters
        frames += 1
        if time.time() - t0 > 2:
            S['status'] = ('running: %s %.0f fps | %s | %s | color %s %.2f-%.2f | ai %.3g-%.3g | m %.2f-%.2f | a=%.3g b=%.3g' % (
                os.path.basename(current), frames / (time.time() - t0),
                'calibrated' if S['a'] is not None else 'UNCALIBRATED', S['fit'],
                'x'.join(map(str, color.shape[:2])), float(color[..., :3].min()), float(color[..., :3].max()),
                float(disp.min()), float(disp.max()), float(meters.min()), float(meters.max()),
                S['a'] or 0, S['b'] or 0))
            frames, t0 = 0, time.time()

def request(model, site_dir):
    S['want'], S['site'] = model, site_dir
    t = S['thread']
    if model and (t is None or not t.is_alive()):
        S['thread'] = threading.Thread(target=_worker, daemon=True, name='quest3-ai')
        S['thread'].start()
"""

# (Re)build the worker module when WORKER_SRC changes; the previous one unloads and retires first.
VERSION = zlib.crc32(WORKER_SRC.encode())
W = sys.modules.get('_quest3_ai')
if W is None or getattr(W, 'VERSION', None) != VERSION:
    prev = W
    if prev is not None:
        prev.S['want'] = None      # older workers unload their session on this
        prev.S['retire'] = True    # and exit their thread
    W = types.ModuleType('_quest3_ai')
    W.VERSION, W.PREV = VERSION, prev
    exec(compile(WORKER_SRC, '<quest3_ai_worker>', 'exec'), W.__dict__)
    sys.modules['_quest3_ai'] = W

def onCook(scriptOp):
    try:
        _cook(scriptOp)
    except Exception as e:
        msg = 'cook error: %s: %s' % (type(e).__name__, str(e).splitlines()[0][:160])
        if W.S['shown'] != msg:
            W.S['shown'] = msg
            scriptOp.parent().par.Aistatus = msg
            print('quest3 AI', msg)
    return

def _cook(scriptOp):
    S = W.S
    p = scriptOp.parent().par
    S['cooks'] += 1
    S['calibrate'] = bool(p.Aicalibrate.eval())
    if p.Depthscale.eval() != 'ai':
        W.request(None, None)
    else:
        W.request(os.path.join(p.Modelsdir.eval(), p.Aimodel.eval() + '.onnx'), p.Sitedir.eval())
        # Direct (non-delayed) reads: delayed=True returned empty frames for the input here; both are small
        col = scriptOp.inputs[0].numpyArray() if scriptOp.inputs else None
        dep = op('decode').numpyArray()
        if col is not None:
            with W.LOCK:
                S['color'], S['depth'] = col, dep
        res = S['result']
        if res is not None:
            if p.Outputrange.eval() == 'stretch':                                      # Min..Max -> 0..1
                lo, hi = float(p.Mindepth.eval()), float(p.Maxdepth.eval())
                valid = np.clip((res - lo) / max(hi - lo, 1e-3), 0.0, 1.0)
            else:
                valid = np.clip(res / 8.191, 0.0, 1.0)                                  # Kinect units
            out = np.where(res > 0, valid,
                  np.where(res <= -1.0, 2.0 + res, p.Unknownvalue.eval())).astype(np.float32)  # uncalibrated preview
            w = int(p.Camerares.eval()); h = w * 3 // 4                      # camera's 4:3 aspect
            out = W._resize(out, h, w)
            scriptOp.copyNumpyArray(np.dstack([out, out, out, np.ones_like(out)]))
    if S['shown'] != S['status']:
        S['shown'] = S['status']
        p.Aistatus = S['status']
    return
'''

START_CALLBACKS = r'''
# Re-sends all settings when the project opens.
def onStart():
    run("op('sync').module.sync_all()", delayFrames=60, fromOP=me)
    return

# A Script TOP only cooks when something pulls it; in AI mode, cook it every frame.
def onFrameStart(frame):
    if parent().par.Depthscale.eval() == 'ai':
        op('ai_depth').cook(force=True)
    return
'''

parentComp = me.parent()
old = parentComp.op('quest3')
if old is not None:
    old.destroy()

c = parentComp.create(baseCOMP, 'quest3')
c.nodeX, c.nodeY = me.nodeX + 250, me.nodeY
c.color = (0.2, 0.45, 0.8)

# ---- Kinect page: same names/labels as the Kinect TOP --------------------------------
k = c.appendCustomPage('Kinect')

def toggle(page, name, label, value):
    p = page.appendToggle(name, label=label)[0]
    p.default = value; p.val = value
    return p

def num(page, name, label, value, lo, hi, clamp=False):
    p = page.appendFloat(name, label=label)[0]
    p.default = value; p.val = value
    p.normMin, p.normMax = lo, hi
    if clamp:
        p.clampMin, p.clampMax = True, True
        p.min, p.max = lo, hi
    return p

def menu(page, name, label, names, labels, value):
    p = page.appendMenu(name, label=label)[0]
    p.menuNames, p.menuLabels = names, labels
    p.default = value; p.val = value
    return p

toggle(k, 'Active', 'Active', True)
menu(k, 'Camerares', 'Camera Resolution',
     ['320', '640', '960', '1280'], ['320x320', '640x640', '960x960', '1280x1280'], '640')
toggle(k, 'Neardepthmode', 'Near Depth Mode', False)
toggle(k, 'Mirrorimage', 'Mirror Image', False)
num(k, 'Tooclosevalue', 'Too Close Value', 0.0, 0, 1, clamp=True)
num(k, 'Toofarvalue', 'Too Far Value', 0.0, 0, 1, clamp=True)
num(k, 'Unknownvalue', 'Unknown Value', 0.0, 0, 1, clamp=True)

# ---- Quest 3 page: connection + extras ----------------------------------------------
q = c.appendCustomPage('Quest 3')
ip = q.appendStr('Questip', label='Quest IP')[0]
ip.default = QUEST_IP; ip.val = QUEST_IP
q.appendStr('Ndisource', label='NDI Source')
q.appendStr('Ndicolor', label='NDI Color Source')
q.appendPulse('Findquest', label='Find Quest')
q.appendPulse('Resync', label='Resync Settings')
menu(q, 'Fps', 'FPS', ['15', '30', '60', '72', '90', '120'],
     ['15', '30', '60', '72', '90', '120'], '60')
num(q, 'Mindepth', 'Min Depth (m)', 0.5, 0, 3, clamp=False)
num(q, 'Maxdepth', 'Max Depth (m)', 3.0, 0.5, 8.191, clamp=False)
menu(q, 'Eye', 'Depth Sensor Eye', ['0', '1'], ['Left', 'Right'], '0')
toggle(q, 'Lowpower', 'Low Power (30 fps, 320px)', False)
menu(q, 'Depthscale', 'Depth Scale', ['detail', 'ai', 'kinect', 'normalized'],
     ['Detail (Kinect units, finer steps in Min..Max)', 'AI Detail (color camera + AI, Kinect units)',
      'Kinect (m / 8.191, coarse)', 'Normalized Min..Max (near white)'], 'detail')

menu(q, 'Outputrange', 'Output Range', ['kinect', 'stretch'],
     ['Kinect units (m / 8.191, for tutorials)', 'Min..Max stretched (Min = black, Max = white)'], 'kinect')

a = c.appendCustomPage('AI')
menu(a, 'Aimodel', 'AI Model', ['da2_base_fp16', 'da2_small_fp16'],
     ['Depth Anything V2 Base (best faces, non-commercial)', 'Depth Anything V2 Small (faster, any use)'],
     'da2_base_fp16')
md = a.appendFolder('Modelsdir', label='Models Folder')[0]
md.default = MODELS_DIR; md.val = MODELS_DIR
sd = a.appendFolder('Sitedir', label='Python Packages Folder')[0]
sd.default = SITE_DIR; sd.val = SITE_DIR
toggle(a, 'Aicalibrate', 'Calibrate to Meters', True)
a.appendStr('Aistatus', label='Status')[0].readOnly = True

# ---- Internals ----------------------------------------------------------------------
ndi = c.create(ndiinTOP, 'ndiin1')
ndi.par.active.expr = 'parent().par.Active'
ndi.par.name.expr = 'parent().par.Ndisource'
ndi.nodeX, ndi.nodeY = 0, 0

decode_src = c.create(textDAT, 'decode_pixel')
decode_src.text = DECODE_GLSL
decode_src.nodeX, decode_src.nodeY = 200, 150

dec = c.create(glslTOP, 'decode')
dec.inputConnectors[0].connect(ndi)
dec.par.pixeldat = 'decode_pixel'
dec.par.format = 'rgba32float'
dec.par.vec0name = 'uMode'
dec.par.vec0valuex.expr = "1 if parent().par.Depthscale.eval() in ('detail', 'ai') else 0"
dec.par.vec0valuey.expr = "1 if parent().par.Depthscale.eval() == 'ai' else 0"
dec.par.vec0valuez.expr = "1 if parent().par.Outputrange.eval() == 'stretch' else 0"
dec.par.vec1name = 'uRange'
dec.par.vec1valuex.expr = 'parent().par.Mindepth'
dec.par.vec1valuey.expr = 'parent().par.Maxdepth'
dec.par.vec2name = 'uVals'
dec.par.vec2valuex.expr = 'parent().par.Tooclosevalue'
dec.par.vec2valuey.expr = 'parent().par.Toofarvalue'
dec.par.vec2valuez.expr = 'parent().par.Unknownvalue'
dec.nodeX, dec.nodeY = 200, 0

# ---- AI Detail branch ----------------------------------------------------------------
ndic = c.create(ndiinTOP, 'ndiin_color')
ndic.par.active.expr = "bool(parent().par.Active.eval()) and parent().par.Depthscale.eval() == 'ai'"
ndic.par.name.expr = 'parent().par.Ndicolor'
ndic.nodeX, ndic.nodeY = 0, 300


ai_src = c.create(textDAT, 'ai_callbacks')
ai_src.text = AI_CALLBACKS
ai_src.nodeX, ai_src.nodeY = 400, 450

ai = c.create(scriptTOP, 'ai_depth')
ai.par.callbacks = 'ai_callbacks'
ai.par.format = 'rgba32float'
color_small = c.create(resolutionTOP, 'color_small')   # GPU downscale to the model's 518x518 input
color_small.inputConnectors[0].connect(ndic)
color_small.par.outputresolution = 'custom'
color_small.par.resolutionw, color_small.par.resolutionh = 518, 518
color_small.nodeX, color_small.nodeY = 200, 300
ai.inputConnectors[0].connect(color_small)
ai.nodeX, ai.nodeY = 400, 300

ai_out = c.create(nullTOP, 'ai_out')          # ai_depth already outputs 4:3 at Camera Resolution
ai_out.inputConnectors[0].connect(ai)
ai_out.nodeX, ai_out.nodeY = 600, 300

sw = c.create(switchTOP, 'select')
sw.inputConnectors[0].connect(dec)
sw.inputConnectors[1].connect(ai_out)
sw.par.index.expr = ("1 if parent().par.Depthscale.eval() == 'ai' and parent().par.Ndicolor.eval() "
                     "else 0")   # show normal depth until the color source is found
sw.nodeX, sw.nodeY = 800, 0

out = c.create(outTOP, 'out1')
out.inputConnectors[0].connect(sw)
out.nodeX, out.nodeY = 1000, 0

osc = c.create(oscoutDAT, 'oscout1')
osc.par.address.expr = 'parent().par.Questip'
osc.par.port = 9000
osc.nodeX, osc.nodeY = 0, -150

status = c.create(oscinCHOP, 'status')   # current values reported by the headset
status.par.port = 9001
status.nodeX, status.nodeY = 200, -150

sync = c.create(parameterexecuteDAT, 'sync')
sync.par.op = '..'
sync.par.pars = '*'
sync.par.valuechange = True
sync.par.onpulse = True
sync.text = SYNC_CALLBACKS
sync.nodeX, sync.nodeY = 0, -300

start = c.create(executeDAT, 'startup')
start.par.start = True
start.par.framestart = True
start.text = START_CALLBACKS
start.nodeX, start.nodeY = 200, -300

for child in c.children:
    child.viewer = True       # show images on the tiles inside the component
c.par.opviewer = './out1'   # show the depth image on the component tile
c.viewer = True

run("op('{}').op('sync').module.find_quest(); op('{}').op('sync').module.sync_all()".format(c.path, c.path),
    delayFrames=120, fromOP=me)
print('quest3 panel created at', c.path)
