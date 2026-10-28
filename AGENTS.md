# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls, the
test list and the project structure; this file covers how the code fits together and the rules that keep it working.

## What this is

A city-building simulation in the style of Maxis's 1989 SimCity, in C# on .NET 10 and Avalonia 12. Every tile and
building is drawn pixel by pixel in code, and the sounds are synthesised by `tools/make_sounds.py`; nothing comes
from the original game or Micropolis. One self-contained executable per platform.

## Commands

```bash
dotnet build AVASimCity.slnx                      # SDK pinned in global.json (10.0.400, latestFeature)
dotnet test AVASimCity.slnx                       # headless: no window opens
dotnet run --project src/AVASimCity               # play; --no-audio for silence
SIMCITY_SHOTS=$PWD/Images dotnet test tests/AVASimCity.Tests --filter ScreenshotTests   # refresh the README's pictures
SIMCITY_PROBE=1 SIMCITY_YEARS=30 SIMCITY_SEED=7 dotnet test tests/AVASimCity.Tests --filter PilotProbe   # tuning report
python3 tools/make_sounds.py                      # rewrite src/AVASimCity/Sounds (standard-library Python only)
```

`PilotProbe` does nothing unless `SIMCITY_PROBE` is set (`SIMCITY_PROBE=disasters` turns random disasters on).

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and invariant globalization for
every project: a warning fails the build.

## Layout

- `src/AVASimCity/Engine/`: the game, with no drawing in it.
  - `GameEngine.cs` runs the states, the tool in hand, the view, windows, speeds, files and the demonstration.
  - `Sim.cs` is the rules: tools, power, traffic, growth, the maps, demand, money, disasters.
  - `World.cs` holds everything that changes, and can be copied; it also defines the seeded `Dice`.
  - `Terrain.cs` makes new land; `Scenarios.cs` the five cities in trouble; `Pilot.cs` the computer mayor.
  - `CityFile.cs` saves and loads; `InputHandler.cs` turns held keys into scrolling; `SoundManager.cs` plays sound.
- `src/AVASimCity/Views/`: `GameCanvas*.cs` (the screen, palette and windows), `MapPainter.cs` (the map as one
  picture, redrawn as tiles change), `TileArt.cs` and `Pix.cs` (the pixel art).
- `src/AVASimCity/Audio/`: the OpenAL Soft engine shared with the author's other AVA games.
- `tests/AVASimCity.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshot and probe tests.

## Rules that keep it working

- **Fixed steps.** The game advances in fixed 60 Hz steps (`Sim.Step`, driven by an accumulator in
  `GameEngine`), so it plays the same at any frame rate and tests can run years in seconds.
- **One world, one random source.** Everything that changes lives in `World`, so a city can be copied, saved and
  carry on the same. Draw random numbers from `w.Dice` (seeded per city), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`; `Views/` reads the world and draws it.
- **Input.** `InputHandler.Player` gives `dy` negative for up (towards row 0). Keep that sign if you add keys.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `src/AVASimCity/Audio/native/`. In a virtual machine `OpenAlConfig` writes a larger mixing period to
  `ALSOFT_CONF`.
- **Tests never touch the player's files.** Cities are saved under `ApplicationData/AVASimCity`; tests pass a
  temporary folder to `GameEngine` and run silent. (In-game screenshots go to the Pictures folder.)

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
