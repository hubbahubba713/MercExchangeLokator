@echo off
setlocal

set REPO=c:\Users\LENOVO\Downloads\MercExchangeLokator-master (1)\MercExchangeLokator-master
set GIT="C:\Program Files\Git\cmd\git.exe"

echo === Step 1: Stage ALL changes (desktop + mobile project) ===
cd /d "%REPO%"
%GIT% add -A -f
%GIT% add MobiMercFinder/ -f
%GIT% add build_and_run.bat setup_mobile.bat -f
%GIT% status --short

echo.
echo === Step 2: Commit ===
%GIT% commit --allow-empty -m "Add MobiMercFinder mobile app + sync all desktop changes"
echo Committed.

echo.
echo === Step 3: Push to origin (MercExchangeLokator) ===
%GIT% push origin --all
%GIT% push origin --tags

echo.
echo === Step 4: Push to mobile (MobiMercFinder) ===
%GIT% push mobile --all
%GIT% push mobile --tags

echo.
echo === Done! ===
echo Desktop repo: https://github.com/hubbahubba713/MercExchangeLokator
echo Mobile repo:  https://github.com/hubbahubba713/MobiMercFinder
pause
