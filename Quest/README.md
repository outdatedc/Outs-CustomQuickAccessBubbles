# Custom Quick Access Bubbles - Quest

This is a separate IL2CPP build for A Township Tale Quest
`main-1.7.2.1.42203-BC-58`, using the generated assemblies and MelonLoader
from the supplied APK. Version 1.0.1 fixes a Quest color conversion failure
that prevented settings and profiles from being saved; this fix still needs
headset testing.

## Installation

On the Quest, place the built `CustomQuickAccessBubbles.Quest.dll` and the
Quest version of TabletUILib in the game's MelonLoader `Mods` folder. The
tablet library's assembly name is `TabletUILib`; use a single copy named
`TabletUILib.dll` if it is not already installed. Do not use the PC tablet
library or place this Quest DLL in the PC game's Mods folder. ModBubbles and
TextChat are not required.

For the supplied loader, the Quest folder is:

`Android\data\com.Alta.ATownshipTale\files\MelonLoader\com.Alta.ATownshipTale\Mods`

Fully quit and restart the game after installing. Open the quick-access wheel
and select **Customize Bubbles**. If the share keyboard cannot find its prefab,
open server search once, then try again. Test on a server where client mods
are permitted. If the game fails to load or connect, remove only the Quest
mod DLL and check MelonLoader's log before further testing.

Settings are stored in `UserData\QuickAccessBubbleColors.cfg`; saved profiles
are in `UserData\QuickAccessBubbleProfiles`. Save a profile explicitly before
applying it. Version 1.0.0 could not save while affected by the stripped Unity
method, so changes from that version may need to be recreated.

Share codes use the PC mod's seven-character color format. Colors are rounded
to four bits per channel; white represents an uncustomized part. Codes do not
include bubble sizes or a full profile.

## Building

Install a .NET 8 SDK. Create a reference directory containing the DLLs from
the APK's `assets\MelonLoader\Il2CppAssemblies` folder, the loader and
interop DLLs from `assets\MelonLoader\net8`, and the Quest tablet library
as `TabletUILib.dll`. Do not substitute PC game assemblies.

```powershell
dotnet build .\CustomQuickAccessBubbles.Quest.csproj -c Release "-p:QuestReferences=C:\path\to\quest\references"
```

The DLL is emitted under `bin\Release\net8.0`. The project embeds the PC
mod's `Mods\CustomizeB.png` as its icon. The reference DLLs are build-time
inputs and should not all be copied into the Quest Mods folder.
