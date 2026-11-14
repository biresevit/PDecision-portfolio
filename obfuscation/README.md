# Obfuscated customer delivery

How to build a customer-ready PDecision folder: the Release build, with `PDecision.dll` obfuscated so the API
layer isn't readable from a decompiler. `DecisionMaking.dll` and its `PokerUtil.dll` dependency are **never**
obfuscated - legacy strategy code has been observed to behave differently after metadata/member
transformations, so they always ship plain. Everything else (third-party DLLs, `PokerSuite.*` packages) ships
as-is.

```powershell
.\build.ps1 -ObfuscarConsole C:\tools\obfuscar\GlobalTools.dll
```

Output goes to `.\dist` (gitignored). See "Testing a build" below before sending `dist` to anyone, and
**"Mapping.txt must stay private"** — read that before you run this the first time.

## Building obfuscar.console from source

Note: since `build.ps1` now only ever obfuscates `PDecision.dll` by itself (`DecisionMaking.dll`/`PokerUtil.dll`
are always left plain), the cross-assembly bug below no longer affects the normal build. Keep this section for
anyone who later obfuscates more than one assembly together for some other reason.

The published NuGet package (`Obfuscar.GlobalTool`, currently 2.2.50) does **not** include the fix for
[obfuscar/obfuscar#316](https://github.com/obfuscar/obfuscar/issues/316), a cross-assembly rename resolution
bug. Without it, obfuscating `PDecision.dll` + `DecisionMaking.dll` + `PokerUtil.dll` **together** (needed so
that a renamed public method in one assembly is renamed consistently everywhere that calls it) fails to save
with `Error: Failed to resolve A.d`. Each assembly obfuscates fine alone; it's specifically the 3-assembly
combination that trips it.

**Before rebuilding from source, check `https://www.nuget.org/packages/Obfuscar.GlobalTool` for a release
newer than 2.2.50** — if the fix has shipped, just `dotnet tool install --global Obfuscar.GlobalTool` and
pass its `GlobalTools.dll` (or the `obfuscar.console` command it installs) as `-ObfuscarConsole`.

Until then, build it yourself:

```powershell
git clone --depth 50 https://github.com/obfuscar/obfuscar.git C:\tools\obfuscar-src
cd C:\tools\obfuscar-src
git submodule update --init --recursive
```

Its `global.json` pins SDK `10.0.100`; if you only have .NET 9 installed (as this project did), replace it:

```powershell
@'
{ "sdk": { "version": "9.0.101", "rollForward": "latestFeature" } }
'@ | Set-Content global.json
dotnet build src\GlobalTools\GlobalTools.csproj -c Release -f net9.0
```

(`src\Console\Console.csproj`, the WinForms GUI, targets classic `net462` and needs the full .NET Framework
targeting pack — skip it; `GlobalTools.csproj` is the cross-platform CLI and is all this needs.) Pass the
resulting `src\GlobalTools\bin\Release\net9.0\GlobalTools.dll` as `-ObfuscarConsole`.

## Mapping.txt must stay private

Obfuscar writes `work\obf-output\Mapping.txt`: the full original-name → obfuscated-name table. Anyone with it
can reverse the obfuscation instantly. It is **not** copied into `dist` by `build.ps1`, but it does sit in the
gitignored `work\` folder after every build — don't email it, don't commit it, delete it (or the whole `work`
folder) once you're done debugging a specific build. Keep one copy somewhere private if you think you'll need
to decode an obfuscated stack trace from a customer's crash report later (match the renamed class/method names
in the trace against this file; `DecisionMaking]A.J` lines mean `A.J` is this build's name for whatever's on
the left).

## Before shipping

`dist\appsettings.json` is copied from the repo's `PDecision\appsettings.json` as-is. Before sending `dist` to
a customer:
- Confirm `Database:ConnectionString` uses the restricted `pdecision_client` login, never `pokeruser` (the
  admin login must never leave this machine).
- Confirm `HandHistory:UploadToAzure` is `true` and `LocalCopyDir` is empty (those get overridden to test
  values during "Testing a build" below — don't ship a `dist` folder you tested with, rebuild instead, or
  reset `appsettings.json` by hand before zipping).

## Testing a build

**Don't skip this.** The obfuscation bugs documented in `Obfuscar.template.xml` all failed exactly the same
way: the service looked like it started fine, then hung forever with zero error output, or returned a bare
HTTP 500 with ASP.NET Core's default logging disabled (`builder.Logging.ClearProviders()` in `ApiServer.cs`
means an unhandled exception in a route handler is otherwise invisible). A build that merely "launches" is not
evidence it works.

1. **Does it reach Ready?**
   ```powershell
   $env:PDECISION_Api__HumanLikeDelays = 'false'
   $env:PDECISION_HandHistory__UploadToAzure = 'false'
   $env:PDECISION_HandHistory__LocalCopyDir = "$env:TEMP\pdecision_test_hh"
   & .\dist\PDecision.exe
   ```
   Poll `http://127.0.0.1:5005/api/health` (without `-f`/`-SkipHttpErrorCheck`, so you see the body on a
   non-200 too) until `status` is `ready`. Compare the timing and the log
   (`C:\PokerSuite\Logs\PDecision*.log`) against a plain (non-obfuscated) build of the same commit: it should
   reach Ready in about the same time, with the same sequence of log lines up to
   `CasinoSettings <casino>: CONFIGURE_LOCALLY` → `done loading strategy files` → `Done Initialize`. If it stalls silently
   after a specific log line and never raises an exception, that's exactly how every bug in this file's SkipType
   list first showed up — see "Diagnosing a hang or silent failure" below.

