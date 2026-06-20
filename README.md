# MercExchangeLokator
Easy way to find merchant exchanges for Total battle

Required packages:
-- EMGU

## Capture backend behavior

- Managed capture (`SnaptureShim`) is the default and works out of the box.
- Native capture (`SnaptureCLI.dll`) is optional.

### Enable optional native capture

1. Build or place `SnaptureCLI.dll` in one of the expected output paths.
2. Set environment variable `MERC_USE_NATIVE_CAPTURE=true` before launching the app.

If native capture cannot be loaded or fails at runtime, the app automatically falls back to managed capture.
