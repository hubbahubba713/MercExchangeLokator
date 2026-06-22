@echo off
cd /d "c:\Users\LENOVO\Downloads\MercExchangeLokator-master (1)\MercExchangeLokator-master\MercExchangeLokator"
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" MercExchangeLokator.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo
start "" "bin\x64\Debug\MercExchangeLokator.exe"
