# 상태 효과 정렬 기능 검토

> 이전 설계 기록이다. 2026-09-27 사용자 요청으로 이 문서에 따른 순서 편집 구현을 제거했다. 현재 비교와 새 방향은 [1열 HUD·필터 비교 분석](status-effect-hud-comparison.md)을 참고한다.

검토일: 2026-09-27. 대상: ReferToCompendium 1.0.0과 사용자가 제공한 DLL 3개. 이 문서는 구현 제안이며 기능 구현이나 게임 실행 검증 결과가 아니다.

## 권장 방향

**도감에서 개인 순서를 편집하고, HUD의 상태 효과 표시 목록에 그 순서를 적용한다.** 첫 단계는 수동 순서 편집에 집중하고, 아이콘 배치·크기·글꼴은 기존 게임 또는 다른 UI 모드가 담당하게 한다.

사용자가 제공한 세 모드는 상태 효과의 필터링 또는 화면 배치를 처리한다. 검토한 코드에는 사용자가 정한 효과별 순서를 저장하고 복원하는 기능이 없다. 따라서 ReferToCompendium이 이 부분을 맡으면 역할이 자연스럽게 나뉜다.

여기서 정렬 대상은 HUD에 표시되는 상태 효과 아이콘 목록이다. 도감 왼쪽의 `Active effects` 제목 위치, 오른쪽 설명문 안의 효과 순서, HUD 아이콘 순서는 서로 다른 대상이다.

## DLL 비교

| 모드 | 확인한 플러그인 버전 | 실제 역할 | 정렬 기능과의 결합 |
| --- | --- | --- | --- |
| StatusEffectFilter | 1.0.2 | HUD 입력 목록에서 지정한 효과를 제외 | 필터를 통과한 항목만 정렬 |
| CompactStatusEffects | 1.1.2 | 아이콘·이름·시간을 세로 목록으로 배치하고 크기·좌표 조정 | 정렬된 입력 순서대로 세로 배치 |
| MyLittleUI | 1.2.26 | 상태 효과 템플릿, 크기, 간격, 네 방향 배치, 지도 유무별 설정 | 정렬된 입력 순서대로 선택한 방향에 배치 |

CompactStatusEffects의 어셈블리 버전은 1.0.0.0이지만 BepInPlugin 선언은 **1.1.2**다. 파일 메타데이터만 보고 모드 버전을 판단하지 않았다.

### StatusEffectFilter

- Harmony owner는 `Azumatt.StatusEffectFilter`다.
- `Hud.UpdateStatusEffects` Prefix가 `ref List<StatusEffect>` 인수를 `Where(...).ToList()`로 교체한다. 남은 항목의 순서는 유지하며 원본 HUD 호출을 계속 실행한다.
- 제외 설정은 `m_name` 원문 또는 현재 언어의 번역명으로 비교한다. 번역명은 언어 변경에 취약하고 같은 표시명을 가진 효과를 구분하지 못할 수 있다.
- 설정 UI의 `On`은 표시가 아니라 **제외 활성화**다. 향후 자체 숨김 기능을 만들면 `HUD에 표시`처럼 결과가 명확한 체크박스를 권장한다.
- 자동완성 후보 수집은 `FejdStartup.Start`에서 수행한다. 이 시점의 ObjectDB만으로 늦게 등록되는 모드 효과까지 모두 알 수 있다고 가정하면 안 된다.

근거: 디컴파일 결과 `StatusEffectFilter/StatusEffectFilter/HudUpdateStatusEffectsPatch.cs:8`, `StatusEffectFilterPlugin.cs:13,56`, `FejdStartupStartPatch.cs:9`, `StatusEffectFilter.ConfigManagerEntry/ToggleStringListConfigEntry.cs:37`.

### CompactStatusEffects

- Harmony owner는 `com.ttolg.compactstatuseffects`다.
- `Hud.UpdateStatusEffects` Postfix에서 `statusEffects[i]`와 private HUD 행 목록의 `[i]`를 대응시킨다. 행의 Y 좌표는 `-i * SpacingY`다.
- 타이머를 `statusEffects[i].GetIconText()`로 다시 쓴다. 아이콘 Transform만 사후 재배열하면 다른 효과의 시간이 붙는 문제가 생길 수 있다.
- F8 설정 창을 위해 플레이어 입력과 마우스 캡처도 패치한다. 도감 안에 편집 UI를 넣는다면 이 별도 입력 제어 방식을 복제할 필요가 없다.

