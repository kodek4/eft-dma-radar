# Lone's EFT DMA Radar | kodek4 fork

![icon-static](https://github.com/user-attachments/assets/d2b02f5a-298c-45fd-8154-2331f1f21c0f)

## Overview

This is an up-to-date build of Lone DMA EFT/Arena Radar - a standalone copy with no restrictions.

**Currently supported EFT version: 0.16.7**

## Changes Compared to Upstream

This fork focuses on radar usage setups. ESP-specific changes are not currently planned.

### Features

**Map Rotation System**

- Full map rotation control via minibar interface
- Full-width rotation with no black bars during vertical rotations

**Height-Aware Alpha System**

- Configurable opacity for players and game entities at different elevation levels
- Two modes available:
  - **Regular mode**: Static alpha toggle for entities above/below player height
  - **Gradient mode**: Dynamic opacity adjustment based on vertical distance
- Applies to all radar entities (players, loot, doors, switches, etc.) with configuration split between world/player entities

**Performance Enhancements**

- Widgets moved to dedicated canvas for improved rendering
- FPS tracking integration for performance monitoring
- Various optimization improvements

## Getting Started

1. Download and extract the solution
2. Open the solution in Visual Studio
3. Publish either `eft-dma-radar` or `arena-dma-radar` project
4. If required, move `libSkiaSharp.dll` and `libHarfBuzzSharp.dll` from `publish/runtimes/` to `publish/` folder
5. Run the corresponding executable (`eft-dma-radar.exe` or `arena-dma-radar.exe`)

## Arena Support

Arena is fully supported and will continue to be maintained.

## Special Thanks

**Lone Survivor**

- Open sourced the `lone-dma` project, enabling all subsequent development

**x0m**

- Paypal: https://www.paypal.me/eftx0m?locale.x=en_NZ
- BTC: `1AzMqjpjaN5fyGQgZTByRqA2CzKHQSXkMr`
- LTC: `LWi2mP6GaDQbhDAzs4swiSEEowETRqCcLZ`
- ETH: `0x6fe7aee467b63fde7dbbf478dce6a0d7695ae496`
- USDT: `TYNZr9FL5dVtk1K5D5AwbiWt4UMbu9A7E3`

**Mambo**

- Significant contributions across multiple areas
- Paypal: https://paypal.me/MamboNoob?country.x=CA&locale.x=en_US
- BTC: `bc1qgw9v6xtwxqhtsuuge720lr5vrhfv596wqml2mk`

**Marazm**

- Maintains updated EFT and Arena maps
- https://boosty.to/dma_maps/donate
- USDT: `TWeRAuxsCFa8BHLZZbkz9aUHJcZwnGkiJx`
- BTC: `bc1q32enxjvfvzp30rpm39uzgwpdxcl57l264reevu`

**xiaopaoliaofwen**

- Various contributions and maintenance of the main fork
- https://buymeacoffee.com/kekmate

## Contact

For inquiries or assistance, join the [EFT Educational Resources Discord Server](https://discord.gg/jGSnTCekdx) and use the [Lone DMA Section](https://discord.com/channels/1218731239599767632/1342207117704036382).

Report major outages by opening an [Issue](https://github.com/Lone83427/lone-eft-dma-radar/issues). Please provide detailed information.

**Note**: Issues are for bug reports only, not feature requests. Misuse may result in restricted access.
