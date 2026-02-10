# PowerBlockerViewer

A Windows tool to monitor processes preventing screen from turning off.

## Features

- **Real-time Monitoring**: Continuously scans for processes blocking screen-off via `powercfg /requests`
- **Memory Optimized**: Uses rate limiting, caching, and garbage collection for constant memory usage
- **Clean UI**: Shows only process names (without paths) and their blocking reasons
- **Auto-refresh**: Updates every 3 seconds to show current status
- **Administrator Support**: Properly handles UAC elevation for complete results

## Usage

1. **Run as Administrator** for complete results
2. **View Process List**: See which applications are preventing screen from turning off
3. **Refresh**: Click "Refresh Now" or wait for automatic 3-second updates
4. **Status Messages**: Color-coded status (red when blocking, green when clear)

## Technical Details

- **Language**: C# (.NET 8)
- **Framework**: Windows Forms
- **Command**: Parses `powercfg /requests` output
- **Filtering**: Only shows processes in the DISPLAY section (screen-off blocking)
- **Memory Management**: 
  - Rate limiting to prevent unnecessary refreshes
  - Output caching to avoid re-parsing identical data
  - ListView BeginUpdate/EndUpdate for memory efficiency
  - Automatic garbage collection cycles
  - Named event handlers to prevent lambda memory capture

## Process Information Displayed

The application monitors the following types of power requests:
- **DISPLAY**: Processes preventing screen from turning off
- **SYSTEM**: Processes preventing system sleep (ignored by this app)
- **AWAYMODE**: Away mode requests (ignored)
- **EXECUTION**: Execution requests (ignored)
- **PERFBOOST**: Performance boost requests (ignored)
- **ACTIVELOCKSCREEN**: Active lock screen requests (ignored)

## Compilation

Build with `dotnet build PowerBlockerViewer.csproj` to create:
- `PowerBlockerViewer.exe` - Windows executable
- All dependencies are included in .NET 8 SDK

## Repository Structure

```
PowerBlockerViewer/
├── Program.cs                 # Main application code
├── PowerBlockerViewer.csproj # .NET 8 project file
├── app.manifest             # Windows application manifest
├── .gitignore              # Git ignore rules
└── README.md               # This file
```

## License

This project is open-source and available under the MIT License.