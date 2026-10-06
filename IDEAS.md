# Ideas

## Accessibility

- **High-contrast wallpaper suppression**
  - Problem: OS high-contrast users can get translucent chrome over wallpaper backgrounds.
  - Smallest first version: query Win32 `SPI_GETHIGHCONTRAST` at startup and force `HasActiveWallpaper` false.
  - Status: parked because this Avalonia version exposes no cross-platform high-contrast preference.
