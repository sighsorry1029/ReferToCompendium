# 상태 효과 HUD 비교 — 1열 배치와 config 필터

검토일: 2026-09-27. 사용자가 제공한 StatusEffectFilter, CompactStatusEffects, MyLittleUI DLL을 직접 분석했다. 표의 **기본값은 DLL 선언값**이며, 확인한 `rtd` 프로필의 cfg 값은 별도 절로 구분했다. 원본 DLL은 수정하거나 publicize하지 않았다.

## 결론과 이번 변경

원하는 기능은 **CompactStatusEffects의 세로 1열 배치 + StatusEffectFilter의 HUD 제외 처리**를 중심으로 구성하는 것이 가장 단순하다. MyLittleUI에서는 템플릿 복원, 지도/노맵 구분, 아이콘·이름·시간 배치를 참고할 수 있다.

세 DLL 모두 효과별 사용자 순서를 저장하는 모드는 아니다. 이번 요청에 따라 ReferToCompendium에 추가했던 **Status effect order 버튼·편집 패널·사용자 순서 저장·HUD 정렬 패치·전용 config**를 제거했다. 기존 까마귀 안내와 도감 항목 정렬/접기는 유지했다.

비교 이후 사용자 승인에 따라 **선택적 1열 HUD와 config 필터를 구현했다.** 최종 구성은 아래 6절과 [README](../README.md)에 반영했다. 기존 `status-effects-shared.json` 또는 cfg에 남아 있는 옛 정렬 설정은 사용자 데이터로 보존하며 현재 코드에서 읽거나 쓰지 않는다.

## 기능 비교

| 항목 | StatusEffectFilter 1.0.2 | CompactStatusEffects 1.1.2 | MyLittleUI 1.2.26 |
| --- | --- | --- | --- |
| 핵심 역할 | 일부 효과를 HUD에서 제외 | 고정 세로 1열 스타일 | 방향·간격·크기·템플릿·위치 변경 |
| HUD 패치 | `UpdateStatusEffects` Prefix | 같은 메서드 Postfix | 같은 메서드 Postfix |
| 효과 목록 변경 | 제외 결과로 ref 인수 교체 | 없음 | 없음 |
| 개별 효과 숨김 설정 | 있음 | 없음 | 검토한 상태 효과 기능에는 없음 |
| 효과별 우선순위/순서 저장 | 없음 | 없음 | 없음 |
| 세로 1열 | 배치에 관여하지 않음 | 고정 동작 | `Direction=TopToBottom` |
| 아이콘·이름·시간 스타일 | 변경하지 않음 | 기존 행을 직접 변경 | 별도 템플릿을 만들어 교체 |
| 지도/노맵별 배치 | 없음 | 없음 | 별도 설정 |
| 미니맵 이동·크기 실시간 추적 | 없음 | 없음 | 해당 상태 효과 배치 코드에는 없음 |
| 설정 UI | Configuration Manager 커스텀 드로어 | 자체 F8 창 | BepInEx 설정; 상태 효과 전용 창 없음 |
| 추가 입력 패치 | 없음 | F8 창에서 이동·카메라 입력 제어 | 상태 효과 배치 자체에는 없음 |
| 설정 동기화 | 관련 동기화 코드 없음 | 관련 동기화 코드 없음 | 기능 Enable 일부는 서버 동기화 대상으로 선언 |

CompactStatusEffects는 **어셈블리 버전 1.0.0.0, BepInPlugin 버전 1.1.2**다. 여기서는 실제 플러그인 선언의 1.1.2를 사용한다.

## 1. StatusEffectFilter

### 설정 전체

cfg: `BepInEx/config/Azumatt.StatusEffectFilter.cfg`.

| Section | Key | 타입 | DLL 기본값 | 의미 |
| --- | --- | --- | --- | --- |
| `HUD` | `ExcludedStatusEffects` | string | `LocalizedExample=On,$tokenized_example=Off` | HUD에서 제외할 이름과 개별 활성 여부 |

