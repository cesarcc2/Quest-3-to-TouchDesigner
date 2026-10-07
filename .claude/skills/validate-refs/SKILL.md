---
name: validate-refs
description: Check that C# namespaces (Klak.Ndi, Meta.XR.*, Oculus, URP) used in Assets/Scripts are backed by packages in Packages/manifest.json, and surface Unity compile errors. Use after editing C# or package files.
---
1. Run `bash .claude/skills/validate-refs/validate.sh`.
2. Report only MISSING or ERROR lines. If everything passes, reply with a single line.
3. Fix a MISSING line by adding the package to `Packages/manifest.json`, not by removing the `using`. Before changing code, confirm the API in the package source under `Library/PackageCache/<pkg>` with a targeted grep (don't read whole files).
