# Ideas

## Accessibility

- **High-contrast wallpaper suppression**
    - Problem: OS high-contrast users can get translucent chrome over wallpaper backgrounds.
    - Smallest first version: query Win32 `SPI_GETHIGHCONTRAST` at startup and force `HasActiveWallpaper` false.
    - Status: parked because this Avalonia version exposes no cross-platform high-contrast preference.

## Wallpaper

- **Phase B readability guard**: Sample wallpaper luminance and strengthen the scrim when foreground contrast needs it. Keep parked until after the i3 380M performance pass.
- **Album-art background**: Allow the current track's artwork as a wallpaper source, with cache, decode-size, and playback-update safeguards.
- **Automatic wallpaper mode**: Define an `Auto` mode that chooses a suitable background based on theme or system state.
- **Saved and shared looks**: Package appearance settings, accents, and wallpaper choices as reusable/shareable looks.
- **4K decode cap**: Record and enforce a decode-size ceiling for large wallpaper images to bound memory use on older systems.