설정은 이 항목 하나다. 기본 문자열은 예시이며 실제 특정 효과를 기본으로 숨기려는 목록은 아니다. 별도 Enabled, 위치, 간격, 배율, 열 수 설정은 없다. 빈 문자열 또는 모든 항목 Off는 결과적으로 아무 효과도 제외하지 않는다.

### 무엇을 어떻게 필터링하는가

`Hud.UpdateStatusEffects` Prefix는 다음 순서로 동작한다.

1. config를 쉼표로 분리하고 빈 항목을 버린다.
2. 각 항목 전체를 Trim한다.
3. 정확히 `=On`으로 끝나는 항목만 활성 제외 항목으로 선택한다.
4. `=` 앞의 첫 문자열을 제외할 이름으로 사용한다.
5. 효과의 `m_name` 원문 또는 `Localization.instance.Localize(m_name)`와 정확히 일치하면 제외한다.
6. 남은 효과로 새 List를 만들고 `ref statusEffects`를 교체한 뒤 원본 HUD 메서드를 실행한다.

**`On`은 표시가 아니라 ‘숨기기 활성화’다.** 대소문자·공백을 포함한 정확 일치이며 부분 일치, 정규식, 와일드카드는 없다. `Name =On`처럼 이름과 `=` 사이에 공백이 있으면 이름에 공백이 남는다. 실제 HUD 필터는 `=on`을 `=On`과 같게 취급하지 않는다. 반면 설정 UI의 체크 표시에서는 대소문자를 무시하므로 수동 입력은 `=On`/`=Off` 표기를 지켜야 한다.

이 모드는 **Unity Object.name/내부 프리팹 이름을 비교하지 않는다.** 보통 `$...` 형태의 번역 토큰 또는 표시 이름인 `m_name`을 비교한다. 번역된 이름으로 저장하면 언어 변경에 영향을 받을 수 있고 같은 표시명의 효과가 함께 제외될 수 있다.

제외하는 것은 HUD 표시 목록뿐이다. 실제 버프·디버프를 해제하지 않고, SEMan 내부 효과 목록이나 도감 Active effects 설명을 수정하지 않는다. 살아남은 효과의 상대 순서도 유지한다.

### UI·갱신·참고할 점

- Configuration Manager가 제공하는 커스텀 드로어에 전체 On/Off, 개별 체크, 삭제, 입력 추가와 자동완성을 제공한다. 이 DLL이 자체 F1 창을 만드는 것은 아니다.
- 새 항목과 자동완성 선택 항목은 `=On`으로 추가한다. 추가 입력에 `=`가 있으면 거부한다.
- 자동완성은 아이콘과 이름이 있는 ObjectDB 효과의 원문 토큰·번역명을 수집한다. 검색은 대소문자를 무시하는 **접두사 검색**이다.
- `FejdStartup.Start` Postfix가 후보 캐시를 준비한다. 이후 등록된 모드 효과나 언어 변경에 대한 전용 후보 갱신 경로는 없다.
- cfg의 변경·생성·이름 변경을 FileSystemWatcher로 감시하고 `Config.Reload()`한다. 실제 필터는 매 HUD 갱신마다 config 문자열을 다시 파싱한다.
- 새 구현에서는 설정 변경 시 제외 집합을 캐시하고, 후보/언어 변경을 별도로 갱신하는 편이 낫다. 이름 조회 방식과 표시 문구를 그대로 복제할 필요는 없다.

근거: `StatusEffectFilterPlugin.cs:13,56,63,81`, `HudUpdateStatusEffectsPatch.cs:8`, `FejdStartupStartPatch.cs:9`, `ToggleStringListConfigEntry.cs:21,37`, `AutoCompleteBox.cs:101`.

## 2. CompactStatusEffects

### 설정 전체

cfg: `BepInEx/config/com.ttolg.compactstatuseffects.cfg`.