근거: `CompactStatusEffects/CompactStatusEffects/CompactStatusEffectsPlugin.cs:11,81,125`, `Hud_UpdateStatusEffects_Patch.cs:9,52,93`.

### MyLittleUI

- Harmony owner는 `shudnal.MyLittleUI`다.
- `Hud.Awake`에서 자체 템플릿을 준비하고, `Hud.UpdateStatusEffects` Postfix에서 HUD 행의 위치와 텍스트 배치를 바꾼다. 정렬·필터링은 하지 않는다.
- `LeftToRight`, `RightToLeft`, `TopToBottom`, `BottomToTop`은 항목 우선순위가 아니라 **화면에 채우는 방향**이다.
- `WeightSE`라는 별도 HUD 자식이 있으면 일반 효과 목록 뒤에 배치한다. 이런 별도 UI와 수호신 능력 표시 등을 일반 StatusEffect 정렬 목록에 임의로 섞으면 안 된다.
- CompactStatusEffects와 MyLittleUI는 같은 root·행·이름·타이머의 배치를 수정한다. 두 배치 기능을 동시에 활성화하면 실행 순서에 따른 덮어쓰기나 혼합 배치가 가능하다. ReferToCompendium의 순서 정렬로 이 기존 충돌까지 해결할 수는 없다.
- MyLittleUI의 상태 효과 기능 Enable 일부는 서버 동기화 설정이다. 다른 모드 설정을 자동으로 끄지 않고, 실제 서버 설정 제약을 존중해야 한다.
- 인벤토리의 무게 표시 hover에는 상태 효과를 합산한 별도 툴팁도 있다. 이 경로는 SEMan에서 복사한 목록을 사용하므로 HUD 순서를 바꾼다고 자동으로 정렬되지 않는다. 검토한 DLL에는 도감 `TextsDialog`의 목록·본문 메서드 패치가 없었다.

근거: `MyLittleUI/MyLittleUI/CustomStatusEffectsList.cs:14,34,66,100,145`, `MyLittleUI.cs:19,2178,2182,2225,2229`, `InventoryCharacterStats.cs:414`.

## 추천 사용자 흐름

1. 도감의 `상태 효과 정렬 / Status effect order` 버튼으로 전용 편집 패널을 연다. 기존 도감 항목의 위·아래 이동과 시각적 사용법을 맞춘다.
2. 각 행에 아이콘, 현지화된 이름, 현재 활성 여부와 위·아래 버튼을 표시한다. 실제 시간 표시가 있는 효과는 원래 `GetIconText()` 표현을 사용한다.
3. 기본 목록은 현재 효과를 보여준다. 이전에 본 효과도 펼쳐 편집할 수 있게 하면 효과가 사라졌다고 설정까지 잃지 않는다. 미설치 모드의 기록도 보존한다.
4. 순서 변경은 즉시 HUD에 반영하고 자동 저장한다. 세로 UI에서는 위·아래, 기본 HUD에서는 게임의 채우기 순서에 따른 앞·뒤가 된다.
5. `기본 순서`로 사용자 지정 우선순위를 해제할 수 있게 한다. 처음 설치했거나 아직 편집하지 않았다면 원래 입력 순서를 유지한다.

처음부터 HUD 아이콘 자체를 드래그하는 방식은 권장하지 않는다. 전투 중 클릭 처리, 인벤토리의 마우스 모드, 게임패드, 다른 모드의 위치 갱신과 효과 만료가 겹친다. 버튼 방식으로 안정화한 뒤 **편집 패널 내부** 드래그를 추가하는 편이 낫다.

원본 `TextsDialog.AddActiveEffects()`는 효과 설명 전체를 하나의 문자열로 만들어 `TextInfo` 하나에 넣는다. 따라서 설명문을 기존 도감 행처럼 바로 끌어다 놓는 방식은 사용할 수 없다. 실제 효과 객체를 표시하는 별도 편집 행이 필요하다. 기존 설명문은 유지하고, 편집 패널의 생명주기를 도감 열기·닫기에 연결하는 방식을 권장한다.

## 정렬 규칙과 저장

