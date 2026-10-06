# Native UI resource validation

Read-only review on 2026-09-26 for Valheim client 1.0.16, Steam build 25527674.

Source snapshot: `C:\Users\blizz\.codex\references\valheim\snapshots\client-b25527674-windows-x64-20260925T211211Z`.
Tool: existing `C:\Users\blizz\.codex\references\valheim\tools\unitypy-resource-review-r1\Scripts\python.exe`, UnityPy 1.25.3.

Only the required existing asset bundles were loaded and the relevant UI object trees inspected; no game assets, references or DLLs were modified. The bundles are under `original\valheim_Data\StreamingAssets\SoftRef\Bundles`. Both hashes matched the snapshot's `manifest.json` before reading and remained identical afterward.

| Bundle | SHA-256 |
| --- | --- |
| `86c3d76e` (MonoScript names) | `e6e94d90b051538457be1a2d8c1734fd062e1264a876c86c6315b7eeb0dfdad8` |
| `d59cfac` (UI and game asset objects) | `0746c6d77ce36ef9329ee94755c4f7fbe690ff68c60eec753046d96810c83133` |

## Raven button and canvas

The snapshot's `manifest_extended` assigns `Assets/UI/prefabs/IngameGui/IngameGui.prefab`, `IngameGui_Inventory.prefab` and `IngameGui_Chat.prefab` to `d59cfac`.

All four serialized inventory instances have a `Canvas` on the same GameObject as `InventoryGui`. Each raven button is named `Texts`, under `root/Info/Texts`, and its `Button.m_OnClick.m_PersistentCalls` contains `OnOpenTexts`, targeting the matching `InventoryGui` component, with call state 2. The implementation discovers this callback, without depending on those object names.

| InventoryGui component path ID | Inventory GameObject path ID | Raven Button component path ID |
| --- | --- | --- |
| `-8509157124019308799` | `3359313825733621505` (`IngameGui_Inventory`) | `-6169847489939294463` |
| `-8188783868199053762` | `1890431904931698238` (`Inventory_screen`) | `8174683270217979454` |
| `-5278109027304891150` | `-7898931693509174030` (`Inventory_screen`) | `-2470849148840665870` |
| `2097470354399936994` | `-7615096620977372702` (`Inventory_screen`) | `597735778945516002` |

## Native ambient bubble

All four `Chat.m_npcTextBase` references point to inactive `NpcDialog` GameObjects. Each root contains only `RectTransform`, `CanvasGroup`, and `Animator`. Its direct children are `Image` (UI Image), `Topic` (TextMeshProUGUI), and `Text` (TextMeshProUGUI), with their RectTransforms and CanvasRenderers. There is no destruction, lifetime, Talker, NpcTalk, network or other gameplay component anywhere in these bubble trees.

| Chat component path ID | m_npcTextBase GameObject path ID |
| --- | --- |
| `-8727717522620230082` | `-791704301188688322` |
| `-5207186843885387294` | `-1499606027551063582` |
| `-4159421624279393038` | `-3690854432596648718` |
| `4194898897794004773` | `7913229464211584805` |

For the standalone Chat prefab, root RectTransform `7831355915150915365` is 450 by 120 before adaptation. Image RectTransform `7918756766413649701` stretches from anchor (0,0) to (1,1) with zero size delta, so resizing the bubble resizes its artwork. Text component `-1504644045149014235` has font size 20 and native white text.

Animator `7595747564129974053` references controller `3294959532273780952` (`NpcDialog`). It has the `visible` parameter, no state-machine behaviours, and these four clips:

| Clip | Path ID |
| --- | --- |
| `npc_dialog_hidden` | `-8120713486749931882` |
| `npc_dialog_visible` | `-4882296224392996888` |
| `npc_dialog_show` | `-8279178089511905950` |
| `npc_dialog_hide` | `722115215349916070` |

All clip event lists are empty. Packed generic bindings target the root CanvasGroup (Unity class ID 225): `m_Alpha` (1574349066), `m_Interactable` (4287062452), and `m_BlocksRaycasts` (3739863151). No transform/position/size bindings occur. The first implementation isolated raycasts but retained these alpha-driving animations; the user subsequently reported fading even after the code's display timer was removed. The fixed presentation now disables/detaches and destroys Animator/legacy Animation components on the hint clone, explicitly sets its CanvasGroups to alpha 1, and keeps the owned parent group noninteractive with `ignoreParentGroups=true`. No original NPC prefab, game UI group, or actual NPC animation is changed.

## Compendium list title bounds

All four `TextsDialog.m_elementPrefab` instances have a direct `name` child whose RectTransform stretches horizontally and vertically: anchorMin (0,0), anchorMax (1,1), anchoredPosition (0,0), sizeDelta (-8,-4), pivot (0.5,0.5). Thus the original right inset is 4 units; reducing `offsetMax.x` reserves horizontal space for editing controls.

| TextsDialog component | Row prefab GameObject | `name` RectTransform |
| --- | --- | --- |
| `-4159216475252181247` | `-1432835584306039039` | `-2170149924403398911` |
| `2138599689112507966` | `5736435095576368702` | `704705342577301054` |
| `-7700560417315906318` | `5621844724159847666` | `4347214127115836658` |
| `6287712077277348322` | `-943720056128290334` | `-1599032317869637150` |

The standalone row is 314 by 32 with top-left anchors/pivot. `m_spacing` is 36. Its name TMP component `382855286044047105` has auto-sizing enabled, min 1/max 24, and overflowMode 3. Standalone `m_listRoot` RectTransform `-4604528310201534719` stretches both axes with sizeDelta approximately (-10,0).

## Scope

This validates the original serialized UI structure, references, animation bindings and relevant source API assumptions. It does not replace in-game rendering/input verification or validate third-party inventory replacements. The implementation fails softly when another UI removes the serialized Compendium callback. No full asset extraction, game launch, multiplayer test or Release build was performed for this review.
