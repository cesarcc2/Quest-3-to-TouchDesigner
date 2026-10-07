# Quest 3 → TouchDesigner Depth Bridge

Streams the Meta Quest 3 environment depth map over Wi-Fi via NDI (`Quest3_Depth`) for TouchDesigner, with Kinect-panel style settings you can change from TouchDesigner (OSC) or the headset controllers.

`EnvironmentDepthManager` → `DepthArrayToRT.shader` (one eye, linearized) → runtime RGBA8 render texture → patched KlakNDI `NdiSender` → `NDI In TOP`

## Requirements
- Unity 6000.6.4f1 with Android Build Support (SDK, NDK, OpenJDK)
- Quest 3 in Developer Mode, connected over USB (`adb devices` shows it as `device`)
- PC and Quest on the same LAN subnet, with client/AP isolation off. Quest on 5 GHz or Wi-Fi 6, PC on Ethernet if possible.
- TouchDesigner 2023+ (NDI built in)

## Stack
| Package | Version |
|---|---|
| `com.meta.xr.sdk.core` (registry `npm.developer.oculus.com`) | 207.0.0 |
| `jp.keijiro.klak.ndi` | 2.1.2, **embedded and patched** in `Packages/jp.keijiro.klak.ndi` (see below) |
| `com.unity.xr.meta-openxr` (Meta Quest Session + Occlusion features; required for depth on OpenXR) | 2.6.1 |
| URP / OpenXR / XR Management | 17.6.0 / 1.18.0 / 4.7.0 |

Android ARM64, IL2CPP, Vulkan only, GameActivity, app ID `com.cesarcc2.quest3touchdesignerbridge`.

### KlakNDI patch (`// [Quest3Bridge patch]`)
- **Frame rate and timecode:** stock KlakNDI sends a 0/0 frame rate and a timecode of 0. The patch advertises the real fps and lets NDI generate timecodes.
- **fps limiter:** `NdiSender.frameRate` caps the send rate.

## First-time setup
Close the Unity Editor, then run from the project root:
```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Unity.exe"
"$UNITY" -batchmode -quit -nographics -projectPath . -buildTarget Android \
  -executeMethod ProjectSetup.Run -logFile Build/setup.log
```
This sets up:
- player settings, URP, and OpenXR with the Meta XR, Session and Occlusion features
- `OVRProjectConfig`: passthrough, scene support and experimental features
- the `DepthRenderTexture` asset
- `Assets/Scenes/Main.unity`, with the rig, depth manager, NDI sender, bridge, OSC and controller components

You can also run it from the Editor with **Quest3Bridge ▸ Run Project Setup**.

## Build & deploy
```bash
"$UNITY" -batchmode -quit -nographics -projectPath . -buildTarget Android \
  -executeMethod BuildScript.BuildAndroid -logFile Build/build.log
adb install -r -g Build/Quest3Depth.apk          # -g grants USE_SCENE, so there's no prompt
adb shell monkey -p com.cesarcc2.quest3touchdesignerbridge -c android.intent.category.LAUNCHER 1
adb logcat -s Unity | grep DepthToNdi
```
In Claude Code you can run `/quest-deploy` instead.

- **Keep the headset awake while it's off your head:** `adb shell am broadcast -a com.oculus.vrpowermanager.prox_close`. Undo it with `...automation_disable`.
- **`more than one device/emulator`:** Native Instruments' `NTKDaemon` listens on port 5563, and adb mistakes it for an emulator. Set `ANDROID_SERIAL=<quest serial>` or use `adb -s <serial>`.

## TouchDesigner
1. Make sure the timeline is **playing** (▶ in the bottom bar), or nothing updates.
2. Add an **NDI In TOP** and set **Source Name** to `<QUEST-HOSTNAME> (Quest3_Depth)`.
3. The image is grayscale. With the defaults, near is white (`/invert 1`).
4. To control the app, add an **OSC Out DAT** to `<quest-ip>:9000` and an **OSC In CHOP** on port 9001.

## Following Kinect tutorials (Quest 3 panel)
`td/quest3_panel.py` builds a **`quest3` component** that stands in for the Kinect TOP:
1. In TouchDesigner, add a **Text DAT**, paste the file's contents into it, then right-click ▸ **Run Script**.
2. Select `quest3` and press **P**.
   - **Quest 3 page:** check *Quest IP*, then pulse **Find Quest**.
   - **Kinect page:** the same options as the Kinect TOP.
3. Wherever a tutorial wires `kinect1` (Image = Depth), wire `quest3` instead.

| Kinect TOP | Quest 3 panel |
|---|---|
| Active, Mirror Image, Near Depth Mode | same |
| Too Close / Too Far / Unknown Value | same |
| Camera Resolution | 320–1280, square (the Quest sensor is 320×320 native) |
| Image, Camera Remap, Skeleton | not available (always Depth) |

Depth values match the Kinect TOP's Depth image: **0–1 linear, 1.0 = 8.191 m, near = dark**. A tutorial threshold of 0.2 means about 1.6 m on both devices.

**Output Range ▸ Min..Max stretched** maps *Min Depth* to black and *Max Depth* to white, for high, stable contrast. The default *Kinect units* is dark and flat on purpose, like a real Kinect, so tutorial thresholds match.

**Depth Scale ▸ Detail** is the default. The Quest spends all its 8-bit levels on *Min..Max Depth*, and the panel converts back to Kinect units in 32-bit float. With the default 0.5–3 m that's about 1 cm steps, and Near Depth Mode (0.2–1.2 m) gives about 4 mm. Plain *Kinect* scale gives 3.2 cm steps. Frame the person inside Min..Max: anything outside gets the Too Close / Too Far value.

