#!/usr/bin/env bash
# Maps namespaces used in Assets/Scripts to required packages; then scans Editor.log for compile errors.
M="Packages/manifest.json"
[ -f "$M" ] || { echo "MISSING $M"; exit 1; }

check() { # namespace-regex package
  if grep -rqE "^\s*using\s+$1" Assets/Scripts 2>/dev/null; then
    grep -q "\"$2\"" "$M" && echo "OK      $1 -> $2" || echo "MISSING $1 needs $2"
  fi
}
check "Klak\.Ndi"            "jp.keijiro.klak.ndi"
check "Meta\.XR"             "com.meta.xr.sdk.core"
check "UnityEngine\.Rendering\.Universal" "com.unity.render-pipelines.universal"
check "UnityEngine\.XR"      "com.unity.xr.openxr"

grep -q '"https://registry.npmjs.com"' "$M" && grep -q '"jp.keijiro"' "$M" \
  && echo "OK      scoped registry jp.keijiro" || echo "MISSING scoped registry npmjs / jp.keijiro"

# Compile errors from newest batch log (Build/*.log) or the editor session (only if Unity has run)
LOG=$(ls -t Build/*.log 2>/dev/null | head -1)
[ -f "$LOG" ] || LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
[ -f "$LOG" ] || LOG="$(cygpath -u "$LOCALAPPDATA" 2>/dev/null)/Unity/Editor/Editor.log"
if [ -f "$LOG" ]; then
  errs=$(grep -E "error CS[0-9]+" "$LOG" | sort -u | tail -15)
  [ -n "$errs" ] && echo "ERROR compile:" && echo "$errs" || echo "OK      no CS errors in $LOG"
else
  echo "INFO    Editor.log not found (open project in Unity once)"
fi
