# ReferToCompendium

A small client-side Valheim mod that makes the Compendium easier to find and keeps its list in your preferred order.

![](https://i.ibb.co/B5vy3N6P/Screenshot-2026-09-26-143651.png) <br>
Many players often doesn't even bother clicking the Compendium button even though some mods would put useful info there. <br>
So this mod gets players attention to the Compendium. <br>

![](https://i.ibb.co/j9w8k6zD/Screenshot-2026-09-26-140342.png)
Player can edit the order of Compendium entries. And Some entries can be hidden. <br>

![](https://i.ibb.co/k2XRrxmQ/Screenshot-2026-09-26-140413.png)
Edited info of Compendium is located in %USERPROFILE%\AppData\LocalLow\IronGate\Valheim\ReferToCompendium\layout-shared.json <br>

## Features

- **Raven hint:** a short message beside the raven's beak, styled like NPC speech. It stays visible while the inventory is open, without fading, and hides while another panel is in front.
- **Personal order:** drag Compendium entries into place, including entries added by other mods.
- **Less clutter:** move unwanted entries into a collapsible group at the bottom. Restore them whenever you want.
- **Status HUD:** show status effects in one vertical column and hide selected effects from the HUD.

English and Korean UI text are included. The default English hint is “Click for more info” followed by “Caw!”

## Translations

Five UI texts use YAML translations: the default raven hint, drag instructions, Reset button, collapsed group title, and collapsed group help. English and Korean files are embedded in the mod DLL, along with YamlDotNet. No separate translation library needs to be installed.

To customize a language or add another one, create a UTF-8 file named `ReferToCompendium.<Language>.yml` anywhere under `BepInEx/plugins` or `BepInEx/config`, including subfolders. Use the game's language name, for example:

```text
BepInEx/config/ReferToCompendium.Korean.yml
BepInEx/config/MyTranslations/ReferToCompendium.German.yml
```

The download includes `ReferToCompendium.English.yml`. It is loaded when English is selected and can be copied and renamed as a starting point for another language. Keep your edits under `BepInEx/config` so a package update does not overwrite them. You only need to include the entries you want to change:

```yaml
rtc_raven_hint: |-
  Click for more info
  Caw!
rtc_reset: Reset
```

| Key | Text |
| --- | --- |
| `rtc_raven_hint` | Default raven hint |
| `rtc_drag_hint` | Instructions above the Compendium list |
| `rtc_reset` | Reset button |
| `rtc_collapsed_title` | Collapsed entries heading |
| `rtc_collapsed_help` | Help shown when that heading is selected |

Translations are applied in this order, with later files overriding individual keys:

1. Built-in English.
2. The selected language's built-in translation, if included.
3. `ReferToCompendium.<Language>.yml` under `BepInEx/plugins`.
4. `ReferToCompendium.<Language>.yml` under `BepInEx/config`.
5. `BepInEx/config/ReferToCompendium/translations/<Language>.yml`, which remains supported and has highest priority.

Missing or blank entries keep the text from the previous layer. If multiple matching files exist under the same root, only the first full path in ordinal (case-sensitive) order is selected, and a warning is logged. Invalid files also produce a warning and leave the previous layer unchanged; another duplicate is not used as a fallback. Files ending in `.example.yml` or using other naming patterns are ignored.

Files are searched only at startup and when the game language changes. Discovery skips symbolic links and junctions. Restart the game, or switch to another language and back, to reload edits. Changing the game language also refreshes the mod's open UI.

Config names and descriptions remain in English. A custom **Raven hint / Text** value takes priority over translations. Text from the game and other mods is unchanged.

## Edit the list

1. Open the inventory and click the raven to open the Compendium.
2. Hold an entry's title, drag it up or down, and release at the gold line. Each completed move saves automatically.
3. Use **−** to collapse an entry. Expand **Collapsed entries** at the bottom and use **+** to restore it.

Click a title to read its page, or hold and drag to change its order. Hold near the list's top or bottom edge to scroll, and drop outside the list to cancel. Drag within the main list or within the expanded collapsed group.

The drag hint and **Reset** button stay above the list while you scroll. **Reset** restores the default layout for currently available entries. Editing uses the mouse or virtual pointer. With a gamepad, select **Collapsed entries** and press **A** to expand or fold the group.

## Configuration

Created after the first launch: `BepInEx/config/sighsorry.ReferToCompendium.cfg`.

| Setting | Default | Description |
| --- | --- | --- |
| Raven hint / Enabled | `true` | Show or hide the hint. Takes effect immediately. |
| Raven hint / Text | Empty | Custom message. Leave empty for the localized default based on the game language. |
| Status effects / Single column | `true` | Arrange HUD effects vertically, with each icon beside its name and timer. Turning this off restores the original layout. |
| Status effects / Position | `(-40, -250)` | Column position relative to the top-right corner. Negative X moves left; negative Y moves down. |
| Status effects / Scale | `0.8` | Row size, from `0.5` to `2`. Spacing is adjusted separately. |
| Status effects / Row spacing | `40` | Distance between rows, from `20` to `150`. |
| Status effects / Hidden effects | Empty | Effect keys to hide from the HUD. Leave empty to show all available effects. |

All settings are local to your client.

## Status effects

**Single column** is enabled by default and shows a vertical list near the default minimap position. Its position stays the same in no-map worlds; it does not follow a moved or resized minimap. Effects keep their existing order.

To hide effects, enter their internal names or raw localization tokens in **Hidden effects**, separated by commas, semicolons, or line breaks. Matching is exact and case-sensitive. For example, `Rested,Resting` hides those two HUD entries. For effects added by other mods, use the keys provided in that mod's documentation or source. Localized display-name lookup is not used.

Filtering also works with **Single column** off. It only changes HUD visibility: the effects still apply, and the Compendium's **Active effects** page remains unchanged. These settings stay in the client config, separate from the saved Compendium layout.

## Saved layout

Your entry order and collapsed entries are saved automatically in **one shared layout**. All characters and modpack profiles using the same Valheim user data folder share these preferences.

The layout is stored separately from the mod config. On Windows:

```text
%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\ReferToCompendium\layout-shared.json
```

Switching, moving, or renaming a modpack profile keeps your preferences. Entries from removed mods retain their saved order and collapsed state for when they return.

Back up this folder to transfer your preferences to another PC; Steam Cloud sync is not guaranteed.

## Compatibility

Uses the existing Compendium entries and their content. Mods that replace the Compendium UI may need additional support. Renamed or translated mod titles can be treated as new entries. Compatibility with every mod combination has not been verified in-game.

Use one status HUD layout mod at a time. When using ReferToCompendium's **Single column** layout:

- **CompactStatusEffects:** disable it and restart the game to clear its existing styling.
- **MyLittleUI:** disable **Enable** in both `Status effects - Map - List` and `Status effects - Nomap - List`, plus **Custom element enabled** in both corresponding `List element` sections.

ReferToCompendium does not change other mods' configs. StatusEffectFilter can still hide effects; an effect hidden by either filter stays hidden.

Source code and license: [GitHub](https://github.com/sighsorry1029/ReferToCompendium).
