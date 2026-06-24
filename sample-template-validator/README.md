# sample-template-validator

A composite GitHub Action that validates a MonoGame `dotnet new` template: it generates the template across the platforms **it declares**, checks the output is clean and coherent, and (where the runner can) compiles each target. One generic engine — the template under test drives which tests run, so it works for any MonoGame sample.

```yaml
- uses: actions/checkout@v4
- uses: MonoGame/monogame-actions/sample-template-validator@v1
  with:
    template: .                       # folder containing .template.config
    workload: ${{ matrix.workload }}  # optional: ios / android
```

A sample passes **only its template**. There are no scenario files to write or maintain — the entire test battery lives inside the action and is derived from the template's own `template.json`.

## What it tests

The battery is **owned by the action** and runs automatically — derived from `template.json`, nothing passed in. Tests are tagged by category so they can be filtered (`--filter Category=Structural`).

**Structural** (every OS, `dotnet new` only — fast):

| Check | What it asserts | Implemented in |
|---|---|---|
| Platform resolution | Every declared platform maps to a known target (else fails, naming the offender) | `PlatformTests` |
| Per-platform isolation | Selecting one platform generates only its folder, excludes the others, and the solution references it | `PlatformTests` |
| All platforms together | Every declared platform's folder is generated | `PlatformTests` |
| Invalid platform | An unknown `--Platforms` value is rejected (non-zero exit) | `PlatformTests` |
| Default generation | No platform args still generates | `PlatformTests` |
| Substitution | Every text parameter (and `sourceName`) substitutes through the output, with no original token left behind (skipping `copyOnly` files) | `SubstitutionTests` |
| No stray artifacts | No `.template.config`, `.git`/`.vs`/`.idea`, or `bin`/`obj` in the output | `GenerationIntegrityTests` |
| No unprocessed conditionals | No leftover `<!--#if … -->` markers | `GenerationIntegrityTests` |
| Solution integrity | Every project the `.slnx`/`.sln` references actually exists (no dangling refs) | `GenerationIntegrityTests` |
| Parameter surface | `--help` lists every declared parameter | `GenerationIntegrityTests` |

**Build** (host-appropriate; opt out with `include-build: false`):

