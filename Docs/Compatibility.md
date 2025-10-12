# Compatibility

DxMessaging is render‑pipeline agnostic (pure C#) and targets Unity 2021.3+. The matrix below summarizes support by Unity version and Render Pipeline.

Unity Version vs Render Pipeline

| Unity | Built‑In RP | URP | HDRP |
| --- | --- | --- | --- |
| 2021.3 LTS | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 2022.3 LTS | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 2023.x | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 6.x | ✅ Compatible | ✅ Compatible | ✅ Compatible |

Notes

- RP‑agnostic: DxMessaging does not depend on rendering APIs; it works equally across Built‑In, URP, and HDRP.
- Minimum version is governed by the package manifest (`unity`: 2021.3). Newer LTS versions are expected to work.
