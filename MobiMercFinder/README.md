# MobiMercFinder

Mobile companion to [MercExchangeLokator](https://github.com/hubbahubba713/MercExchangeLokator) — finds the Merc Exchange building using template matching on screenshots.

## Features
- Pick a screenshot or take a photo
- Same CcoeffNormed template matching algorithm as the desktop app
- Live threshold slider (0.30 – 0.55)
- Detection zone (Top/Bottom band filter)
- Vibration alert on detection
- All settings persist across restarts

## How to use
1. Launch the app
2. Tap **Load Ref 1** and pick `merc_exchange_sample02.png`
3. Tap **Load Ref 2** and pick `merc_exchange_sample.png` (optional but improves accuracy)
4. In-game, take a screenshot
5. Tap **Pick Screenshot** and select it
6. App shows score and highlights if the Merc Exchange was detected

## Build requirements
- .NET 9 SDK
- .NET MAUI workload (`dotnet workload install maui-android`)
- Android SDK (API 21+)

## Project structure
```
MobiMercFinder/
  DetectorService.cs       — Template matching engine (OpenCvSharp)
  MainPage.xaml/.cs        — Detection UI
  Platforms/Android/       — Android entry point + manifest
  Resources/               — Styles, fonts, images
```
