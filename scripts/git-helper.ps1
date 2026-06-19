<#
Simple Git helper for Windows PowerShell.
Usage:
  Open Developer PowerShell in the repo root and run:
	.\scripts\git-helper.ps1              # shows status and suggested commands
	.\scripts\git-helper.ps1 -Push       # stages, commits (if needed) and pushes to origin

This script does not store credentials. For HTTPS push you may need to use a PAT or sign in.
#>
param(
	[switch]$Push
)

function Run-Git {
	param($args)
	$proc = Start-Process git -ArgumentList $args -NoNewWindow -RedirectStandardOutput -RedirectStandardError -PassThru -Wait
	$output = $proc.StandardOutput.ReadToEnd().Trim()
	$errorOutput = $proc.StandardError.ReadToEnd().Trim()
	return @{ ExitCode = $proc.ExitCode; Out = $output; Err = $errorOutput }
}

# Check git
$gitVer = Run-Git '--version'
if ($gitVer.ExitCode -ne 0) {
	Write-Host "Git not found. Install Git for Windows: https://git-scm.com/download/win" -ForegroundColor Yellow
	exit 1
}
Write-Host "Git: $($gitVer.Out)"

# Show repo status
Write-Host "\nRepository status:" -ForegroundColor Cyan
$st = Run-Git 'status --porcelain'
if ($st.ExitCode -ne 0) { Write-Host "Failed to run git status: $($st.Err)" -ForegroundColor Red; exit 1 }
if ([string]::IsNullOrWhiteSpace($st.Out)) { Write-Host "Working tree clean." } else { Write-Host $st.Out }

# Current branch
$branch = Run-Git 'rev-parse --abbrev-ref HEAD'
if ($branch.ExitCode -ne 0) { Write-Host "Failed to get current branch: $($branch.Err)" -ForegroundColor Red; exit 1 }
Write-Host "Current branch: $($branch.Out)"

# Remotes
$rem = Run-Git 'remote -v'
if ($rem.ExitCode -ne 0) { Write-Host "Failed to list remotes: $($rem.Err)" -ForegroundColor Red; exit 1 }
if ([string]::IsNullOrWhiteSpace($rem.Out)) {
	Write-Host "No git remotes configured." -ForegroundColor Yellow
	Write-Host "To create a GitHub repo and push from command line (recommended):" -ForegroundColor Cyan
	Write-Host "  1) Create a repo on github.com (or use 'gh repo create' if you have GitHub CLI)." -ForegroundColor Cyan
	Write-Host "  2) Run these commands from the repo root:" -ForegroundColor Cyan
	Write-Host "     git remote add origin https://github.com/USERNAME/REPO.git" -ForegroundColor Green
	Write-Host "     git branch -M main" -ForegroundColor Green
	Write-Host "     git push -u origin main" -ForegroundColor Green
	Write-Host "If you prefer the current branch name, replace 'main' with the branch shown above." -ForegroundColor Cyan
} else {
	Write-Host "Remotes:" -ForegroundColor Cyan
	Write-Host $rem.Out
}

# Show unpushed commits (if any)
$upstream = Run-Git 'rev-parse --abbrev-ref --symbolic-full-name @{u}'
if ($upstream.ExitCode -eq 0) {
	$unpushed = Run-Git 'log --oneline @{u}..HEAD'
	if (-not [string]::IsNullOrWhiteSpace($unpushed.Out)) { Write-Host "\nUnpushed commits:"; Write-Host $unpushed.Out }
	else { Write-Host "No unpushed commits." }
} else {
	Write-Host "No upstream configured for this branch. Pushing will set upstream if you push with -u." -ForegroundColor Yellow
}

if ($Push) {
	Write-Host "\nPreparing to push changes (staging all files)..." -ForegroundColor Cyan
	$add = Run-Git 'add -A'
	if ($add.ExitCode -ne 0) { Write-Host "git add failed: $($add.Err)" -ForegroundColor Red; exit 1 }

	# commit only if there are staged changes
	$diffIndex = Run-Git 'diff --cached --name-only'
	if (-not [string]::IsNullOrWhiteSpace($diffIndex.Out)) {
		Write-Host "Committing changes..." -ForegroundColor Cyan
		$msg = "Add CI workflow and helper script"
		$commit = Run-Git "commit -m `"$msg`""
		if ($commit.ExitCode -ne 0) { Write-Host "git commit failed: $($commit.Err)" -ForegroundColor Red; exit 1 }
	} else {
		Write-Host "No staged changes to commit." -ForegroundColor Yellow
	}

	# push
	Write-Host "Pushing to origin..." -ForegroundColor Cyan
	$push = Run-Git "push -u origin $($branch.Out)"
	if ($push.ExitCode -ne 0) { Write-Host "git push failed: $($push.Err)" -ForegroundColor Red; exit 1 }
	Write-Host "Push completed." -ForegroundColor Green
}

Write-Host "\nHelper finished. If you need a GitHub repo created automatically, install 'gh' CLI and run: gh repo create --public --source=. --remote=origin --push" -ForegroundColor Cyan