| Section | Key | 타입 | DLL 기본값 | 적용 |
| --- | --- | --- | --- | --- |
| `1 - General` | `Enabled` | bool | `true` | 초기 false면 패치 자체를 설치하지 않음. 설치 후 false면 HUD 꾸미기 중단 |
| `1 - General` | `ToggleGuiKey` | KeyCode | `F8` | 자체 설정 창 열기/닫기 |
| `2 - Position` | `RootX` | float | `-180` | 오른쪽 위 앵커 기준 X |
| `2 - Position` | `RootY` | float | `-290` | 오른쪽 위 앵커 기준 Y |
| `3 - Sizing` | `Scale` | float | `1` | 각 행의 localScale |
| `3 - Sizing` | `SpacingY` | float | `55` | 행 간 세로 거리 |

열 수 설정은 없다. **세로 1열이 고정 구현**이다. root의 anchorMin/anchorMax/pivot을 `(1,1)`로 설정하고 `(RootX,RootY)`를 적용한다. 각 행 i는 `localPosition=(0,-i×SpacingY,0)`이다.

`Under Minimap`은 **우상단 고정 좌표 프리셋**이다. 미니맵의 실제 RectTransform을 읽어 따라가는 기능은 아니다. 미니맵을 옮기거나 확대하면 이 목록이 자동으로 따라가는 것은 아니다. 좌표와 간격은 UI 좌표계 단위이며 화면 실픽셀과 항상 같지는 않다.

### 행 디자인

| 요소 | 적용 값 |
| --- | --- |
| Icon | 36×36, 왼쪽 중앙 기준 위치 `(18,0)` |
| Cooldown | Icon과 같은 크기·위치 |
| Name | 아이콘 오른쪽 `(46,0)`, 영역 240×44, 글자 13.5, 왼쪽 중앙 정렬, 줄바꿈 없음, 넘침은 말줄임 |
| TimeText | 아이콘 아래 `(18,-20)`, 영역 56×18, 글자 11.88, 중앙 정렬, 줄바꿈 없음 |

이름은 원본 HUD가 쓴 문자열을 사용한다. 시간은 대응하는 `statusEffects[i].GetIconText()`로 다시 쓰고, 빈 문자열이면 시간 오브젝트를 끈다. 아이콘/글자 크기는 개별 config가 아니라 위 상수와 행 Scale로 결정된다. Scale을 키워도 SpacingY가 자동 확대되지는 않는다.

### F8 창과 비활성화 동작

- X/Y 슬라이더: -800~0, ±5 버튼.
- Scale 슬라이더: 0.4~1.6, ±0.05 버튼. 버튼 내부 제한은 0.3~2.0이지만 같은 GUI 갱신에서 슬라이더도 적용된다.
- SpacingY 슬라이더: 30~90, ±2 버튼.
- `Reset (Under Minimap)`은 기본 좌표·배율·간격을 복구하고 config를 저장한다. `Save Settings`도 별도로 있다.
- 이 범위는 자체 GUI 동작이며 config Bind의 값 제한은 아니다. 외부에서 입력한 값에 동일한 범위 검증을 하는 HUD 코드는 없다.
- 창을 열면 `PlayerController.TakeInput` 결과를 false로 만들고, `GameCamera.UpdateMouseCapture` 원본을 건너뛰어 커서를 표시한다.
- `Enabled=false`는 원래 HUD를 완전히 복원하지 않는다. 이전 위치·크기를 저장하는 구조가 아니기 때문이다. 초기 false로 시작한 뒤 true로 바꿀 때 패치를 새로 설치하는 코드도 없다.
- 창이 열린 상태에서 Enabled가 false가 되면 창 그리기만 중단되고 IsGuiOpen 기반 입력 처리가 남을 수 있다. 새 구현에서 이 UI와 입력 패치를 가져올 이유는 적다.
- 자체 파일 감시/Reload는 없다. ConfigEntry 값이 바뀌면 다음 HUD 갱신에 반영되지만, 외부 cfg 파일 편집의 실시간 반영을 모드 자체가 제공하는 것은 아니다.