- 첫 버전은 **사용자 지정 순서**만 지원한다. 새로 나타난 미설정 효과는 뒤에 두고, 같은 우선순위와 미설정 효과끼리는 그때 들어온 순서를 유지한다.
- 효과를 관찰해 편집 후보에 추가하는 일과, 사용자 지정 우선순위를 만드는 일을 분리한다. 단순히 도감을 열었다는 이유로 원래 HUD 순서를 영구 고정하지 않는다.
- 이름순 또는 남은 시간순은 나중에 별도 모드로 추가할 수 있다. 특히 남은 시간순은 계속 위치가 바뀌며, 무기한 효과와 커스텀 타이머의 정책도 필요하다. 기본값으로 삼지 않는 편이 좋다.
- 버프/디버프 자동 분류도 첫 단계에서 제외한다. 원본 StatusEffect에는 모든 모드에 통하는 단일 버프/디버프 플래그가 없다.
- 저장 키는 번역된 이름이나 UI 텍스트가 아니라 **원본 효과 이름 `StatusEffect.name`**을 기준으로 한다. 원본 `NameHash()`도 이 이름에서 계산되며 `Clone()`은 `MemberwiseClone()`이다. 임의의 `(Clone)` 문자열 제거 또는 표시명 기반 ID는 도입하지 않는다.
- 비정상적으로 빈 이름 또는 충돌하는 모드 효과가 발견되면 선언 타입·어셈블리 등을 보조 식별에 사용하고, 그래도 구분할 수 없는 항목은 안정된 개별 저장을 보장하지 않는다. 실행 중 객체 ID나 순번을 영구 키로 쓰지 않는다.
- Shared 정책을 그대로 유지한다. 같은 사용자 데이터 폴더의 `ReferToCompendium/status-effects-shared.json`에 저장하는 것을 권장한다. 파일을 나누어도 저장 범위 정책은 하나이며, 기존 도감의 `layout-shared.json` 형식과 데이터는 유지된다.
- config에는 `Status effect sorting / Enabled` 같은 기능 스위치만 두고 개인 순서는 JSON에 둔다. 기존 저장 코드의 원자적 교체·백업·손상 보호를 재사용하되, 도감의 `collapsed`를 HUD 숨김 의미로 재해석하지 않는다.

HUD 숨김은 순서 정렬과 별도 기능이다. 추가한다면 `HUD에 표시` 체크와 편집 패널의 `숨긴 항목` 그룹으로 제공하고, 실제 효과나 능력치는 그대로 유지한다. 도감 제목을 접는 동작이 HUD 효과 숨김으로 연결되어서는 안 된다. StatusEffectFilter가 외부에서 숨긴 효과는 이 모드의 표시 체크로 되살리지 않는다.

## 구현 지점

권장 처리 순서는 다음과 같다.

```text
게임이 만든 HUD 후보 목록
  → StatusEffectFilter의 제외 처리
  → ReferToCompendium의 사용자 순서 적용
  → 원본 Hud.UpdateStatusEffects
  → CompactStatusEffects 또는 MyLittleUI의 화면 배치
```

- `Hud.UpdateStatusEffects(List<StatusEffect>)`에 Prefix를 추가하고 `HarmonyAfter("Azumatt.StatusEffectFilter")`를 명시한다. 필요한 경우 낮은 우선순위도 병행하지만 임의의 모든 모드에 대한 최종 실행 순서를 보장한다고 설명하지 않는다.
- 이미 전달된 인수를 대상으로 정렬한다. 독립적인 작업 목록이 필요하면 얕은 사본을 만들어 **ref 인수로 전달**하고, 뒤따르는 원본 및 Postfix가 같은 정렬 결과를 읽게 한다. 원본 호출 직후 인수를 되돌리는 방식은 사용하지 않는다.
- SEMan 또는 ObjectDB에서 새 목록을 만들어 HUD 인수를 덮어쓰지 않는다. 그렇게 하면 다른 모드가 숨긴 효과가 다시 나타날 수 있다. 편집 후보 수집은 HUD 출력 처리와 분리한다.
- `SEMan.GetStatusEffects()`는 실제 내부 List를 반환한다. 이를 직접 Sort/Remove하면 표시뿐 아니라 상태 효과 계산 순서까지 바뀔 수 있으므로 읽기 전용으로만 취급한다. `m_hidden`, `m_icon` 등 효과 객체 자체도 바꾸지 않는다.
- 저장 순위 dictionary는 편집·로드 시 갱신한다. 매 프레임 JSON 읽기/쓰기, ObjectDB 전체 탐색, 현지화 비교를 하지 않는다. 순서 판정은 원래 인덱스를 동률 기준으로 사용해 안정적으로 처리한다.
- HUD의 private `m_statusEffects`는 이번 순서 변경만으로는 직접 접근할 필요가 없다. 향후 필요해지면 원본 접근 제한에 맞춰 Harmony 필드 주입이나 캐시한 AccessTools를 사용한다.
- 새 정렬 패치나 편집 UI의 오류가 기존 raven hint 및 도감 기능 전체를 중단시키지 않도록 초기화·정리를 나눈다. HUD/도감 파괴와 재생성, 로그아웃 시 Unity 객체 참조를 해제한다.
- 편집 중 효과가 만료되면 비활성 표기로 남겨 선택·스크롤을 보존한다. 입력 이벤트 도중 목록을 통째로 재생성하지 않는다.
- 세 DLL을 필수 의존성으로 추가할 필요는 없다. 원본 HUD만으로 동작하고, 이 모드들이 설치된 경우 같은 표시 목록을 함께 사용하는 구조다.

