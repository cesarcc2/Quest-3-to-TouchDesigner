#!/usr/bin/env bash
# Usage: deploy.sh build|install|logs
set -e
ADB="${ADB:-C:/platform-tools/adb}"
EDITORS="C:/Program Files/Unity/Hub/Editor"
UNITY="$EDITORS/$(ls "$EDITORS" | sort -V | tail -1)/Editor/Unity.exe"
APK="Build/Quest3Depth.apk"
PKG="${PKG:-com.cesarcc2.quest3touchdesignerbridge}"
BLOG="Build/build.log"
# Target the first online device (ignores offline emulators)
[ -n "$ANDROID_SERIAL" ] || export ANDROID_SERIAL=$("$ADB" devices | awk 'NR>1 && $2=="device"{print $1; exit}')

case "$1" in
  build)
    mkdir -p Build
    "$UNITY" -batchmode -quit -nographics -projectPath "$(pwd -W 2>/dev/null || pwd)" \
      -buildTarget Android -executeMethod BuildScript.BuildAndroid -logFile "$BLOG" || true
    if [ -f "$APK" ]; then echo "OK build $(du -h "$APK" | cut -f1)"
    else echo "FAIL build"; grep -E "error|Error|Exception" "$BLOG" | sort -u | tail -20; exit 1; fi ;;
  install)
    "$ADB" install -r -g "$APK" | tail -1
    "$ADB" logcat -c   # fresh buffer so 'logs' shows this run from startup
    "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 && echo "OK launched $PKG" ;;
  logs)
    timeout 20 "$ADB" logcat -s Unity:V DEBUG:E AndroidRuntime:E 2>/dev/null \
      | grep -iE "ndi|depth|error|exception|DepthToNdi" | head -60 || true ;;
  *) echo "usage: $0 build|install|logs"; exit 2 ;;
esac
