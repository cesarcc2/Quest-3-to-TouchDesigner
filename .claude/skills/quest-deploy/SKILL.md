---
name: quest-deploy
description: Build the Android APK in Unity batch mode and install/run it on the connected Quest 3 via adb, then show filtered logs. Use when the user asks to build, deploy, install or test on headset.
---
Preconditions: `/env-check` has no FAIL lines, and Unity Editor is **closed** (batch mode can't open a project that's already open).

1. Build: `bash .claude/skills/quest-deploy/deploy.sh build` (needs `Assets/Editor/BuildScript.cs` with `BuildScript.BuildAndroid`; create it if missing, outputting `Build/Quest3Depth.apk`).
2. Install and launch: `bash .claude/skills/quest-deploy/deploy.sh install`
3. Logs: `bash .claude/skills/quest-deploy/deploy.sh logs` (runs 20 s, filtered to Unity/NDI/depth).

On failure, show only the last ~20 relevant lines. Don't dump the full build log.