근거: `CompactStatusEffectsPlugin.cs:55,81,91,129,245`, `Hud_UpdateStatusEffects_Patch.cs:9,37,52,93`, `PlayerController_TakeInput_Patch.cs:5`, `GameCamera_UpdateMouseCapture_Patch.cs:6`.

## 3. MyLittleUI

cfg: `BepInEx/config/shudnal.MyLittleUI.cfg`. 상태 효과 기능은 HUD 전체를 다루는 큰 모드의 일부다.

### 전역 설정

| Section / Key | 타입 | DLL 기본값 | 동작 |
| --- | --- | --- | --- |
| `General / Enabled` | bool | true | 전체 기능 게이트. 변경 시 상태 효과 템플릿·항해 표시 갱신 |
| `General / Lock Configuration` | bool | true | 서버 설정 잠금용 항목 |
| `General / Disable minimap` | bool | false | 작은 미니맵을 숨기고 Nomap 배치 선택 |
| `General / Status effects and forecast position fix` | bool | true | 예전 Map 기본 좌표 (-170,-240)를 현재 기본 좌표 (-170,-265)로 보정 |

Enabled와 Lock Configuration은 `serverConfig`로 등록한다. 아래 표의 동기화 표시는 `ConditionalConfigSync`에 전달한 동기화 인수를 뜻하며, 실제 서버 적용·편집 제한은 접속 조건과 잠금 설정에 달려 있다. 서버에서 적용된 실효 값은 이번에 확인하지 않았다.

### 목록과 행 설정

아래에서 Map은 `Status effects - Map - ...`, Nomap은 `Status effects - Nomap - ...` 섹션이다.

| Section 끝부분 | Key | 타입 | Map 기본값 | Nomap 기본값 | 동기화 선언 | 동작 |
| --- | --- | --- | --- | --- | --- | --- |
| `List` | `Enable` | bool | true | true | 서버 동기화 | root 이동과 매 프레임 행 배치 |
| `List` | `Position` | Vector2 | (-170,-265) | (-170,-70) | 로컬 | root anchoredPosition |
| `List` | `Direction` | enum | TopToBottom | TopToBottom | 로컬 | 채우는 방향 |
| `List` | `Spacing` | int | 8 | 10 | 로컬 | 아이콘 크기에 추가하는 간격 |
| `List element` | `Custom element enabled` | bool | true | true | 서버 동기화 | 커스텀 템플릿 사용 |
| `List element` | `Size` | int | 32 | 36 | 로컬 | 아이콘 크기와 행 간격 계산에 사용 |

`Direction`은 LeftToRight, RightToLeft, TopToBottom, BottomToTop의 네 방향이다. 항목 i의 이동량은 `i × (Size + Spacing)`이다. 따라서 기본 TopToBottom의 행 간격은 **Map 40, Nomap 46**이다. Compact의 SpacingY=55와 동일한 의미의 설정이 아니다.

`Size`는 `Custom element enabled=false`일 때도 이 간격 계산에 사용된다. 원본 행 모양에 MyLittleUI 배치만 사용하는 경우에도 영향을 준다.

목록과 행은 **별도 스위치**다. List Enable이 false여도 Custom element가 true이면 커스텀 템플릿이 남을 수 있다. 다른 모드가 상태 효과 디자인을 전담하게 하려면 현재 사용하는 Map/Nomap 양쪽의 List Enable과 Custom element enabled를 함께 검토해야 한다. 서버 동기화 대상으로 선언된 값은 접속 서버 설정에 의해 제한될 수 있다.

### 템플릿·텍스트·갱신

`Hud.Awake` Postfix에서 원본 템플릿과 root 위치를 보관하고, `SE_Template_MLU`라는 사본을 만든다. 아이콘과 Cooldown을 Size×Size로 맞추고 이름·시간을 옆에 배치한다. Name/TimeText 영역 폭은 200, 높이는 Size+4이며 글자 크기 설정은 Size/2다. 행 이름·시간의 세로 정렬과 줄바꿈은 시간 표시 유무에 따라 HUD Postfix에서 조정한다.

