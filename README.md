# StageReadyVR

A VR public speaking trainer for the PICO 4. The speaker presents to a virtual audience in a lecture hall, university classroom or conference room. Speech, head movement and delivery are tracked during the session, and a coaching report is shown at the end.

## Requirements

| Tool | Version |
| --- | --- |
| Unity Editor | 2022.3.62f3 (LTS) with the Android Build Support module (OpenJDK and Android SDK and NDK Tools) |
| Git LFS | any recent version, required before cloning |
| PICO SDK | 3.4.0, downloaded manually (see below) |
| Headset | PICO 4 with developer mode enabled |

## Getting started

1. Install Git LFS and run `git lfs install` once. Large assets (models, animations, light maps, textures) are stored in LFS, and without it you will get tiny pointer files and broken assets.
2. Clone the repository and let LFS finish pulling. The first clone is several GB.
3. Install the PICO SDK as described in the next section. Do this before opening the project.
4. Open the folder in Unity Hub with editor version 2022.3.62f3.
5. Add your API key (see "API keys").
6. Switch the build target to Android (File > Build Settings).

## PICO SDK setup

The PICO Unity Integration SDK is proprietary. Its license forbids redistribution without written permission from PICO, so it is deliberately **not** in this repository. Every developer installs it locally, and the project references it by a relative path so the setup is identical on every machine.

1. Download the **PICO Unity Integration SDK 3.4.0** from the PICO developer site: https://developer.picoxr.com/resources/
2. Unzip it so that `package.json` sits directly inside this exact folder, at the project root:

   ```
   ThirdParty/PICO-3.4.0/package.json
   ```

3. Open the project. `Packages/manifest.json` points at it with `"com.unity.xr.picoxr": "file:../ThirdParty/PICO-3.4.0"`.

`ThirdParty/` is gitignored. If Unity reports that it cannot resolve `com.unity.xr.picoxr`, the folder is missing or misnamed. If the SDK is ever upgraded, change the folder name and the path in `Packages/manifest.json` together and commit that change.

## API keys

Secrets are never committed. The coaching service reads the Anthropic API key from a local text file:

1. Create `Assets/Resources/anthropic_key.txt`.
2. Paste only the key into it, with no quotes and no trailing newline.

The file is already covered by `.gitignore`. A key bundled into a build can be extracted from the APK, so this is for development and internal testing only. A shared deployment should use a relay server that holds the key.

## Project layout

| Path | Contents |
| --- | --- |
| `Assets/Scripts/` | Runtime code: session flow, audience, speech analysis, coaching, feedback dashboard, head tracking, controller input |
| `Assets/Editor/` | Editor tooling, including the command line build (`PicoBuild.cs`) |
| `Assets/MainMenu.unity` | Entry scene |
| `Assets/Lecture hall_Auditorium/`, `Assets/Nimikko_UniversityClassroom/`, `Assets/Conference Room 01_V2/` | Practice environments |
| `Assets/characters/`, `Assets/npc_casual_set_01/`, `Assets/state_animations/`, `Assets/sitting_animations/` | Audience models and animations |
| `Packages/` | Package manifest and lock file |
| `ThirdParty/` | Local only, holds the PICO SDK (gitignored) |

## Building for the PICO 4

From the editor, use File > Build Settings with Android selected, or run the scripted build from a terminal:

```
Unity -batchmode -nographics -quit -projectPath . -buildTarget Android \
      -executeMethod PicoBuild.BuildAndroid -buildOutput Builds/StageReadyVR.apk
```

The build uses the scenes enabled in Build Settings and exits non zero on failure. Without signing variables it signs with the Android debug keystore and logs a warning. For a release build, set all four of these environment variables (setting only some of them fails the build on purpose):

| Variable | Meaning |
| --- | --- |
| `STAGEREADY_KEYSTORE_PATH` | absolute path to the keystore file |
| `STAGEREADY_KEYSTORE_PASS` | keystore password |
| `STAGEREADY_KEY_ALIAS` | key alias |
| `STAGEREADY_KEY_PASS` | key password |

Keystores (`*.keystore`, `*.jks`) and the built `.apk` files are gitignored. Install a build with `adb install -r Builds/StageReadyVR.apk`.

## Contributing notes

- Do not commit `Library/`, `Builds/`, `.apk` files, keystores or anything containing keys. The `.gitignore` covers these, but check `git status` before committing.
- Commit `.meta` files together with their assets, since Unity uses them to keep references intact.
- Keep machine specific paths out of `Packages/manifest.json` and `ProjectSettings/`.
