# Out's Custom Quick Access Bubbles

MelonLoader mods for A Township Tale that customize quick-access bubbles.

- Separate outer, inner, and icon colors per bubble
- Per-bubble size (10% steps on the tablet)
- Six profiles and seven-character color share codes
- Social Tablet UI (requires TabletUILib)
- Scanning for bubbles added by other mods

## Versions

- [`PC/`](PC) - Mono build for the PC game. Place the project in the game's `Mods` folder so the relative reference paths resolve, and build with the .NET Framework compiler. Requires PC `TabletUILib.dll`.
- [`Quest/`](Quest) - IL2CPP build for Quest. See [Quest/README.md](Quest/README.md). Untested builds may have issues; please report logs.

## Not included

Game files, the APK, generated game assemblies, MelonLoader, and TabletUILib are not distributed here. Obtain them from their original sources.

Share codes keep colors only (four bits per channel); they do not include sizes.