템플릿 재설정 시 마지막 HUD 행을 제거해 원본 HUD가 항목 수 차이를 보고 행을 다시 만들게 한다. 기능을 끌 때는 보관한 원본 root 위치·템플릿을 선택하는 복원 경로가 있다. Compact가 기존 행 값을 계속 덮어쓰는 방식과 다른 점이다. 다만 두 모드가 함께 수정한 최종 상태까지 완벽하게 복원한다는 뜻은 아니다.

지도 모드의 Enable/Position/Custom element/Size 변경은 템플릿 재설정 이벤트를 호출한다. Direction/Spacing은 Postfix가 현재 값을 매번 읽으므로 다음 갱신에 반영된다. 노맵 쪽은 여섯 설정 모두 템플릿 재설정 이벤트가 있다. `Game.UpdateNoMap` 후에도 배치를 갱신한다.

노맵 설정 선택 조건은 `Game.m_noMap || General / Disable minimap`이다. 미니맵이 화면에 보이는지 매번 조사하는 방식은 아니다. `Position`은 root의 고정 anchoredPosition이며 미니맵 이동/크기 추적 기능이 아니다.

### 함께 움직이는 항해 표시

이 설정은 일반 상태 효과 아이콘 목록과 구분해야 한다. 섹션은 각각 `Status effects - Map - Sailing indicator`, `Status effects - Nomap - Sailing indicator`다.

| Key | 타입 | Map 기본값 | Nomap 기본값 | 동기화 선언 |
| --- | --- | --- | --- | --- |
| `Enabled` | bool | true | true | 서버 동기화 |
| `Sail power indicator position` | Vector2 | (-350,-290) | (-350,-320) | 로컬 |
| `Sail power indicator scale` | float | 1 | 1.1 | 로컬 |
| `Wind indicator position` | Vector2 | (-350,-140) | (-350,-170) | 로컬 |
| `Wind indicator scale` | float | 1 | 1.1 | 로컬 |

원래 위치를 보관하고 활성 상태에 따라 항해 중 바람·돛 표시 위치/배율을 변경한다. 새 ReferToCompendium의 1열 상태 효과 기능에 이 부분까지 자동 포함할 필요는 없다.

### 특수 표시와 경계

- root 아래 별도 `WeightSE`가 있으면 일반 효과 개수 다음 위치로 배치한다. 이 오브젝트를 StatusEffect 목록의 한 항목이라고 가정하면 안 된다.
- 인벤토리 무게 표시 hover에 효과 정보를 합산한 별도 툴팁이 있다. 일반 HUD 필터와 별개이며 HUD에서 숨긴 효과가 여기에 표시될 수 있다.
- 날씨 예보와 상태 효과 root의 위치가 같은 주변 공간을 사용한다. `General / Status effects and forecast position fix`는 특정 예전 기본 위치를 보정하는 설정이며 미니맵 추적 기능이 아니다.
- 이 모드는 항목별 필터나 사용자 우선순위를 구현하지 않는다. 검토한 상태 효과 코드에서 도감의 Active effects 목록도 패치하지 않는다.

효과 툴팁 설정은 `Stats - Character / Show character active effects on weight hover`이며 bool, 기본 true, 로컬 설정이다. `InventoryGui.UpdateCharacterStats` Postfix에서 실제 SEMan 효과 목록을 복사해 약 3초 간격으로 설명을 갱신한다. 장비 착용/해제와 SEMan의 효과 추가·삭제 Postfix는 캐시를 무효화하며 효과 자체를 제거하거나 HUD에서 숨기지 않는다. `InventoryGui.OnDestroy` Postfix에서 툴팁을 정리한다.

같은 공간을 사용하는 별도 HUD로 `Info - Forecast / Enabled`와 `Info - Winds / Enabled`가 있으며 둘 다 기본 true다. 예보 기본 Position은 (-78,-255), Nomap은 (-78,-55)다. 미래 풍향의 `Info - Winds - List / Position`은 (-180,-255), `Nomap Position`은 (-180,-55)다. 이들은 상태 효과 행이 아니므로 상태 효과 필터가 숨길 대상으로 가정하면 안 된다. `Info - Forecast - Lists`의 Rain/Snow/Thunder/Mist/Ash Rain은 **날씨 분류 목록**이며 상태 효과 제외 목록이 아니다.

