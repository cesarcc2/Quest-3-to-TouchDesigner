#!/usr/bin/env bash
# Prints OK/WARN/FAIL lines for the Quest build environment.
EDITORS="C:/Program Files/Unity/Hub/Editor"
ADB="${ADB:-C:/platform-tools/adb}"

ver=$(ls "$EDITORS" 2>/dev/null | sort -V | tail -1)
[ -n "$ver" ] && echo "OK   Unity $ver" || { echo "FAIL Unity not found in $EDITORS"; exit 1; }

AND="$EDITORS/$ver/Editor/Data/PlaybackEngines/AndroidPlayer"
[ -d "$AND" ]          && echo "OK   Android Build Support" || echo "FAIL Android Build Support missing (Unity Hub > Installs > Add modules)"
[ -d "$AND/SDK" ]      && echo "OK   Android SDK"   || echo "WARN Android SDK not in module"
[ -d "$AND/NDK" ]      && echo "OK   Android NDK"   || echo "WARN Android NDK not in module"
[ -d "$AND/OpenJDK" ]  && echo "OK   OpenJDK"       || echo "WARN OpenJDK not in module"

if [ -x "$ADB" ] || [ -f "$ADB.exe" ]; then
  echo "OK   adb $("$ADB" version | head -1 | awk '{print $NF}')"
  dev=$("$ADB" devices | awk 'NR>1 && $2=="device"{print $1}')
  [ -n "$dev" ] && echo "OK   device $dev ($("$ADB" -s "$dev" shell getprop ro.product.model | tr -d '\r'))" \
                || echo "WARN no authorized device (enable dev mode, USB debugging, accept prompt in headset)"
else
  echo "FAIL adb not found at $ADB"
fi
