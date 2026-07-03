# Throw Up

An infinite spray paint mod for the game **Schedule I**. 

## Author
* **useridredacted**

## How It Works
The mod uses **MelonLoader** and **Harmony** to hook into the game's drawing logic in `Assembly-CSharp.dll`. 
Rather than trying to freeze the pixel count (which breaks the rendering of paint on the canvas), the mod hooks the update loop of `SpraySurfaceInteraction` and:
1. Dynamically increases the canvas limit multiplier (`PaintedPixelLimitMultiplier`) to `80000f`. This effectively increases the maximum paint limit from 25,000 pixels to **2,000,000,000 pixels** (virtually infinite).
2. Forces the drawing state (`_allowDraw`) to `true` during the update cycles so that drawing is never blocked.
3. Postfix detours the `BaseItemInstance.get_ID` property. If you are holding the "Spray Paint" item, the mod spoofs its ID to `"spraycan"` to bypass the game's native spray can validation checks.

This keeps all drawing visual rendering fully intact so you can paint your pieces normally, while making the paint limit practically infinite and naturally keeping the UI indicator at 100%.

## Requirements
* [MelonLoader](https://melonwiki.xyz/) (v0.6.0+ or v0.7.0+)
* Game: **Schedule I** (on Steam)

## Installation
1. Install MelonLoader on your **Schedule I** game.
2. Build the mod DLL (`ThrowUpMod.dll`) or download a release.
3. Drop the `ThrowUpMod.dll` into your game's `Mods` folder.
4. Run the game.

## Controls
* **Hold `G`** (with spray paint equipped): Raycast forward from your view and slide the preview canvas along the wall surface.
* **Release `G`**: Lock the canvas at the current preview location.
* **Look at the canvas and press `E`**: Enter the spray painting interface naturally.
* **`Backspace`** or **`Delete`** (when looking at a placed canvas): Clear the drawing and remove (hide) the canvas.

## Building from Source
You can compile this mod by running the helper build script `build.ps1` in PowerShell:
```powershell
./build.ps1
```
Or manually run the Roslyn compiler:
```powershell
csc /target:library /out:ThrowUpMod.dll /r:"<PathToGame>\MelonLoader\net6\MelonLoader.dll" /r:"<PathToGame>\MelonLoader\net6\0Harmony.dll" /r:"<PathToGame>\MelonLoader\net6\Il2CppInterop.Runtime.dll" /r:"<PathToGame>\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll" /r:"<PathToGame>\MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll" /r:"<PathToGame>\MelonLoader\Il2CppAssemblies\Il2CppFishNet.Runtime.dll" InfinitePaintMod.cs
```