근거: `MyLittleUI.cs:1570,2178,2200,2225,2255`, `CustomStatusEffectsList.cs:14,34,66,75,100,139,246`, `InventoryCharacterStats.cs:414`.

## 4. 정확한 패치 지점과 충돌

| 모드 | 대상 | 방식 | 책임 |
| --- | --- | --- | --- |
| Filter | `Hud.UpdateStatusEffects` | Prefix, ref 목록 교체, true 반환 | 제외 후 원본 렌더링 진행 |
| Filter | `FejdStartup.Start` | Postfix | 자동완성 후보 준비 |
| Compact | `Hud.UpdateStatusEffects(List<StatusEffect>)` | Postfix | root·행·아이콘·이름·시간 배치 |
| Compact | `PlayerController.TakeInput` | Postfix | 설정 창이 열리면 입력 불허 |
| Compact | `GameCamera.UpdateMouseCapture` | Prefix | 설정 창이 열리면 원본 skip, 커서 표시 |
| MyLittleUI | `Hud.Awake` | Postfix | 상태 효과 템플릿과 항해 표시 준비 |
| MyLittleUI | `Hud.OnDestroy` | Prefix | 템플릿 참조 정리 |
| MyLittleUI | `Hud.UpdateStatusEffects` | Postfix | 행 배치·텍스트 정렬·WeightSE 위치 |
| MyLittleUI | `Game.UpdateNoMap` | Postfix | 지도/노맵 배치 전환 |

Filter와 Compact에는 별도 Harmony priority/before/after 선언이 없다. MyLittleUI의 Awake Postfix는 `Azumatt.MinimalUI`, `org.bepinex.plugins.passivepowers`보다 먼저, HUD 갱신 Postfix는 이 둘보다 나중에 실행되도록 선언한다. **Compact와 MyLittleUI 사이의 우선순위를 지정한 선언은 없다.**

각 Harmony owner는 `Azumatt.StatusEffectFilter`, `com.ttolg.compactstatuseffects`, `shudnal.MyLittleUI`다.

Filter와 배치 모드 하나의 조합은 `필터 Prefix → 원본 HUD → 배치 Postfix`로 역할이 구분된다. Compact와 MyLittleUI를 동시에 켜면 같은 root, 행, Icon, Name, TimeText를 수정하므로 실행 순서에 따라 값이 덮어써지거나 스타일이 섞일 수 있다. 한 목록의 배치 담당은 하나로 정하는 것이 좋다.

두 배치 모드 모두 효과 데이터 `[i]`와 HUD 행 `[i]`가 대응한다고 전제한다. 새 필터는 원본 HUD가 행을 구성하기 **전**에 입력 목록을 거르고, 효과 객체 자체와 SEMan 목록은 유지해야 한다. HUD가 만들어진 뒤 행만 숨기는 방식은 빈칸·시간/아이콘 불일치를 만들 수 있다.

## 5. 확인한 rtd 프로필의 현재 cfg

읽기 전용으로 `rtd/BepInEx/config`의 해당 모드 설정만 확인했다. MyLittleUI cfg가 존재하며 다음 값이 기록되어 있었다.

| 항목 | 현재 파일 값 |
| --- | --- |
| General / Enabled | true |
| General / Disable minimap | false |
| Map / List Enable, Custom element enabled | 모두 true |
| Map / Position, Direction, Spacing, Size | (-170,-265), TopToBottom, 8, 32 |
| Nomap / List Enable, Custom element enabled | 모두 true |
| Nomap / Position, Direction, Spacing, Size | (-170,-70), TopToBottom, 10, 36 |
| Map/Nomap Sailing indicator | 위 표의 DLL 기본값과 동일 |
| General / Status effects and forecast position fix | true |

