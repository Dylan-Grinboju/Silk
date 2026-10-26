# Loader reliability regression tests

Run the tests with the .NET 8 SDK or newer:

```powershell
dotnet run --project tests/Regression/Regression.csproj
```

The runner compiles the production config, loader, manager, updater, patch, and utility sources with small Unity/game stubs. It uses real YAML parsing, Cecil assembly metadata, filesystem operations, and a loopback HTTP server. Test configs and dummy mod files stay under the runner's ignored `bin` directory. No game installation is needed. The locked-file test runs on Windows; the atomic replacement test runs on every platform.

To compare an unmodified checkout, set `-p:SilkSourceRoot=<absolute-path-to-its-src-directory>`. At upstream commit `fd2739590190a65eeaf2ee7f1bc6f9f01025e7a2`, 3 of the 23 tests pass. With these fixes, all 23 pass.

## Problems addressed

| Problem | Effect on SpiderSurge | Fix |
| --- | --- | --- |
| `Initialize()` used a manually constructed `MonoBehaviour`; the manager created another component later | `SpiderSurgeMod.Instance` differed from the component running `Update()` and `OnApplicationQuit()`; instance fields and Unity object APIs were unreliable | Create one inactive Unity component, initialize it, then activate that same component after all initializers finish |
| Duplicate mod IDs were initialized repeatedly | Extra copies of SpiderSurge could receive frame callbacks even when its own startup guard rejected duplicate initialization | Sort discovery deterministically and attempt each compatible mod ID once; warn with the extra file's path |
| Mods were registered before initialization succeeded | A failed mod could still receive Unity callbacks and incorrectly mark the session as gameplay-modded | Register only successful initializers, destroy failed inactive components, and set the gameplay flag only on success |
| Failure announcements dereferenced an announcer that did not yet exist | One failed mod could stop Silk before its patches and Mods menu were installed | Skip in-game announcements until an announcer exists; failure details remain in the log |
| Nested config defaults were shared with mutable live configs | Changing a setting could change the defaults or another config using the same template | Clone nested YAML dictionaries and lists on every load and merge |
| Malformed YAML was either fatal to Silk or silently replaced by later mod saves | Startup could fail or hand-edited settings could be lost | Back up malformed YAML and invalid root shapes before saving defaults; propagate I/O errors instead of silently resetting unreadable files |
| Config saves rewrote the destination in place | Interrupted writes could leave truncated settings; existing readers could see partial rewrites | Serialize and write a sibling temporary file, then atomically replace the destination |
| Numeric config conversion used the current OS culture | Decimal YAML values could fail under locales using a comma decimal separator | Convert scalar values with invariant culture, preserving direct typed access to dictionaries used by SpiderSurge's batched saves |
| Mod discovery depended on the working directory and rejected any path containing `Disabled` | Launchers could look in the wrong folder; valid mod filenames or installation paths could be skipped | Use the application's base directory and exclude only exact `Disabled` directory components below `Silk/Mods` |
| The Mods menu waited for a synchronous update request | Slow/offline update servers could stall the main thread or prevent menu creation | Create the menu first; use an asynchronous HTTP request with a ten-second timeout and contain request failures |

The duplicate policy keeps the first compatible file in sorted path order. It does not choose the newest version automatically. A failed initializer reserves its ID for the process so another copy cannot repeat partially completed side effects. Existing SpiderSurge workarounds remain compatible and were not changed.

## Building the loader with local game references

The upstream `SpiderHeck.GameLibs` package version is unavailable from nuget.org. The original package build remains the default; `GameLibsPath` allows using a local Silk `Library` directory instead:

```powershell
dotnet build src/Silk.csproj -c Release '-p:GameLibsPath=C:\path\to\SpiderHeck\Silk\Library'
```

As in the upstream build, `src/lib` must contain `0Harmony.dll`, `MonoMod.RuntimeDetour.dll`, `MonoMod.Utils.dll`, `Mono.Cecil.dll`, `Mono.Cecil.Rocks.dll`, and `YamlDotNet.dll`. Use the runtime dependencies from the installed loader. Game reference DLLs and local dependencies are not committed or redistributed by these tests.

## Unity validation

The stubs cannot prove Unity's native lifecycle. `tests/UnityProbe` supplies an optional in-game probe:

```powershell
dotnet build tests/UnityProbe/UnityProbe.csproj -c Release '-p:GameLibsPath=C:\path\to\SpiderHeck\Silk\Library'
```

Back up the loader, mods, config, and saves before temporarily installing the built loader and probe. Install two copies of the probe DLL in the Mods directory. It includes one normal mod and one deliberately throwing initializer; the latter must appear as a load failure. Do not distribute either probe with a normal loader installation.

Inspect `SilkRegression.ProbeState` after startup. `InitializeCalls` and `AwakeCalls` must both be 1, their instance IDs must match, `AwakeSawInitialized` must be true, and `UpdateCalls` must increase. `FailedInitializeCalls` must be 1 and `FailedAwakeCalls` 0. The failed component must not appear in `Silk.Mods.Manager.Mods`. Set `updater.latestVersionUrl` temporarily to an unreachable loopback URL and verify the Mods menu is created and the request failure is only a warning. Restore the installation afterward.

Validated on Steam SpiderHeck / Unity 2020.3.42f1 with the installed SpiderSurge and SpiderInspector:

- Probe initialized and awoke once with the same native instance ID; frame callbacks ran.
- Both duplicate IDs were rejected; the failing initializer ran once and never awoke.
- SpiderSurge's `Instance` matched its registered Unity component. Fixes, Surge, Stats, and Teams all reported running.
- Silk completed startup, created its Mods menu, and logged an unreachable update server as a warning.
- The loader built for .NET Framework 4.7.2. The existing nullable-default warning in `GetModConfigValue` remains.
- The original Silk DLL, settings, and saves were restored. Unrelated concurrent SpiderSurge DLL and subsequent settings updates were preserved. No test match telemetry was sent.

This was a startup/integration smoke test, not a full gameplay, Xbox, or two-peer multiplayer test. Known game lighting errors appeared alongside the deliberately induced probe error; no additional loader startup exception remained.
