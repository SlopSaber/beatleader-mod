# BeatLeader PC mod

An open-source leaderboard System for Beat Saber

- [Join our Discord server](https://discord.gg/beatleader)
- [Support us on Patreon](https://www.patreon.com/beatleader)

![cover](./Media/cover.png)

## Usage

- Download zip for your game version from the [Releases](https://github.com/BeatLeader/beatleader-mod/releases) and extract it to your BeatSaber directory
- Make sure to install and update all required [dependencies](#dependencies)(they are included in the release zips, just unzip the correct zip in your game folder)

Go to the https://beatleader.com/ to see your scores on the web

If you experience any issues, have any suggestions or bug reports - you can leave them in our [Discord server](https://discord.gg/beatleader)

## Dependencies

- BSIPA, BeatSaberMarkupLanguage, SiraUtil - available in [BSManager](https://www.bsmanager.io) and on the [BeatMods website](https://beatmods.com/#/mods)
- [LeaderboardCore](https://github.com/rithik-b/LeaderboardCore)
- [ReactiveUI](https://github.com/reactive-platform/reactive-mono)

## Building

Set `BeatSaberDir` in the ignored `Source/BeatLeader.csproj.user` file to your Beat Saber installation. Visual Studio and command-line builds use that installation for framework and mod references. Install the required mod dependencies there before building.

```xml
<Project>
  <PropertyGroup>
    <BeatSaberDir>C:\Games\Beat Saber</BeatSaberDir>
  </PropertyGroup>
</Project>
```

To select an installation for a command-line build, pass `-p:GameDirectory="C:\Games\Beat Saber"`. An explicit `GameDirectory` takes precedence over the local setting. If neither is configured, an existing `Refs` directory is used.