Detail mode won't match the Kinect's facial detail. The Quest sensor is 320×320 over a wider field of view and is heavily smoothed, so faces show their overall shape and relief but not fine features.

To check calibration, hold your hand 1 m from the headset. The pixel value should be about 0.122. If it's clearly off, set the NDI In TOP's *Input Color Space* inside `quest3` to match.

## AI Detail (color camera + AI depth)
**Depth Scale ▸ AI Detail** on the `quest3` panel gives much finer depth, with faces showing eyes, nose and lips. It works like this:
1. The Quest streams its color camera as `Quest3_Color` (1280×960, 30 fps). It also resamples its real depth to the color camera's viewpoint (`/align 1`), so both images line up pixel for pixel.
2. Inside TouchDesigner, **Depth Anything V2** runs on each color frame through ONNX Runtime + DirectML, which works on AMD, NVIDIA and Intel GPUs. The `ai_depth` Script TOP does this in a background thread.
3. The AI's relative depth is fitted to the Quest's real meters, with outlier rejection and smoothing. The output is in Kinect units (meters / 8.191) in 32-bit float.

**One-time PC setup (already done on this PC):**
```bash
py -3.11 -m pip install --target "%LOCALAPPDATA%\quest3-ai\td-site" onnxruntime-directml "numpy==2.1.2"
# then delete numpy* from td-site, because TouchDesigner ships its own numpy
```
Models go in `pc/models/`: `da2_base_fp16.onnx` and `da2_small_fp16.onnx`, from Hugging Face `onnx-community/depth-anything-v2-{base,small}`. The **AI** page of the panel has the model choice, folders, and a **Status** line.

Measured on an RX 9070 XT (inference only, 518 px): Small fp16 runs at 73 fps and Base fp16 at 45 fps. `pc/bench.py` re-measures this.

**License:** Depth Anything V2 **Base is CC-BY-NC-4.0 (non-commercial)**. Use **Small** (Apache-2.0) for paid work.

**Limits:**
- The AI *estimates* depth from the image, so mirrors, glass, screens and very dark scenes can fool it.
- Meters come from the fit to Quest depth, which is accurate enough for thresholds, not for measurement.
- It adds about 1–2 frames of latency.
- The color camera is only on in AI Detail mode, to save battery.

## Runtime settings (Kinect-panel style)
Settings persist on the headset between launches.

### OSC
Send messages such as `/fps 60`. The Quest replies with every current value as `/quest/<name>` to the sender's IP on port **9001**. Send `/status` to request them. Allow UDP 9001 in the Windows firewall.

| Address | Value | Kinect equivalent |
|---|---|---|
| `/fps` | 1–120. Also picks the lowest display Hz (72/90/120) that covers it | – |
| `/res` | 320 / 640 / 960 / 1280 (square, any 64–2048 accepted) | Camera resolution |
| `/mirror` | 0/1 | Mirror image |
| `/min`, `/max` | meters | Depth range |
| `/near` | 1 = 0.1–1.5 m, 0 = 0.3–5 m | Near depth mode |
| `/tooclose`, `/toofar` | 0–1 output value | Too close / too far value |
| `/unknown` | 0–1 output for "no depth" pixels | Unknown value |
| `/invert` | 1 = near is white | – |
| `/eye` | 0 left / 1 right sensor | – |
| `/encoding` | 0 = 8-bit gray, 1 = 16-bit packed (experimental), 2 = Detail (decoded by the quest3 panel) | – |
| `/lowpower` | 1 = max 30 fps, 320 px, 72 Hz display | – |
| `/color` | 1 = stream the color camera as `Quest3_Color` | – |
| `/colorfps` | color stream fps (default 30) | – |
| `/align` | 1 = depth resampled to the color camera's view | – |
| `/colorflip` | 1 = extra horizontal flip of color only | – |
| `/kinectscale` | 1 = Kinect depth convention (default), 0 = normalized min..max | Depth image scale |

### Headset controllers
| Input | Action |
|---|---|
| A / B | fps up / down |
| X / Y | resolution up / down |
| Right stick ↕ / Left stick ↕ | max / min depth |
| Right stick click | low-power toggle |
| Left stick click | near-mode toggle |
| Both grips | show the settings label |

The label appears for 3 s after each change.

### Decoding depth in TouchDesigner
- **Gray8:** `v = r`. Meters: `min + (invert ? 1 - v : v) * (max - min)`. Each step is `(max - min) / 255`, so narrow the range for more precision. The native sensor is 320×320, so a larger `/res` only upscales.
- **Packed16 (experimental):** NDI's lossy YUV 4:2:2 compression can corrupt the low byte. Put this in a GLSL TOP set to 32-bit float:
  ```glsl
  out vec4 fragColor;
  void main() {
      vec4 c = texture(sTD2DInputs[0], vUV.st);
      float v = (round(c.r * 255.0) * 256.0 + round(c.g * 255.0)) / 65535.0;
      fragColor = TDOutputSwizzle(vec4(v, v, v, c.b)); // b = valid mask
  }
  ```

## Troubleshooting
| Symptom | Fix |
|---|---|
| No source in TD | Same subnet? AP isolation? Firewall? Wait 5–10 s for mDNS discovery. |
| Preview frozen, updates only when toggling Active | The TouchDesigner timeline is paused. Press ▶ in the bottom bar, or spacebar on the network. TOPs only cook while it plays. |
| Source shown, black image | USE_SCENE denied: reinstall with `-g` or grant it in Settings ▸ Privacy ▸ Spatial data. |
| `Environment depth not supported` | Check that `com.unity.xr.meta-openxr` is installed, and that the Session and Occlusion features, passthrough and experimental features are on. Re-run ProjectSetup. |
| Choppy stream | Lower `/fps` or `/res`, or use `/lowpower 1`. Check the Quest is on 5 GHz. |
