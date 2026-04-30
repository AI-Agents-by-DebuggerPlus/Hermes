# Optional local/CI smoke: WSL distro activates default venv and runs `hermes --version`.
# Requires WSL, Ubuntu (or override), and Hermes installed under ~/hermes-agent/venv.
# Usage: pwsh -File scripts/smoke_hermes_cli.ps1 [-Distro Ubuntu]

param(
    [string] $Distro = "Ubuntu"
)

$bash = 'source "$HOME/hermes-agent/venv/bin/activate" && hermes --version'
$args = @("-d", $Distro, "--", "/bin/bash", "-lc", $bash)
$p = Start-Process -FilePath "wsl.exe" -ArgumentList $args -NoNewWindow -PassThru -Wait
exit $p.ExitCode