| Check | What it asserts | Implemented in |
|---|---|---|
| Per-target build | Each declared target the runner can compile (see [Targets and build rules](#targets-and-build-rules)) is generated single-platform and built | `BuildTests` |

Because the suite is derived from the template, adding or removing a platform or parameter in the template automatically adds or removes its tests.

## Declaring platforms

The engine needs to know, for each platform the template offers, which MonoGame **target** it is and which source **folder** holds it. Declare that in a `platforms` map inside the template's `monogame` extension block (this keeps `symbols`/`choices` schema-valid):

```jsonc
"monogame": {
  "platforms": {
    "desktop": { "target": "DesktopVK",   "folder": "Desktop" },
    "windows": { "target": "WindowsDX12", "folder": "Windows" },
    "android": { "target": "Android",     "folder": "Android" },
    "ios":     { "target": "iOS",         "folder": "iOS" }
  }
}
```

The choice *token* (`desktop`), the *folder* (`Desktop`) and the *target* (`DesktopVK`) are independent — the map ties them together, so a sample can name things however it likes. The token is the value of the `Platforms` choice symbol; the engine reads the choices from there (override the symbol name with the `platform-symbol` input if yours differs).

If a template omits the map, the engine falls back to built-in guesses for common patterns (`desktopgl`, `desktopvk`, `windowsdx`, `windowsdx12`, `android`, `ios`, plus `desktop`/`windows` shorthand).
New samples should declare the map explicitly.

### Targets and build rules

The only platform knowledge the engine hard-codes is the build constraints per canonical target:

| target | builds on | workload |
|---|---|---|
| `DesktopGL` | any OS | — |
| `DesktopVK` | any OS | — |
| `WindowsDX` | Windows | — |
| `WindowsDX12` | Windows | — |
| `Android` | any OS | `android` |
| `iOS` | macOS | `ios` |

A template declaring a `target` outside this table fails fast with a clear message — extend `engine/PlatformCatalog.cs` when MonoGame adds or renames a backend.

## Inputs

| Input | Default | Description |
|---|---|---|
| `template` | `.` | Path to the template (folder containing `.template.config`). |
| `platform-symbol` | `Platforms` | The choice symbol that lists platforms. |
| `workload` | `''` | Optional dotnet workload to install first (e.g. `ios`, `android`). |
| `include-build` | `true` | `false` to skip the build tests (structural only). |
| `dotnet-version` | `9.0.x` | .NET SDK to set up. |

## Recommended caller workflow

The action runs in one job; you own the OS matrix, so you control which platforms get *built* (structural tests run on every OS regardless).

A typical matrix builds every target across the three runners:

```yaml
jobs:
  test:
    name: ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        include:
          - { os: windows-latest, workload: '' }       # builds desktop + windows targets
          - { os: macos-latest,   workload: ios }      # builds desktop + ios targets
          - { os: ubuntu-latest,  workload: android }  # builds desktop + android targets
    steps:
      - uses: actions/checkout@v7
      - uses: MonoGame/monogame-actions/sample-template-validator@v1
        with:
          template: .
          workload: ${{ matrix.workload }}
```

## Requirements

- The template exposes a multi-value `choice` symbol listing its platforms (named `Platforms`, or set `platform-symbol`), and ideally a `monogame.platforms` map (otherwise the fallback tokens apply).
- The .NET SDK from `dotnet-version` (the action installs it).
- The build tests restore real MonoGame packages — ensure they resolve from nuget.org, or add a `nuget.config` to the sample pointing at the MonoGame feed. The structural tests need no packages and pass regardless.
- Conditional `.slnx` files are validated against generated output; the engine doesn't require the `.slnx` special-operations setup itself, but a broken setup is caught (leftover markers / dangling references).

## Example Template

Below is an example of a standard template to be consumed by the validator for MonoGame projects (used by the action test):

[!code-xml[](./tests/fixture/.template.config/template.json)]

## What's in this action

```text
sample-template-validator/
  action.yml          # the composite action
  README.md           # this file
  engine/             # the xUnit engine (source; restored + run via dotnet test)
    PlatformCatalog.cs           # canonical targets + build rules + fallback token map
    Harness.cs                   # dotnet CLI wrapper, template.json reader, test fixture, helpers
    PlatformTests.cs             # per-platform structural tests
    SubstitutionTests.cs         # token / sourceName substitution
    GenerationIntegrityTests.cs  # artifacts, markers, solution refs, --help
    BuildTests.cs                # host-gated build tests
  tests/
    fixture/          # a tiny, dependency-free template the action's own test CI runs against
```

`tests/fixture/` is **test data, not an action** — a minimal `dotnet new` template (trivial projects, deliberately non-canonical tokens) that the action's own CI (`.github/workflows/main.yml`) validates to prove the engine itself works, fast and offline. Real samples are never affected by it.

Source ships in the action (not a prebuilt binary): it stays maintainable, auditable, and builds against whatever SDK the runner has. The per-run restore/compile is seconds and NuGet-cacheable — negligible next to generation/build time.

## Local development

To run the engine directly against any template via environment variables (the same ones the action sets), from the `engine/` folder:

```bash
TEMPLATE_ROOT=/path/to/template dotnet test                                # everything (structural + build)
TEMPLATE_ROOT=/path/to/template dotnet test --filter Category=Structural   # fast, no build
TEMPLATE_ROOT=/path/to/template INCLUDE_BUILD=false dotnet test            # same as above, explicitly
```

Environment variables: `TEMPLATE_ROOT` (required), `PLATFORM_SYMBOL` (default `Platforms`), `INCLUDE_BUILD` (default `true`).