2. **Does it make correct decisions and write correct hand histories?**
   ```powershell
   cd <repo root>
   python tests\multitable_test.py --exe dist\PDecision.exe --hands 10 --seed 42
   ```
   Run it a few times with different `--seed` values, and compare the pass rate against the same command
   pointed at a plain (non-obfuscated) build. One or two transient failures under heavy concurrent load that
   don't reproduce on an identical-seed rerun are consistent with pre-existing test flakiness unrelated to
   obfuscation (observed during this project's own validation); a failure that reproduces every time, or that
   baseline never shows, is not.

3. Optionally, the `simulator/PokerTable` project (`F5` run, `Ctrl+B` all bots, `Ctrl+F` fast mode — see the
   main repo's `README.md`) against `dist\PDecision.exe` is a longer, less scripted complementary check:
   point `PDECISION_URL` at it and watch the window title's error/skip counters.

### Diagnosing a hang or silent failure

If a future code change reintroduces one of these bug classes, it will look like the build above: the process
stays alive, the log goes quiet after some line, and `/api/health` either never returns `ready` or returns a
bare 500. Don't guess — get a thread/heap dump:

```powershell
dotnet tool install --global dotnet-dump --configfile <a nuget.config with only nuget.org - the private
                                                         PokerSuite feed in the default one 401s on this>
dotnet-dump collect -p <pid> -o hang.dmp
dotnet-dump analyze hang.dmp
```

Inside the analyzer:
- `pstacks` groups all threads by stack — if your own code (`StatsLoader`, `DbStrategySettings`,
  `Controller`, CONFIGURE_LOCALLY) isn't on ANY thread's stack, nothing is running or blocked; the logical chain of
  work died silently somewhere (an unobserved faulted Task is the usual cause).
- `dumpheap -stat -type Exception` lists every exception object still reachable on the heap, including ones
  that were caught and never logged. Look for `TypeLoadException` ("Could not load type CONFIGURE_LOCALLY" — a type with an
  enum-typed member that needs a `<SkipType>`, see `Obfuscar.template.xml`'s failure family 2),
  `System.Configuration.SettingsPropertyNotFoundException` (a `.settings`-generated class got renamed), or
  anything from your own namespaces.
- `dumpheap -type TypeLoadException` (or whatever type you found) gives an address; `pe -nested <address>`
  prints its message and captured stack trace, translated through the renamed method names.

Translate a renamed name (`A.J`, `C.E.B(List\`1)`, CONFIGURE_LOCALLY) back to source using `work\obf-output\Mapping.txt`:
search for ` -> A.J` to find which original type/member it is.

## Keeping the skip list current

`audit-enum-members\` is a small reflection-based tool: it loads the three staged (pre-obfuscation) DLLs and
reports every type with a field or property typed as one of their own enums, then checks whether that type is
already in `Obfuscar.template.xml`'s skip list (see that file's header comment, "failure family 2", for why
this matters — it's the single largest source of the bugs found while building this pipeline). Run it after
`build.ps1`'s staging step, or any time you add a new field/property of an enum type to `PDecision`,
`DecisionMaking` or `PokerUtil`:

```powershell
cd audit-enum-members
dotnet build -c Release
dotnet bin\Release\net8.0\audit-enum-members.dll ..\work\obf-staging ..\Obfuscar.template.xml
```

A `!!!` line names a type that needs a new `<SkipType name="CONFIGURE_LOCALLY" skipMethods="true" skipFields="true"
skipProperties="true" />` entry added to `Obfuscar.template.xml`, in the `<Module>` block for whichever
assembly declares it. If the type is an enum itself (not just a type that uses one), also check whether
anything calls `Enum.Parse`/`Enum.TryParse` against text that isn't generated by this same renamed build (SQL
columns, solver/strategy files on disk, the legacy TCP wire format) — those need the same protection even
though the audit tool, which only checks for enum-typed *members*, won't flag the enum type on its own.