따라서 **파일 설정 기준으로 MyLittleUI의 1열 배치는 이미 켜져 있다.** 이 기능을 그대로 사용하면 새 모드에는 필터만 추가하는 구성도 가능하다. ReferToCompendium이 배치까지 담당하게 할 경우 기존 배치 기능과 중복되지 않게 해야 한다.

확인한 config 디렉터리에서는 `Azumatt.StatusEffectFilter.cfg`, `com.ttolg.compactstatuseffects.cfg`가 발견되지 않았다. 이것만으로 두 DLL의 로딩 성공·실패나 현재 실행 상태를 판단하지 않았다. MyLittleUI cfg 값도 서버 동기화 이후의 실제 메모리 값이나 화면 렌더링을 확인한 결과는 아니다. 세 모드의 설정 파일은 수정하지 않았다.

## 6. ReferToCompendium의 최종 구현

### 범위

- 기본 순서는 게임이 전달한 순서를 유지한다. 효과별 이동 UI·우선순위·과거 관찰 목록·개인 순서 JSON은 사용하지 않는다.
- 표시할 효과를 config로 거른 다음, 남은 항목을 미니맵 아래 한 열에 배치한다.
- 이름과 타이머는 게임의 현지화와 `GetIconText()`를 사용한다. 실제 효과·능력치·만료 시간·도감 본문은 변경하지 않는다.
- 배치와 필터를 독립적으로 켤 수 있게 한다. MyLittleUI의 1열 배치를 유지할 때도 필터를 사용할 수 있다.
- F8 창과 별도 입력 패치는 추가하지 않고 config/Configuration Manager에서 조정한다. 효과 선택·자동완성 UI는 추가하지 않았다.

### 다섯 가지 클라이언트 config

| Section | Key | 기본값 | 용도 |
| --- | --- | --- | --- |
| `Status effects` | `Single column` | true | 세로 1열 배치. 기본 켜짐이며 다른 UI 모드의 상태 효과 배치와 중복 사용하지 않음 |
| `Status effects` | `Position` | (-40,-250) | 우상단 기준 열의 오른쪽 위 위치 |
| `Status effects` | `Scale` | 0.8 | 행 전체 배율, 0.5~2 |
| `Status effects` | `Row spacing` | 40 | 행과 행 사이 거리, 20~150. 배율과 별개 |
| `Status effects` | `Hidden effects` | 빈 문자열 | 제외할 내부 효과 이름/번역 토큰 목록 |

위 좌표는 **고정 좌표 방식의 기본값**이다. 미니맵을 옮겨도 따라가지 않으며 노맵에서도 같은 좌표를 사용한다. 각 행은 오른쪽 위 기준점에서 왼쪽으로 폭 250, 아래로 높이 44만큼 배치되고 행 Scale이 적용된다. 아이콘 36, 이름 글자 18, 시간 글자 14이며 실제 글꼴과 효과 내용은 기존 게임 UI를 사용한다.

필터 키는 효과의 내부 이름(Object.name) 또는 `$...` 원문 번역 토큰이다. 번역된 표시명을 조회하지 않는다. 쉼표·세미콜론·줄바꿈으로 구분하고 항목 주위 공백은 제거하며 대소문자는 구분한다. 런타임 이름 끝의 `(Clone)`은 제거하므로 config에는 원래 이름을 쓴다. **StatusEffectFilter의 기존 문법과 다르게** `=On`/`=Off` 없이 목록에 들어가면 숨김으로 해석한다. 비어 있으면 자체 필터가 아무 효과도 제외하지 않는다. 별도 Filtering 스위치 없이, 배치가 꺼져 있어도 필터가 적용된다.

### 적용 방식과 검증 경계

