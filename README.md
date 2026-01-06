# RetroRommer [BETA]

![License](https://img.shields.io/badge/license-GPL--3.0-blue)
![Build Status](https://img.shields.io/github/actions/workflow/status/midwan/RetroRommer/build-and-release.yml)
![Version](https://img.shields.io/github/v/release/midwan/RetroRommer)

RetroRommer is a utility designed to help MAME enthusiasts complete their ROM collections. It takes a scan report (e.g., from **ClrMamePro**), identifies missing ROMs, CHDs, or Samples, and automatically attempts to download them from a configured source.

## Features

- **Automated Downloads**: Parses MAME logs to find missing items and downloads them.
- **Support for All Types**: 
  - **ROMs & BIOS**: Downloads full zip sets.
  - **Samples**: Downloads sample zip sets.
  - **CHDs**: Identifies and downloads large CHD disk images.
- **Smart Cleanup**: Optionally removes successfully downloaded items from your report file, so you can focus on what's left.
- **Resilient**: Handles "Too Many Requests" errors and avoids downloading garbage HTML files.
- **Modern UI**: Dark-themed, responsive WPF interface with progress tracking.
- **Persistent Settings**: Remembers your paths and credentials.

## Prerequisites

- Windows OS (WPF Application)
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Usage

1.  **Generate a Report**: Use **ClrMamePro** (or similar tools) to scan your collection and save the scan output/miss list as a text file.
    *(The tool parses lines starting with `missing rom:`, `missing disk:`, etc.)*
2.  **Open RetroRommer**:
    - Browse for your `report.txt`.
    - Select a **Destination Folder** where files should be saved.
    - (Optional) Check **"Remove finished items from report"** to clean up the list as you go.
3.  **Configure Connection**:
    - Enter the **Download Website** URL.
    - Enter **Username** and **Password** (if required by the site).
4.  **Download**:
    - Click **Start Download**. 
    - The progress bar and log area will update in real-time.

## Configuration

Settings are saved automatically to `appsettings.json` upon exit. You can also edit this file manually:

```json
{
  "MissFile": "path/to/report.txt",
  "Website": "https://example.com/downloads",
  "Username": "user",
  "Password": "password",
  "Destination": "path/to/roms",
  "CleanupReport": true
}
```

## Building from Source

1.  Clone the repository.
2.  Open `RetroRommer.Core.sln` in Visual Studio or Rider.
3.  Build the solution.

Alternatively, use the command line:
```cmd
dotnet build RetroRommer.Core.sln
```

## License

This project is licensed under the GPL-3.0 License - see the [LICENSE](LICENSE) file for details.
