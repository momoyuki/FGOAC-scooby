# compat

The OpenGL compatibility layers for AMD and Intel graphics. The game asks the driver for NVIDIA-only
extensions; a layer in `App\` translates them. Settings > Display, Graphics layer, picks one.

| Layer | Files | Installed as |
| --- | --- | --- |
| Cloud23333's layer, the default | `fgoglcompat.dll`, from his 1.10 update | `App\fgoglcompat.dll` |
| The layer FGOAC scooby shipped with 1.1.0 | `fgoglcompat-1.1.0.dll` | `App\fgoglcompat.dll` |
| fluphus's AMD layer | `amd-shim\opengl32.dll`, `amd-shim\amdcfg\amdOglpSettings.cfg` | `App\opengl32.dll` beside a copy of the system DLL as `App\opengl32real.dll`, and `App\amdcfg\` |

On a game folder that has Cloud23333's latest update his updater installs his layer and his launch
script loads it; the list there offers his layer and fluphus's, and his `App\fgoglcompat.dll` is never
written. On game 11.00 the list offers Off and all three, and the first run of a fresh install on a PC
with no NVIDIA card puts in his layer. An install keeps the layer it has; a layer file in `App\` that is
none of the builds below is left as it is.

fluphus's AMD layer is [fgo-arcade-amd-shim](https://github.com/fluphus/fgo-arcade-amd-shim), MIT
licence (`amd-shim\LICENSE`), taken verbatim from his release Last Encore, built 2026-10-06 from
commit `07c26ad`. It holds the game at 60 FPS; he asks for AMD Wait for Vertical Refresh set to Off,
Unless Application Specifies, and no overlays or other frame limiters. An earlier build this package
shipped is replaced by this one when the launcher starts.

The 1.1.0 layer arrived through the community without a licence file; players report it working on
RX 500, RX 6000 and RX 7600 cards and on desktop Ryzen graphics. Both `fgoglcompat` builds are shipped
as received and are not modified here.

| File | SHA-256 |
| --- | --- |
| `fgoglcompat.dll` (990,720 bytes) | `23f1e11cd820f43cd7d47a86090da4c3f5215f5c1bd0294a381561abe6882ab6` |
| `fgoglcompat-1.1.0.dll` (659,456 bytes) | `4932eebf73d715949b04c74b7cf1ee9dbabcc6887f666f3524850d374156f66c` |
| `amd-shim\opengl32.dll` (674,816 bytes) | `8404d3197e59b0ee2cb044793a07a7324d29d8b07fa72efd3dbd844a71a3205d` |
| `amd-shim\amdcfg\amdOglpSettings.cfg` | `9cfe518d2ed064c856e0a2a8f0f8686f10ebbe63d0d5995117067bd8276066ae` |
| earlier `amd-shim\opengl32.dll` (458,240 bytes, commit `1fbf3e4`, replaced at start) | `a85042d91ea60a3108f28dfad4673265f2e1f2a402b252436e5d5757c368a0f0` |
