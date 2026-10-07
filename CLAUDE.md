# Quest 3 → TouchDesigner Depth Streamer

Unity (URP) app for Meta Quest 3 that grabs the Meta XR environment depth texture, copies it into a
1280x1280 `DepthRenderTexture`, and streams it over Wi-Fi with KlakNDI (`Quest3_Depth`). TouchDesigner receives it with an `NDI In TOP`.

## Environment (verified 2026-10-06)
- Unity: `C:/Program Files/Unity/Hub/Editor/6000.6.4f1` (Unity 6), with Android Build Support (SDK/NDK/JDK) installed. Quest 3 is connected via adb.
- adb: `C:/platform-tools/adb`
- Shells: use Bash for scripts. Paths contain spaces, so always quote them.

## Workflow: plan first, then build
1. **Plan**: for any task that touches more than one file, write a short numbered plan (files, what changes, how you'll verify it). Wait for approval before editing. Keep plans under ~15 lines.
2. **Develop**: do one plan step at a time and make the smallest change that works. Don't refactor without being asked.
3. **Verify**: run `/validate-refs` after any C# or package change, and `/env-check` before builds.
4. **Report**: give a short summary of the files changed, the verification result, and any open risks. Don't paste whole files back.

## Token-economy rules
- Don't re-read files you just wrote. Read with offsets or grep instead of whole files when you can.
- Never read `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, or `*.meta`, and don't run broad globs over them.
- Use the skills' scripts instead of writing ad-hoc commands. Filter command output with `grep`/`tail`.
- Don't spawn subagents unless asked. Use WebSearch only to confirm an API you're unsure of, and search once with a targeted query.
- Keep answers short and skip summaries of things that didn't change.
- Python on Windows defaults to cp1252. Always run with `PYTHONUTF8=1`, and use the Edit/Write tools for files with non-ASCII text (README has ▸ ↕ ²).
- KlakNDI is embedded in `Packages/jp.keijiro.klak.ndi` with `[Quest3Bridge patch]` changes (frame-rate/timecode stamping, fps limiter). Never delete it or swap it for the registry version.

## Deliverables
| File | Purpose |
|---|---|
| `Packages/manifest.json` | Scoped registry `https://registry.npmjs.com` (scope `jp.keijiro`), `jp.keijiro.klak.ndi` 2.1.2, `com.meta.xr.sdk.core`, URP |
| `Assets/Plugins/Android/AndroidManifest.xml` | INTERNET, ACCESS_NETWORK_STATE, ACCESS_WIFI_STATE, CHANGE_WIFI_MULTICAST_STATE, plus the Quest/OpenXR entries |
| `Assets/Scripts/DepthToNdiBridge.cs` | Depth texture → RT → NdiSender lifecycle |
| `Assets/Shaders/DepthArrayToRT.shader` | Blit of one slice of the depth Texture2DArray (needed, see below) |
| `README.md` | Build, `adb` deploy, TouchDesigner setup |

## Technical constraints (don't violate)
- Application ID: `com.cesarcc2.quest3touchdesignerbridge`.
- Player settings: Android, ARM64 only, IL2CPP, Vulkan only, URP, OpenXR + Meta XR feature.
- `OVRManager`: Passthrough enabled, Experimental Features on. Add the `OVRPassthroughLayer` and `EnvironmentDepthManager` components.
- Depth on OpenXR requires `com.unity.xr.meta-openxr` >= 2.1 (defines `OPEN_XR_META_2_1_OR_NEWER`); without it EnvironmentDepthManager reports "not supported".
- Depth API namespace: `Meta.XR.EnvironmentDepth` (`EnvironmentDepthManager`, `IsDepthAvailable`). The depth is a **global shader Texture2DArray** (`_EnvironmentDepthTexture`, slice 0 = left eye). `Graphics.Blit` can't read an array slice directly, so blit through `DepthArrayToRT.shader` with a slice index. Native resolution is far below 1280, so upscaling is expected.
- The app needs the `USE_SCENE` permission (`com.oculus.permission.USE_SCENE`) at runtime, or depth stays unavailable.
- KlakNDI: namespace `Klak.Ndi`; `NdiSender.ndiName`, `captureMethod = CaptureMethod.Texture`, `sourceTexture`, `keepAlpha = false`. The RT format is `R16_UNorm` (fallback `R8_UNorm`). KlakNDI converts to UYVY, so depth arrives in the luma channel.
- Android NDI discovery needs a Wi-Fi `MulticastLock` and the NSD service running. Acquire them through `AndroidJavaObject` in the bridge script. Quest and PC must be on the same subnet, and AP isolation must be off.
- Verify KlakNDI 2.1.2 Android/arm64 support when importing. If the native lib is missing, stop and report instead of working around it.

## TouchDesigner panel (`td/quest3_panel.py`): verified pitfalls
- The user reloads the panel via the Textport one-liner that reads the file into `/project1/text1` and runs it. The DAT keeps its own copy of the text.
- Check TD parameter names against docs.derivative.ca before using them (e.g. OSC Out DAT uses `address`, not `netaddress`).
- Ops created by script have `viewer` off, so tiles show text. The builder sets `child.viewer = True`.
- **Don't** resize with a Null TOP (it ignores resolution) or use a Resolution TOP on the AI output (it broke the image). Resize the AI output in numpy. A Resolution TOP is fine for color→518 input.
- Inside the Script TOP, `numpyArray(delayed=True)` returned empty frames for its input. Use direct `numpyArray()`.
- AI worker = standalone module `sys.modules['_quest3_ai']` built from `WORKER_SRC` (TD wipes DAT globals on rebuild). It holds one DirectML session at a time (two at once crashed TD), and a version hash hands off to the next worker on code change.
- Calibration = percentile range matching (least squares flattened the detail). The AI venv for offline tests is `%LOCALAPPDATA%\quest3-ai\venv`; TD's packages are in `...\td-site`.
- Debug offline: capture NDI frames with cyndilib in the venv and run the panel's `WORKER_SRC` on them.

## Skills (`.claude/skills/`)
- `/env-check`: Unity version, Android module, SDK/NDK, adb, connected Quest.
- `/validate-refs`: checks that `using` namespaces in Assets/Scripts are backed by packages in manifest.json, and greps the Unity Editor.log for compile errors.
- `/quest-deploy`: batch-mode APK build, then `adb install` and a filtered logcat.
