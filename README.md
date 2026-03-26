# RecRoomDownloader
A tool for downloading and setting up historical builds of Rec Room, with optional mod loader support.

## Requirements
- [DepotDownloader](https://github.com/SteamRE/DepotDownloader/releases) — place `DepotDownloader.exe` in the same folder as `RecRoomDownloader.exe`
- A Steam account that owns Rec Room
- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Setup
1. Download the latest release from the [Releases](https://github.com/TheIcy/RecRoomDownloader/releases) page
2. Place `DepotDownloader.exe` in the same folder
3. Run `RecRoomDownloader.exe`

## Usage
1. Enter your Steam username and password (saved locally for future runs)
2. Select a build from the list
3. Approve the Steam Guard prompt if asked
4. Optionally create `.bat` files for launching in Screen or VR mode
5. Optionally install a mod loader (MelonLoader or BepInEx)

## Mod Loaders
| Mod Loader | Source |
|---|---|
| [MelonLoader](https://github.com/LavaGang/MelonLoader) | Latest release, fetched automatically from GitHub |
| [BepInEx](https://github.com/BepInEx/BepInEx) | Latest release, fetched automatically from GitHub |

## How it works
Build metadata is fetched from [RecRoomDownloader-Data](https://github.com/TheIcy/RecRoomDownloader-Data). RecRoomDownloader passes the selected build's manifest ID to DepotDownloader, which handles the actual Steam download.

## Notes
- Your Steam credentials are stored in plaintext in `steam_username.txt` and `steam_password.txt`. Delete these if you're on a shared machine.
- Downloads are saved to `builds/<build date>/`
