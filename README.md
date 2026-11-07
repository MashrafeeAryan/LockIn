# LockIn

A Windows desktop focus app for studying with ChatGPT. Pick a timer, click **Study ChatGPT**, and the installed ChatGPT window fills its monitor with a small countdown at the top.

## Build and run

Requires Windows 10/11 and .NET Framework 4.8. No extra packages, API keys, or administrator rights are needed.

```powershell
.\build.ps1
.\LockIn.exe
```

Keep `LockIn.exe.config` beside the executable. Open ChatGPT and sign in before starting your first session. Start with a one-minute trial.

## Session behavior

- Choose 25, 45, 60, or a custom duration from 1 to 240 minutes.
- **Hide + block** minimizes other desktop windows and restores their placements afterward. Unsaved work stays open.
- **Ask other apps to close** sends normal close requests, allows save prompts before the timer starts, and minimizes remaining windows. It never force-terminates those apps. Closed apps are not reopened automatically.
- ChatGPT runs full screen on its monitor; other monitors display study covers.
- Common switching shortcuts are blocked during the timer. The desktop unlocks automatically at the deadline.

**Emergency unlock: Ctrl + Alt + Shift + F12.**

The app manages the existing native ChatGPT window rather than embedding a second ChatGPT application. It uses your existing ChatGPT account. The launcher has a button; voice commands are not implemented.

## Recovery and limits

An independent watchdog attempts to restore window placements if the controller exits unexpectedly. If the controller freezes past its session deadline, the watchdog ends only its own controller after an additional 60 seconds and attempts restoration. Process identity is verified first. A display-layout change or a closed ChatGPT window ends the session.

LockIn is a focus aid, not an unbreakable Windows kiosk. Windows recovery controls, sign-out, restart, or ending the program can bypass it. Elevated applications and system surfaces may resist control. It does not filter ChatGPT content or disable every notification.

The executable is a custom unsigned build. The complete live session still needs testing with the installed ChatGPT app. Component validation does not activate a real study lock or close your other apps.

## App selection

The included config targets the Start-menu entry named **ChatGPT** found on the development PC: `OpenAI.Codex_2p2nqsd0c76g0!App`.

For another installation, use **Choose .exe**, or update `ChatGPTAppId` in `LockIn.exe.config`. Find your local Start-menu identity with:

```powershell
Get-StartApps | Where-Object { $_.Name -match 'ChatGPT' }
```

## Validation

```powershell
.\test.ps1
```

Seven component checks cover elapsed-time countdowns, duration limits, shortcut rules, recovery serialization, native full screen and placement restoration, launcher rendering, and watchdog restoration after a test controller exits. Window tests use invisible test windows. Validated on Windows 11 at 125% display scaling.

## Source and privacy

`LockIn.cs` contains the launcher, session controls, native window operations, and recovery watchdog. `Checks.cs` contains the component checks. `build.ps1` uses the installed Windows .NET Framework compiler.

LockIn makes no network requests and stores no chat content or typed text. It keeps current shortcut key states in memory and uses a temporary XML file with window handles, process identities, and placements for recovery. It installs no service, runs no startup task, and changes no Windows policies.
