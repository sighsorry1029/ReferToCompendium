## 1.0.2

- Replace the edit controls with always-available title dragging, automatic saving, and edge scrolling.
- Fix dragging directly within the Compendium title list.
- Keep the drag instructions and Reset button above the scrolling list.
- Add YAML translations for the mod's UI text, with built-in English and Korean defaults.
- Find `ReferToCompendium.<Language>.yml` files in plugins and config subfolders, with config and existing translation paths taking priority.
- Include `ReferToCompendium.English.yml` as a translation template.

## 1.0.1

- Add a single-column status effect HUD with adjustable position, scale, and row spacing.
- Enable the column by default at (-40, -250), with scale 0.8 and row spacing 40.
- Add a client-side Hidden effects list to hide selected HUD entries while preserving gameplay effects and Compendium information.
- Restore the previous HUD arrangement and styling when the column is disabled. Filtering also works with the column off.

## 1.0.0

- Add a persistent raven hint with English and Korean defaults.
- Add personal compendium ordering and collapsed entries.
- Save one shared layout across characters and modpack profiles.