1. 실제 HUD 행만 전용 컨테이너로 옮긴다. 이 컨테이너는 원본 상태 효과 root의 자식이므로 숨김·투명도를 상속하며, 원본 root·템플릿과 별도 WeightSE는 수정하지 않는다. 행의 원래 부모·위치·스타일을 보관하고 배치를 끄면 복구한다. 아이콘·텍스트 내용·타이머 활성 상태는 게임이 매번 갱신한 값을 유지한다.
2. Prefix에서 입력 목록을 거른다. 제외가 있을 때만 목록 사본으로 교체하며 원본 목록·효과 객체와 상대 순서는 유지한다. config 변경 시 불변 HashSet을 교체하고 매 프레임 문자열을 재파싱하지 않는다. 이미 외부 필터가 제거한 효과를 다시 채우지 않는다.
3. 상태 효과 패치는 별도 Harmony owner `sighsorry.ReferToCompendium.StatusEffects`로 설치한다. 필터는 StatusEffectFilter 뒤에서, 배치는 MyLittleUI/CompactStatusEffects 뒤에서 실행하도록 지정했다. 이는 동시 배치 사용을 보장하는 것이 아니며 배치 담당은 하나만 켜야 한다. 필터나 배치 오류는 기존 도감 기능과 분리해서 처리한다.
4. 원본 게임 DLL의 접근 제한을 따른다. private `Hud.UpdateStatusEffects`는 설치 시 조회하고 private HUD 행 목록은 Harmony 필드 주입을 사용한다. publicize나 참고 모드 DLL 의존성을 추가하지 않았다.
5. 원본 API·관리 런타임 필터 검사는 통과했다. 기본/모드 효과의 실제 배치, 추가·만료, UI 배율·긴 목록·노맵, 비활성화 복원과 타 HUD 모드 병용은 실제 게임 화면에서 확인해야 한다.

## 7. 근거와 검증 범위

분석 도구: ILSpy CLI 9.1.0.7988. 디컴파일 결과는 프로젝트의 `obj/status-effect-review/<모드명>/`에 있다. 위 코드 위치는 각 모드의 디컴파일 소스 기준이다. 원본 게임 근거는 현재 ReferToCompendium이 참조하는 Valheim 1.0.16 / Steam build 25527674의 보관 원본과 기존 추출 자료다.

DLL 공통 경로:

```text
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\rtd\BepInEx\plugins\
```

| 하위 경로 | SHA-256 |
| --- | --- |
| `Azumatt-StatusEffectFilter/StatusEffectFilter.dll` | `DA45E7F779218A12D9E2EEAF649ADDC3E42FCE8E539BB21792C65086488EAC20` |
| `Marins-CompactStatusEffects/CompactStatusEffects.dll` | `0A4AE60AF7DBE164F1CBAA79F92A01F5AA4AB7377FDA93D0E0A4CF34ED9DD8E4` |
| `shudnal-MyLittleUI/MyLittleUI.dll` | `94424503353CCD2FABDD24A873A68FA515ECF9B69DBA042DD965E051E2096664` |

확인한 cfg는 `C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\rtd\BepInEx\config\shudnal.MyLittleUI.cfg`다. 서버별 실효 설정이나 실제 UI는 확인하지 않았다. 코드로 확인한 동작과 실제 게임 호환성 검증을 구분한다.

순서 편집 제거 후 ReferToCompendium은 Debug 빌드 경고 0 / 오류 0, 원본 API 참조 601곳 오류 0, 원본 Mono의 기존 도감 접근 14개와 저장 검사 52개를 통과했다. 게임 폴더 DLL 갱신 및 SHA-256 일치도 확인했다. 이는 세 참고 모드 자체의 실행 검증이나 새 1열 기능 구현을 의미하지 않는다.

이후 새 HUD 구현은 Debug 빌드 경고 0 / 오류 0, 원본 API 참조 773곳 오류 0, 원본 Mono의 접근·실제 필터 검사 38개와 기존 저장 검사 52개를 통과했다. 게임 폴더 DLL과 빌드 DLL의 SHA-256 일치를 확인했다. Unity 렌더링·복원 및 실제 Harmony detour 설치는 이 검사 범위에 포함되지 않는다. 상세 이력은 [VALIDATION.md](../VALIDATION.md)에 기록한다.