원본 근거: Valheim 1.0.16 `Hud.cs:526,1635`, `SEMan.cs:298,328`, `StatusEffect.cs:77,355`, `TextsDialog.cs:230`, `ObjectDB.cs:9,70`. `Hud.UpdateStatusEffects`와 HUD 행 목록은 private이며, `GetHUDStatusEffects`·`GetStatusEffects`·`NameHash`는 public이다.

## 구현 후 확인할 범위

- 순서 정책의 단위 검사: 미설정 항목, 동일 우선순위, 비활성·재등장, 미설치 모드 보존, 언어 변경, 초기화, 충돌 키.
- HUD 단독, StatusEffectFilter 병용, CompactStatusEffects 병용, MyLittleUI 병용을 각각 실행한다. 필터와 배치 모드 한 개를 함께 사용하는 조합도 확인한다.
- 아이콘·이름·타이머가 항상 같은 효과를 가리키는지, 효과 추가/만료/갱신/사망 후에도 맞는지 확인한다. 원래 효과의 수치와 만료 시점이 변하지 않아야 한다.
- 지도/노맵, UI 배율, 긴 목록, 게임패드, 도감 닫기·재열기·로그아웃을 확인한다. MyLittleUI의 `WeightSE` 등 별도 표시는 따로 확인한다.
- 두 배치 모드 동시 사용은 별도 충돌 사례로 다룬다. 코드상의 합성 가능성과 실제 동시 실행 성공을 구분한다.

## 분석 자료와 한계

원본 DLL은 수정하거나 publicize하지 않았다. ILSpy CLI **9.1.0.7988**로 디컴파일했으며 결과는 프로젝트의 `obj/status-effect-review/<모드명>/`에 있다. 위 모드 코드 경로는 이 폴더 기준이다. 제품 코드·사용자 config·저장 데이터·기존 Release ZIP은 변경하지 않았고 빌드·설치·게시를 수행하지 않았다.

게임 자료는 현재 프로젝트가 참조하는 설치본과 SHA-256이 일치하는 아래 원본 스냅샷의 기존 추출 결과를 사용했다. 전체 게임 재추출 또는 다른 모드의 버전 대응 작업을 수행한 것은 아니다.

```text
C:\Users\blizz\.codex\references\valheim\snapshots\client-b25527674-windows-x64-20260925T211211Z
  \derived\ilspy-9.1.0.7988-r1\assemblies\assembly_valheim\csharp
```

| 입력 | 식별 정보 / SHA-256 |
| --- | --- |
| StatusEffectFilter.dll | `DA45E7F779218A12D9E2EEAF649ADDC3E42FCE8E539BB21792C65086488EAC20` |
| CompactStatusEffects.dll | `0A4AE60AF7DBE164F1CBAA79F92A01F5AA4AB7377FDA93D0E0A4CF34ED9DD8E4` |
| MyLittleUI.dll | `94424503353CCD2FABDD24A873A68FA515ECF9B69DBA042DD965E051E2096664` |
| 원본 assembly_valheim.dll | Valheim 1.0.16 / Steam build 25527674 / `96CFC004F7F4A6F30D070BEF39EAFD79C466A137121C4665A2F19FB9C15C6127` |

사용자 DLL 경로의 공통 시작점은 `C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\rtd\BepInEx\plugins`다. 각 하위 폴더는 `Azumatt-StatusEffectFilter`, `Marins-CompactStatusEffects`, `shudnal-MyLittleUI`다. 이번 검토는 제공된 DLL의 정적 분석이며, 해당 프로필의 실제 옵션·전체 패치 순서·세 모드의 게임 실행 호환성은 확인하지 않았다.
