# 개발 검증 기록 — 2026-09-26

## 대상과 참조

- Valheim Windows x64 클라이언트 **1.0.16**, Steam build **25527674**.
- 시작 시 설치본의 `appmanifest_892970.acf` buildid를 확인했다.
- 보관 원본: `C:\Users\blizz\.codex\references\valheim\snapshots\client-b25527674-windows-x64-20260925T211211Z\original`.
- 분석: 같은 스냅샷의 `derived/ilspy-9.1.0.7988-r1/assemblies`에 보존된 원본 디컴파일 결과.
- 설치본과 보관 원본 `assembly_valheim.dll` SHA-256은 모두 `96CFC004F7F4A6F30D070BEF39EAFD79C466A137121C4665A2F19FB9C15C6127`로 일치한다.
- 실제 빌드 참조는 설치본 `valheim_Data/Managed`와 `BepInEx/core`다. publicizer, 접근 검사 우회 특성, 병합 DLL 또는 외부 모드 종속성을 추가하지 않았다.

## 구현 경계

`TextsDialog.Setup` 후 한 프레임 지연해 원래 목록을 정리한다. CreatureManager가 `UpdateTextsList` 후 추가하는 `$cm_compendium_tab` 항목과 원래 버튼/`TextInfo`를 보존한다. 본문 표시에는 원래 `ShowText(TextInfo)`를 호출하므로 다른 모드의 해당 Harmony 패치가 계속 적용되는 구조다. CreatureManager와 동시 실제 게임 실행을 검증한 것은 아니다.

`m_texts`, `m_selectionIndex`, `m_baseListSize`는 원본의 **private** 필드다. 캐시한 Harmony 필드 접근자를 사용한다. `ShowText(TextInfo)`도 private이며 캐시한 `MethodInfo`를 통해 호출한다. 목록 갱신 전 숨긴 행을 복구하여 원래 게임의 행 파괴 과정에서 누락되지 않게 한다. 숨긴 행은 기본 패드 이동 목록에서 제외한다.

까마귀 안내는 `Chat.m_npcTextBase`의 UI 사본이다. `Chat.SetNpcText`, `Talker`, NPC RPC, 월드 위치 투영을 사용하지 않는다. 사용자 화면 피드백에 따라 표시/반복 타이머와 세션 내 영구 숨김을 제거하고, 안내 사본의 Animator/Animation도 제거했다. 자체 CanvasGroup 알파를 LateUpdate에서 1로 유지하며 상위 UI 그룹의 알파를 상속하지 않는다. 인벤토리가 열린 동안 계속 표시하고, 도감 등 모달을 닫으면 바로 복귀한다. 버튼의 Icon RectTransform 안 (0.10, 0.72)을 부리 위치로 삼아 왼쪽에 우측 정렬하며 화면 여유 폭에 맞춰 줄바꿈한다. config로 즉시 켜기/끄기와 문구 변경이 가능하다.

## 초기 구현 자동 검사

- `tests/LayoutStateChecks`: **52개 통과**. .NET 10, .NET Framework 4.8, 아래 게임 Mono에서 순서/접기, 없는 모드의 상태 보존과 재등장, 초기화 범위, Unicode, JSON 왕복, 백업 복구, 손상 원본 보존, 알 수 없는 형식 보호와 크기 제한을 검사했다.
- `tests/OriginalApiChecks`: 최종 DLL의 게임/Unity 직접 멤버 참조 **583곳**, 원본 UI 메타데이터 검사 **오류 0개**. 원본에서 resolve하고 직접 private/internal 접근 여부를 검사했다. 도감의 비공개 필드 타입/접근성, Harmony 대상과 raven callback/프리팹 공개 필드도 확인했다.
- `tests/RuntimeChecks/Verify-GameCompatibility.ps1`: 보관 원본의 **Mono 6.13.0에서 14개 접근 검사 통과**. 최종 플러그인의 실제 캐시 필드 접근자를 생성하고, 생성자를 실행하지 않은 TextsDialog 관리 객체에서 private 필드 3개를 읽고 썼다. private ShowText(TextInfo)는 원본 선언과 시그니처까지만 확인했다. UI 메서드 본문, Harmony 네이티브 detour 설치, Unity 씬은 실행하지 않았다. 게임 Mono에서 같은 저장 검사 52개도 통과했다.
- 원본 UI 번들 2개의 해시 및 네 가지 인벤토리 변형의 raven callback/Canvas, NPC 대사 프리팹·애니메이션, 목록 제목 앵커를 읽기 전용으로 확인했다. [리소스 검증 상세](docs/raven-prefab-validation.md).
- 최종 `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: **경고 0 / 오류 0**. 최종 플러그인 DLL만 복사했으며 MSBuild와 별도 Get-FileHash에서 원본·대상의 SHA-256 일치를 확인했다.

초기 구현 검사 당시 DLL과 설치한 DLL의 SHA-256은 모두 `0401EFEEF1FCA4874E87FFB7AAB056C2D39BA930CB730C343DFA42B83EB0E443`이었다.

- 빌드: `C:\Users\blizz\RiderProjects\ReferToCompendium\bin\Debug\ReferToCompendium.dll`
- 설치: `C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\ReferToCompendium.dll`

## 사용자 화면 피드백 후 안내 위치·상시 표시 수정

- 사용자 스크린샷에서 버튼 오른쪽 위에 표시된 뒤 사라지는 기존 동작을 확인했다.
- 원본의 네 가지 인벤토리 모두 raven Button의 `targetGraphic`가 자식 `Icon` Image인 것을 재확인했다. Icon은 64×64, 버튼 중심 대비 (-2,-2)이며 같은 sprite를 사용한다. 이미지 안 부리 앵커 (0.10, 0.72)는 스크린샷 기반 근사값으로, 실제 화면에서 최종 위치는 미검증이다.
- 표시 지연·7초 만료·20초 반복과 도감 방문 후 세션 내 숨김을 제거했다. 모달 표시 중에만 숨기며 다시 인벤토리로 돌아오면 표시한다.
- Debug 빌드 및 `DeployToGame=true` 자동 복사: 경고 0 / 오류 0, 최종 DLL과 게임 폴더 DLL의 SHA-256 일치.
- 수정 DLL의 게임/Unity 직접 참조 **593곳**과 원본 UI 메타데이터 검사 오류 0개. 저장 로직과 private 접근 경로는 변경하지 않아 관련 기존 검사는 반복하지 않았다.
- 이 수정 단계의 DLL SHA-256: `C5DBAF190CFF6249E0110834F6C32DFCC6111EA027A531CE2335500B91486FDA`.
- 빌드 당시 게임 프로세스가 실행 중이므로 새 DLL 적용에는 게임 재시작이 필요하다. 수정 UI의 실제 표시·입력은 아직 실행 확인하지 않았다.

## 추가 화면 피드백 후 페이드 제거·기본 문구 단축

- 타이머 제거 후에도 페이드가 남는다는 사용자 보고를 받았다. 원본 Chat의 파라미터 이름은 소문자 `visible`로 기존 코드와 일치하지만, 안내 사본에 알파를 구동하는 NPC Animator가 남아 있음을 확인했다. 이 경로와 실제 사용자의 페이드 간 인과관계는 런타임에서 측정하지 않았다.
- 안내 사본의 Animator를 비활성화하고 컨트롤러를 분리한 뒤 제거하며, legacy Animation이 있으면 함께 중지·제거한다. 이후 사본의 CanvasGroup.alpha를 1로 설정한다. 자체 레이어는 ignoreParentGroups=true, noninteractive, non-raycast를 유지한다. 다른 NPC나 원본 프리팹은 수정하지 않는다.
- LateUpdate에서 자체 그룹 알파를 1로 유지한다. 기본 문구는 `Click for more info\nCaw!` / `클릭해서 정보 확인\n까악!`으로 단축했다. 기존 사용자 지정 문구는 덮어쓰지 않는다.
- Debug 빌드 및 자동 복사: 경고 0 / 오류 0. 수정 DLL의 원본 API 참조 **601곳**과 UI 메타데이터 검사 오류 0개. 빌드 DLL과 설치 DLL의 SHA-256 일치를 별도로 확인했다.
- 이 수정 단계의 DLL SHA-256: `31130A27428BB6E0EE01A2AE7FA741720AA736E52E8AA50874FADD9566BFA96B`.
- 게임 실행 중에 DLL을 갱신했다. 게임 재시작 후의 실제 장시간 고정 표시, TAB 반복, 모달 복귀 화면은 아직 확인하지 않았다. 저장·도감 비공개 접근 코드는 변경하지 않았다.

## English README and Release packaging setup

- Rewrote the root README in English for users; build instructions now live in `docs/BUILDING.md`.
- Used NoCapLumber's DLL-derived version and five-file package layout as the reference. `Thunderstore/manifest.json` is now the single manifest source; corrected its copied Homestead identity and aligned its template/changelog with the existing 0.1.0 plugin version. Kept the supplied 256x256 icon and website URL.
- Added an automatic Release-only `MakeThunderstorePackage` target and a read-only `ValidateThunderstorePackage` target. The packaged manifest takes its version from the built DLL. The script checks archive entry hashes before renaming a temporary file to the final ZIP name.
- Debug build with `DeployToGame=true`: 0 warnings / 0 errors, installed DLL SHA-256 matches `31130A27428BB6E0EE01A2AE7FA741720AA736E52E8AA50874FADD9566BFA96B`. No ZIP was produced.
- `ValidateThunderstorePackage` passed under Windows PowerShell 5.1: DLL identity, manifest/dependency, all five input files, and 256x256 PNG header. Release property evaluation confirmed `BuildPackages=true` and the intended output directory without building Release.
- Isolated API checks exercised ZIP create/read in memory with a dummy entry and atomic replacement using temporary text files. Found and fixed PowerShell's `$null`-to-empty-string conversion for `File.Replace` by using `[NullString]::Value`.
- No Release build or distribution ZIP was created for this setup request. The complete archive-producing build path has not yet been executed; uploader state and publishing were not inspected.

## Shared 저장 정책 고정

- 저장 범위 설정과 모드팩 프로필 경로 해시를 제거했다. 같은 Valheim 사용자 데이터 폴더를 사용하는 캐릭터·모드팩에서 `ReferToCompendium/layout-shared.json` 하나를 사용한다.
- 기존 Shared 파일 경로와 JSON 형식은 유지했다. 현재 사용자 폴더에는 Shared 파일과 백업만 있었으며, 수정 전후 두 파일의 SHA-256이 각각 일치해 저장 데이터를 변경하지 않았음을 확인했다. 없는 모드 항목 보존과 현재 항목만 초기화하는 기존 저장 로직은 변경하지 않았다.
- README에서 범위 선택 항목을 제거하고 공통 저장 정책을 설명했다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: 경고 0 / 오류 0. 빌드 DLL과 게임 폴더에 자동 복사한 DLL의 SHA-256은 모두 `C96E6C640A27E8FABA547368109188CCFD705DB2C0ACAB4A96A1215CB5312D3A`다.
- 이번 변경에서는 실제 게임 실행이나 Release 빌드·ZIP 생성을 하지 않았다.

## 실제 실행으로 남은 확인 범위

이 작업에서 Valheim 게임을 실행하거나 Unity 씬을 만들지 않았다. 컴파일/원본 메타데이터/격리 관리 런타임 검사는 실제 화면·입력·다른 UI 모드와의 실행 호환성을 보장하지 않는다.

1. 인벤토리 열기/닫기, 도감 열기, 말풍선 설정 변경, 다른 화면 크기와 UI 배율에서 까마귀와 안내 위치 확인.
2. 기본·CreatureManager·다른 모드 항목 이동 후 본문과 아이콘 유지 확인.
3. 선택 중인 항목 접기, 모두 접기, 그룹 펼치기/복원, 긴 목록과 반복 열기/닫기 확인.
4. 게임 재실행과 모드팩 변경 후 공통 순서·접기 복원 확인.
5. 패드 기본 목록 이동과 그룹 A 버튼, 마우스/가상 포인터 편집 확인.
6. 번역 키가 있는 항목의 언어 변경 및 번역 완료된 제목만 제공하는 모드의 식별 한계 확인.

초기 구현 단계에서는 서버 설치·접속·멀티플레이 검증, Release ZIP 생성, 업로더 점검, 사이트 게시를 수행하지 않았다.

## 1.0.0 Release 빌드

- 사용자 요청에 따라 플러그인 상수, 프로젝트 버전, manifest와 changelog를 1.0.0으로 맞췄다.
- `dotnet build ReferToCompendium.sln -c Release`: 경고 0 / 오류 0. 기존 Release 타깃으로 `Thunderstore/ReferToCompendium_v1.0.0.zip`을 생성했다.
- DLL 어셈블리 버전과 ZIP 내부 manifest 버전은 모두 1.0.0이며 BepInEx 의존성은 5.4.2351이다.
- ZIP 루트의 manifest, README, changelog, icon, 최종 모드 DLL 5개를 확인했다. 패키징 단계에서 모든 입력과 ZIP 항목 해시가 일치했고, 완성된 ZIP을 다시 열어 DLL·README·changelog·icon과 현재 원본 파일의 SHA-256 일치를 별도로 확인했다.
- Release DLL SHA-256: `C38CC390665CD2271DC1FEB0C2AE53741540D604D07517857AB08295906FDDE3`.
- Release ZIP SHA-256: `EC12B03688F082A301A2737958798CFC009FBFD0800E127AC71A0F3A80187A84`.
- 실제 게임 실행, 업로더 점검, 사이트 게시 확인은 수행하지 않았다.

## 상태 효과 순서 편집 구현 — 2026-09-27

이 절은 구현 당시의 이력이다. 해당 기능은 아래 「상태 효과 순서 편집 제거와 HUD 비교」 작업에서 제거했다.

- 도감 도구 모음에 `Status effect order / 상태 효과 정렬`을 추가했다. 오른쪽 설명 영역의 임시 편집 패널에서 아이콘·이름·활성 여부·타이머, 위/아래 이동, 이전에 본 효과, 정렬 On/Off, 현재 표시 항목의 기본 순서 복원을 제공한다. 기존 본문은 유지하고 패널을 닫거나 주제를 바꾸면 복구한다. 편집은 마우스/가상 포인터를 사용한다.
- 평상시 갱신에서는 행 객체와 순서를 유지한다. 효과가 만료되면 비활성 표기로 남기며 새 항목은 뒤에 추가한다. 명시적인 이동/초기화/필터 변경 시에만 편집 목록을 다시 정렬한다. 이동은 현재 보이는 행의 이웃을 기준으로 하며 없는 효과의 저장 슬롯은 보존한다.
- 효과 관찰과 사용자 지정 우선순위를 분리했다. 처음 설치하거나 보기만 했을 때는 원래 HUD 순서를 유지한다. 이름·타입·어셈블리 단순 이름으로 식별하며 번역명·어셈블리 버전은 ID에 포함하지 않는다. 동일 ID의 HUD 항목은 삭제하지 않고 기존 상대 순서를 유지한다.
- `Hud.UpdateStatusEffects` Prefix는 `Azumatt.StatusEffectFilter` 뒤에서 전달된 목록만 안정 정렬한다. 순서가 달라질 때만 얕은 사본을 ref 인수로 전달한다. SEMan 내부 목록, 효과 값, 외부 필터, HUD 배치·템플릿은 변경하지 않는다. 상태 효과용 Harmony owner를 분리해 초기화 오류가 기존 도감 패치를 해제하지 않게 했다.
- `status-effects-shared.json`을 기존 Valheim 사용자 데이터의 ReferToCompendium 폴더에 저장한다. 기존 도감 파일은 그대로 유지한다. 별도 버전 1 JSON에 관찰 목록과 사용자 우선순위를 나누고 원자적 교체·백업·손상 보존·미래 형식 보호를 적용했다. 새 관찰은 지연 저장하고 사용자 편집은 즉시 저장하며 실패를 편집 화면에 표시한다.
- 원본 네 가지 TextsDialog 프리팹의 설명 영역 ScrollRect 계층을 읽기 전용으로 확인했다. 실제 화면의 크기·글꼴·스크롤·클릭은 실행 검증하지 않았다.
- `tests/StatusEffectOrderChecks`: **87개 통과**(.NET 10, .NET Framework 4.8, 게임 Mono). 미편집 시 기본 순서, 없는 항목 보존, 편집 중 유지된 행 순서, 부분 초기화, Unicode, 원본 목록·객체 불변, 중복·동률, 저장/백업/손상/미래 형식을 확인했다. 초기 net48 검사에서 한 번의 일시적인 파일 교체 실패가 있었고 이후 재검사는 통과했다. 저장 실패 시 원본 보존과 실패 반환 정책을 유지했다.
- 최종 DLL의 원본 게임/Unity API 참조 **912곳**, 원본 private HUD 대상·공개 수집 API·Prefix ref 인수·HarmonyAfter/Priority.Last 검사: **오류 0개**. 세 참고 DLL은 어셈블리 의존성에 포함되지 않았다.
- 원본 **Mono 6.13.0**에서 기존 layout 52개, 상태 순서 87개, 원본 접근 및 실제 제품 HUD Prefix 경계 **37개**(기존 14 + 신규 23) 검사를 통과했다. 실제 Prefix를 반사 호출하여 얕은 참조·중복·동률 보존, 필터된 항목 미복원, 비활성/초기화/빈 목록/null 처리를 확인했다. StatusEffect는 원본 타입의 생성자 없는 객체이며 식별자 캐시를 주입했다. **Unity의 name getter, Harmony detour 설치, 원본 HUD 본문, Unity 네이티브 호출·렌더링·게임 실행은 검사하지 않았다.** 임시 BepInEx 설정 fixture는 사용자 설정과 분리했다.
- Mono 검사 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-4ee22d6d08794712837de3de2731ed6b\stdout.txt`.
- 최종 `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: **경고 0 / 오류 0**. 빌드 DLL과 Steam 게임의 `BepInEx/plugins/ReferToCompendium.dll` SHA-256은 모두 `AE41B6C6BEDA9AEEB0B7B5D14BA33C2603A0E976F60BA4C5086BCD7A411F6165`다.
- 영어 README와 빌드 검사 안내를 갱신했다. 모드 버전은 1.0.0을 유지하고 이번 수정은 Debug DLL로 검증했다. 기존 Release DLL과 ZIP은 이전 해시 그대로 보존되어 있으며 이번 기능은 해당 ZIP에 포함되지 않는다.
- 실제 게임에서 정렬·만료·도감 재열기·로그아웃, UI 배율·노맵·가상 포인터, StatusEffectFilter와 각 배치 모드 병용을 확인해야 한다. CompactStatusEffects와 MyLittleUI의 배치 기능끼리 생기는 충돌은 별도다. 업로더 점검·사이트 게시는 수행하지 않았다.

## 상태 효과 순서 편집 제거와 HUD 비교 — 2026-09-27

- 사용자 요청에 따라 상태 효과 순서 편집 패널·버튼·HUD Prefix·설정·순서 저장 코드와 전용 검사를 제거했다. 기존 까마귀 안내, 도감 항목 정렬/접기, 공통 도감 저장 기능을 유지했다. 이전 상태 효과 JSON과 사용자 cfg는 삭제하거나 변경하지 않았다.
- [상태 효과 HUD 비교](docs/status-effect-hud-comparison.md)에 제공된 세 원본 DLL의 패치 대상·순서, config 기본값, 템플릿/복원, 필터 의미와 한계, 현재 rtd 파일 설정, 새 구현 제안을 기록했다. 새로운 1열 HUD와 config 필터는 아직 구현하지 않았다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: **경고 0 / 오류 0**. 자동 배포 후 빌드 DLL과 Steam 게임 플러그인 DLL의 SHA-256을 별도로 확인했으며 모두 `08667EBB386D8FD2177BAF10E574AA6992D7803D0954D1ABB52DB8FF242EF7FB`다.
- 원본 API 참조 **601곳, 오류 0개**. 원본 **Mono 6.13.0**에서 기존 도감 접근 **14개**, 공통 저장 **52개** 검사를 통과했다. Mono 실행 근거: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-958daf73b31a4e458c5063ea71f52f53`.
- 버전은 1.0.0을 유지했다. 기존 Release DLL과 ZIP의 해시가 위 Release 절의 값과 같음을 확인했으며 이번 작업에서 Release/ZIP을 생성하지 않았다. 실제 게임 UI·Harmony detour·세 참고 모드의 동시 실행은 검증하지 않았다.

## 선택적 1열 상태 효과 HUD와 필터 — 2026-09-27

- 권장안 승인에 따라 `Status effects`의 다섯 클라이언트 설정을 추가했다. Single column=false, Position=(-180,-290), Scale=1(0.5~2), Row spacing=55(20~150), Hidden effects=빈 문자열이다. 기존 다른 HUD 모드 설정과 사용자 저장 JSON은 변경하지 않았다.
- `StatusEffectHud`의 별도 Harmony owner로 private `Hud.UpdateStatusEffects(List<StatusEffect>)`와 `Hud.OnDestroy`를 패치한다. private HUD 행은 Harmony 필드 주입을 사용한다. 기존 도감 패치와 초기화/실행 오류 처리를 분리했으며, 게임 원본 public API 외의 직접 호출이나 publicize를 추가하지 않았다.
- 필터는 설정 변경 때 불변 규칙을 교체하고, 내부 효과 이름 또는 원문 m_name을 정확히 비교한다. 쉼표·세미콜론·줄바꿈과 앞뒤 공백을 처리하며 런타임 내부 이름의 끝 `(Clone)`을 정규화한다. 내부 이름은 약한 참조 캐시에 보관하고 원문 m_name은 매번 읽는다. 제외할 항목이 있을 때만 새 목록을 생성하며, 입력 목록·효과 객체·남은 순서와 중복을 보존한다. 외부 필터가 제외한 효과를 다시 수집하지 않는다.
- `StatusEffectLayout`은 원본 root의 자식 컨테이너에 실제 HUD 행만 배치한다. 고정 우상단 기준에서 행은 왼쪽으로 펼쳐지고, 아이콘 왼쪽/이름·시간 오른쪽으로 구성한다. 원본 root·템플릿·WeightSE를 수정하지 않으며, 원본 숨김/투명도 계층을 상속한다. 배치를 끄면 원래 부모·sibling·RectTransform·수정한 TMP 스타일을 복원하고 게임이 갱신한 내용·색·아이콘·타이머 활성 여부는 유지한다. 맞지 않는 계층이나 소유권 변경은 경고하고 배치를 중단한다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: **경고 0 / 오류 0**. 빌드와 자동 배포 DLL의 SHA-256을 별도로 확인했으며 모두 `4081E2FE179A2B7038E3175819C9E44FB4E0EDA7770E18BCDB39786B2CB41AC9`다.
- `tests/OriginalApiChecks`: 원본 게임/Unity 직접 참조 **773곳, 오류 0개**. HUD private 대상/필드 시그니처, Prefix ref 인수·Priority.Last·StatusEffectFilter 뒤 순서 지정, Postfix 필드 주입을 검사했다. 참고 DLL은 런타임 의존성에 추가되지 않았다.
- 원본 **Mono 6.13.0**에서 기존 저장 **52개**와 접근/필터 **38개**(기존 도감 접근 14 + 신규 필터 24) 검사를 통과했다. 실제 제품 Prefix를 호출해 구분자·대소문자·Clone 접미사·변하는 m_name·불필요한 사본 미생성·원본 보존·중복/null·전체 제외·외부 필터 보존·비활성/오류 상태를 확인했다. 생성자 없는 원본 StatusEffect에 캐시값을 주입했으며 Unity name getter는 실행하지 않았다. 사용자 cfg를 만들거나 변경하지 않았다.
- 실행 근거: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-664868da46304f2d93a076db46fd8dab\stdout.txt`. **실제 Harmony detour 설치, 원본 HUD 메서드 실행, Unity 렌더링/복원·애니메이션·타 UI 모드와의 실행은 미검증**이다.
- README와 비교 문서에 사용법/최종 구현을 반영했다. 모드 버전은 1.0.0이다. 기존 Release DLL·ZIP 해시가 이전 값과 동일함을 확인했으며, 새 기능의 Release/ZIP 생성이나 업로더 작업은 수행하지 않았다.

## 사용자 화면 기준 상태 효과 기본값 변경 — 2026-09-27

- 사용자 스크린샷에 맞춰 Single column=true, Position=(-40,-250), Scale=0.8, Row spacing=40으로 기본값을 변경했다. Hidden effects는 빈 문자열을 유지했다. 잘못된 숫자 입력에 사용하는 복구값과 현재 설정 설명도 같은 값으로 맞췄다. 기존 사용자 cfg 값은 덮어쓰지 않았다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: **경고 0 / 오류 0**. 빌드 DLL과 게임 폴더 DLL의 SHA-256은 모두 `89445B775D00821437085C809766E304EEF7DFC3FE0FF8C69C94753A3436C1B8`로 일치한다.
- 이번 변경은 기본 수치와 설명에 한정된다. 필터·저장·배치 알고리즘과 비공개 접근 경로는 변경하지 않아 이전 자동 검사는 반복하지 않았다. 실제 게임 실행은 수행하지 않았다. 버전 1.0.0과 기존 Release DLL·ZIP은 유지했다.

## 1.0.1 Release 빌드 — 2026-09-27

- 사용자 요청에 따라 플러그인 상수·프로젝트·manifest 버전을 1.0.1로 올리고, 1열 HUD·기본 배치 값·HUD 전용 필터·배치 복원을 영어 changelog에 기록했다. manifest 설명에도 상태 효과 HUD를 반영했다.
- `dotnet build ReferToCompendium.sln -c Release`: **경고 0 / 오류 0**. 기존 패키징 타깃으로 `Thunderstore/ReferToCompendium_v1.0.1.zip`을 생성했다.
- DLL과 ZIP 내부 manifest 버전 모두 1.0.1, BepInEx 의존성 5.4.2351을 확인했다. ZIP의 다섯 항목과 DLL·README·CHANGELOG·icon 입력 해시 일치를 완성된 ZIP에서 다시 검사했다. 패키징 타깃의 manifest 입력 검사와 전체 항목 해시 검사도 통과했다.
- 최종 Release DLL의 원본 게임/Unity API 참조 **773곳, 오류 0개**. 이번 Release에서 게임·Unity 렌더링은 실행하지 않았다.
- Release DLL SHA-256: `9B60DFB2995CBC0E69F82EA013966E94549BB53ED663BCF730F2DB2ABF66492D`.
- Release ZIP SHA-256: `E284525A71409D029FC04564DDB8CAA126570B033A1333B357AD1555A78991F8`.
- 사용자가 작업 범위를 Release 빌드로 좁혀 Git 초기화·커밋·push는 수행하지 않았다. 업로더 점검·게시 확인도 수행하지 않았다.

## 도감 항목 드래그 정렬 — 2026-10-05

- 목록 편집 모드에서 제목을 왼쪽 버튼으로 잡아 드래그하면 떠 있는 제목과 금색 삽입선을 표시한다. 목록 가장자리에서는 정지한 포인터에서도 자동 스크롤하며, 유효한 위치에 놓을 때만 한 번 이동·저장한다. 일반 목록과 펼친 접기 그룹은 각각의 그룹 안에서 이동한다. 기존 화살표·접기/복원·선택 리스너는 유지했다.
- 목록 밖 드롭, 도감 닫기, 포커스 상실, 새로고침·재배치에는 저장 없이 취소하고 표시/행 이벤트 상태를 정리한다. 드래그 클릭을 취소해 원래 항목 클릭이 함께 실행되지 않도록 하고, 자식 버튼의 드래그는 ScrollRect로 전달한다. 편집 모드 밖에는 행 드래그 처리를 비활성화한다.
- 드래그 도중에는 기존 선택의 지연 SnapTo만 건너뛴다. 원본 private 메서드를 Harmony로 패치하며 직접 호출/publicize는 없다. 포인터 ID·왼쪽 버튼을 확인하고, 이벤트 객체가 다른 마우스 버튼에 재사용되는 입력 모듈을 고려해 위치·카메라를 별도로 보관한다.
- 기존 LayoutState.Move의 다중 칸 이동을 사용하며 저장 파일/형식은 유지했다. 없는 모드 항목과 다른 접기 그룹의 위치 보존, 장거리 양방향 이동, 같은 자리 무변경, 저장 후 복원 검사를 추가했다. 영어 README와 게임 내 한국어/영어 도움말에 사용법을 반영했다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: 경고 0 / 오류 0. 최종 DLL과 Steam `BepInEx/plugins/ReferToCompendium.dll`의 SHA-256은 모두 `ADF5913A4CB15E819C9B52B905A8B6C84110DB832076E2ADD132F1E7FAD56AE7`로 일치한다.
- 원본 게임/Unity API 참조 975곳과 private SnapTo 시그니처 검사: 오류 0개. 실제 컴파일 참조 assembly_valheim.dll SHA-256이 원본 client build 25527674 스냅샷과 일치함을 확인했다.
- 원본 Mono 6.13.0에서 저장/정렬 66개, 기존 접근/필터 38개 검사 통과. 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-516925b482f048fc925cef00272cdbea\stdout.txt`.
- 실제 Unity 입력·렌더링·Harmony detour 설치·게임 실행은 검증하지 않았다. 게임에서 긴 목록 양방향 드래그/가장자리 스크롤, 드래그 후 원치 않는 항목 클릭 없음, 밖에 놓기/Alt-Tab/도감 닫기 취소, 접기 그룹/배율/가상 포인터와 다른 UI 모드 병용을 확인해야 한다.
- 버전 1.0.1 유지. Release DLL과 기존 ZIP의 해시가 이전 Release 절과 동일하며 이번 작업에서 Release/ZIP·업로더 작업은 수행하지 않았다.

## 제목 영역에서 상시 드래그 정렬 — 2026-10-05

- 사용자 보고의 원인은 제목 드래그 자체가 아니라 드롭 영역이었다. 원본 main scene의 TextsDialog.m_leftScrollRect가 이름과 달리 오른쪽 본문 ScrollRect를 참조했다. 기존 코드가 이 필드로 드롭 영역과 자동 스크롤을 판단해 오른쪽으로 끌어야 인식되었다.
- CaptureRows에서 m_listRoot의 조상 ScrollRect 중 content가 실제 m_listRoot와 같은 것을 찾고, 없으면 해당 도감의 자식 ScrollRect에서 같은 조건으로 찾는다. 드롭 범위·자동 스크롤·접기 버튼에서 시작한 스크롤을 모두 이 목록 ScrollRect로 연결했다. 제목은 클릭으로 기존 페이지를 열고 누른 채 위아래로 끌면 순서를 바꾼다.
- 편집/완료 상태와 버튼, 위아래 삼각형 및 관련 코드를 제거했다. 접기/복원 버튼은 항상 표시하고 제목 공간을 넓혔다. 상단은 한국어 `제목을 누른 채 위아래로 끌기 / 순서가 자동 저장됩니다.` 또는 영어 안내와 초기화 버튼으로 구성한다. 유효한 드롭·접기·복원·초기화 시 저장하며 저장 파일/형식은 유지했다. README와 접기 그룹 도움말도 변경했다.
- 원본 리소스 근거: `C:\Users\blizz\.codex\references\valheim\snapshots\client-b25527674-windows-x64-20260925T211211Z\original\valheim_Data\StreamingAssets\SoftRef\Bundles\17245031`, SHA-256 `C717EBF366D304F0729CEEA999BF4BE85AB6CF606FDE0907E62710E6EC4C9E65`. SoftRef/manifest의 assetID `73b271bd5debc4df6928e4ff133538e5`는 `Assets/Scenes/main.unity`다. UnityPy 읽기 전용 typetree 확인: serialized file `CAB-6a829f7b850fac02b06c168e1024a4e2`, TextsDialog pathID 7365, m_listRoot 5363, m_leftScrollRect 7352. 7352의 content는 오른쪽 본문 5372이고 scrollbar는 m_rightScrollbar 7483이다. 실제 제목 목록 ScrollRect 7314는 `Texts/Texts_frame/TextList/SkillList`에 있으며 content 5363, scrollbar 7293, viewport=null이다. 따라서 자체 RectTransform 4994가 제목 목록의 viewport가 된다. 원본 행 SkillElement 1752는 루트 Button 7395를 사용하며 제목 TMP 5902는 raycastTarget=false다. 새 클릭 덮개나 기존 버튼 리스너 교체 없이 수정했다.
- `dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true -p:TreatWarningsAsErrors=true`: 경고 0 / 오류 0. 최종 DLL과 Steam `BepInEx/plugins/ReferToCompendium.dll`의 SHA-256 모두 `B423D0A5F474560780E9912233AB0C9BCFA92EB99B40628AB3E05CEDAFCD8101`로 일치한다.
- 최종 원본 게임/Unity API 참조 956곳 검사: 오류 0개. 원본 Mono 6.13.0에서 저장/정렬 66개와 기존 접근/필터 38개 검사 통과. 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-2d9bff2b7b07440e99a170f3c7e12b4c\stdout.txt`.
- 실제 게임 실행·Unity 렌더링·입력/Harmony detour는 검증하지 않았다. 원본 리소스의 연결 관계와 코드/API/저장 검사를 확인한 것이며, 화면에서의 직접 수직 드래그·스크롤·안내 문구 크기는 게임 내 확인이 필요하다. 버전 1.0.1과 기존 Release DLL/ZIP을 유지했다.
- 위 Mono 검사 후 안내문만 짧게 조정하고 최소 높이 64 UI 단위, 글자 크기 14~18로 맞췄다. 원본 목록 폭 약 305/행 간격 36을 기준으로 기존 45 높이에서 생길 수 있는 안내문 겹침을 줄였다. 최종 Debug 재빌드·자동 배포·원본 API 검사를 다시 통과했으며, 위 최종 DLL 해시는 이 문구 조정까지 포함한다. 원본 main/start 두 scene을 확인했으며 main에 위 TextsDialog 1개, start에는 없었다. 다른 무관한 번들 전체를 검사하지 않았다.

## 안내문·초기화 버튼을 목록 위 여백으로 이동 — 2026-10-05

- 사용자 표시 영역에 맞춰 툴바를 스크롤 콘텐츠 m_listRoot에서 고정 TextList 패널로 옮겼다. 안내문 오른쪽에 초기화 버튼을 두며 목록 스크롤에 함께 움직이거나 마스크로 잘리지 않는다. 행 시작 위치를 0으로 되돌려 기존 안내문 자리의 64 UI 단위 여백을 제거했다. README에 고정 위치를 설명했다.
- 앞 절의 원본 main scene 번들을 재사용해 배치 확인: Texts_frame RectTransform5382 크기1200×800, TextList Rect4950 크기315.41×734, 상단 anchored Y=-51, Mask/RectMask2D 없음. 자식 SkillList Rect4994에는 ScrollRect7314 및 RectMask2D7282가 있다. 새 툴바는 SkillList의 형제로 TextList 상단에 배치하며 anchors=(0,1)..(1,1), pivot=(0.5,0), anchoredPosition=(0,8), sizeDelta=(-8,32)다. 원본 프레임 상단으로부터11~43 영역에 놓이고 목록 앞에는8 단위 간격이 남는다.
- 안내문은 12~16 크기로 자동 맞춤하며 초기화 버튼 높이는 최대28로 제한했다. 드래그·접기·저장 동작 및 저장 형식은 변경하지 않았다.
- Debug 빌드(`DeployToGame=true`, `TreatWarningsAsErrors=true`): 경고0/오류0. 원본 게임/Unity API 참조963곳 검사 오류0개. 빌드 DLL과 Steam plugins DLL의 SHA-256은 모두 `96B85E827A7E3FC1C1642A452C161D2639CD3DF3BBF9341980D40250C5C71983`로 일치한다.
- 배치 변경에 한정되어 기존 저장/Mono 검사는 반복하지 않았다. 원본 리소스와 코드/API 배치를 확인했으며 실제 게임에서의 문구 렌더링·버튼 클릭·타 UI 모드 병용은 실행하지 않았다. 버전1.0.1 및 기존 Release DLL/ZIP 해시는 유지되었다.

## 다섯 UI 문구의 YAML 번역 — 2026-10-05

- 까마귀 기본 안내, 드래그 안내, 초기화, 접힌 그룹 제목, 그룹 도움말만 `rtc_*` 키로 분리했다. 기존 영어·한국어 문구와 줄바꿈을 `translations/English.yml`, `translations/Korean.yml`에 보존하고 DLL 리소스로 포함했다. 설정 항목 이름/설명과 다른 모드의 도감 제목은 번역 대상에 포함하지 않았다.
- 내장 영어 → 내장 선택 언어 → 외부 선택 언어 순으로 적용한다. 외부 경로는 `BepInEx/config/ReferToCompendium/translations/<Language>.yml`이다. 누락/빈 값은 기본 번역을 유지하며 중복 키·잘못된 YAML·중첩 값·복수 문서는 해당 파일 전체를 건너뛰고 경고한다. 알 수 없는 키는 무시한다. 사용자 지정 Raven hint/Text가 번역보다 우선한다.
- 공개 `Localization.OnLanguageChange`에 구독하고 다음 LateUpdate에서 다시 읽는다. 매 프레임 파일 접근이나 전역 게임 번역 사전 수정은 없다. 모드가 만든 표시만 갱신하고 도감 ID/순서/다른 모드 제목을 다시 계산하지 않는다. 진행 중 드래그는 저장 없이 취소하며 선택된 그룹 도움말은 새 스크롤 코루틴 없이 갱신한다. 종료 시 구독을 해제한다.
- YamlDotNet 16.3.0(net47)을 ILRepack.Lib.MSBuild.Task 2.0.44.2로 병합·internalize했다. 매 빌드는 원본 obj DLL을 입력으로 쓰고 병합 후 배포/패키징한다. 게임·BepInEx·Harmony 참조 DLL은 병합하지 않는다. YamlDotNet MIT 고지는 DLL 리소스와 향후 패키지 입력에 포함했다. LocalizationManager는 설계만 참고했으며 소스를 복사하거나 런타임 의존성으로 추가하지 않았다.
- Debug 빌드와 증분 재빌드(`DeployToGame=true`, `TreatWarningsAsErrors=true`): 경고 0 / 오류 0. 최종 DLL과 Steam plugins DLL SHA-256이 `3EFA1E1A46C0C207A382E97086C9F43C6E6253F9C2BE0EF5E31F4AC71DC80AF0`으로 일치한다. 원본 API 참조 980곳과 번역 리소스/고지/internalize/외부 YAML AssemblyRef 부재 검사를 통과했다.
- 최종 병합 DLL을 원본 client build 25527674의 Mono 6.13.0에서 검사했다. 번역 185개, 기존 저장/정렬 66개, 기존 접근/HUD 필터 38개 통과. 테스트 실행 파일만 격리 폴더에 복사하여 별도 YamlDotNet DLL 없이 실제 제품 파서를 실행했다. 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-4fe01d8c008341c8a81a4d1e9b94603c\stdout.txt`.
- 패키징은 `-ValidateOnly`로 6개 입력과 아이콘을 확인했으며 ZIP은 생성하지 않았다. 버전 1.0.1 유지. 이전 Release DLL/ZIP의 SHA-256이 1.0.1 Release 절과 동일함을 확인했다. README에 외부 번역 추가/부분 수정/재읽기 방법을 기록했다.
- 실제 게임 UI 렌더링, 열린 도감의 언어 전환, Harmony detour 설치 및 게임 세션은 실행하지 않았다. 자동 검사는 원본 메타데이터와 격리 Mono의 관리 코드 동작을 검증한 범위다.

## 번역 파일 재귀 검색과 패키지 동봉 — 2026-10-06

- `Paths.PluginPath`와 `Paths.ConfigPath` 및 모든 일반 하위 디렉터리에서 `ReferToCompendium.<Language>.yml`을 정확한 이름으로 찾는다(파일명 대소문자는 무시). 다른 모드/언어 이름, `.example.yml`, `.yml.bak`는 읽지 않는다. BepInEx의 다른 영역을 검색하지 않으며 재귀 검색에서 junction/symlink를 건너뛴다. 접근 실패는 경고하고 탐색 가능한 다른 디렉터리를 계속 처리한다.
- 내장 영어 → 내장 선택 언어 → plugins의 선택 파일 → config의 선택 파일 → 기존 `config/ReferToCompendium/translations/<Language>.yml` 순으로 키별 적용한다. 빈 값과 누락 키는 직전 레이어를 보존하며 잘못된 파일은 전체 레이어를 건너뛴다. 같은 검색 루트에 중복이 있으면 전체 경로의 Ordinal 정렬 첫 파일만 선택하고 선택/무시 경로를 경고한다. 해당 파일이 잘못돼도 다른 중복 파일로 임의 변경하지 않는다.
- 기존 언어 변경 이벤트/초기 LateUpdate에서만 검색한다. 매 프레임 검색이나 watcher는 추가하지 않았다. 언어 변경/파일 제거 후 다시 읽을 때 이전 외부 번역이 남지 않는다. UI 다섯 문구, 설정 키, 도감 저장 형식과 모드 버전 1.0.1을 유지했다.
- ZIP의 명시적 입력에 `ReferToCompendium.English.yml`을 추가했다. 패키징 시 `translations/English.yml`의 바이트를 그대로 복제하며 별도 원본 파일을 관리하지 않는다. 영어 선택 시 사용되는 정상 번역 파일이자 다른 언어의 복제 원본이다. README에 위치/파일명/우선순위/개인 번역을 config에 두는 방법을 반영했다.
- Debug 빌드(`DeployToGame=true`, `TreatWarningsAsErrors=true`) 경고 0 / 오류 0. 최종 DLL과 Steam plugins DLL SHA-256은 모두 `F6753AC608E7A2D16FF79EC060B5040073336ACAC77369E17BC61E52E28D3847`이다. 원본 API/리소스/병합 참조 검사 980곳에서 실패가 없다.
- 원본 client build 25527674의 Mono 6.13.0에서 실제 병합 DLL의 번역 검사 267개, 기존 저장 66개, 접근/HUD 필터 38개 통과. 검색 위치, 파일명 제외, 키별 우선순위, 결정적인 중복 선택, 오류 레이어 보존, 파일 삭제/언어 전환 재검색을 확인했다. 선택적 디렉터리 루프 링크 검사는 Windows 권한 오류 1314로 링크를 만들지 못해 건너뛰었고, ACL 변경/실제 접근 거부 디렉터리 검사는 하지 않았다. 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-2b8e5e01591a405cafc2bd62e9ba063c\stdout.txt`.
- PowerShell 구문 검사와 패키징 `-ValidateOnly`로 영어 YML을 포함한 7개 입력을 확인했다. Release 빌드/ZIP 생성/버전 변경/업로더 작업은 수행하지 않았다. 실제 Unity UI와 모드 관리자를 통한 패키지 설치는 실행하지 않았다.

## 1.0.2 Release 빌드 — 2026-10-06

- 사용자 요청에 따라 플러그인 상수·프로젝트·manifest를 1.0.2로 맞추고, 제목 드래그/자동 저장/목록 상단 안내와 YAML 번역/검색/동봉 변경을 영어 changelog에 기록했다.
- `dotnet build ReferToCompendium.sln -c Release`: 경고 0 / 오류 0. 최종 병합 DLL과 `Thunderstore/ReferToCompendium_v1.0.2.zip`을 생성했다. ZIP은 manifest/README/CHANGELOG/LICENSE/ThirdPartyNotices/icon/모드 DLL/ReferToCompendium.English.yml의 8개 항목이며, 빌드 타깃이 모든 입력과 ZIP의 바이트 해시 일치를 검사했다.
- 완성 ZIP에서 버전 1.0.2, BepInEx 의존성 5.4.2351, DLL·영어 YML·changelog·LICENSE 해시 일치를 별도로 재확인했다. 원격 저장소의 기존 GPL-3.0 LICENSE를 보존하고 패키지에도 포함했다. 빌드 문서의 패키지 목록을 갱신했다.
- Release DLL SHA-256: `4AE86D2F057FA8A31DFE4209778350AC96C2F281F67AA6E53E9A6A6C8386ACE4`.
- Release ZIP SHA-256: `2080603064D7236F282F558A707344EE89025F9CDC03077AD3F4CBC78A6C1F38`.
- 최종 Release DLL 원본 API/리소스/병합 참조 검사 980곳: 실패 0. 원본 client build 25527674의 Mono 6.13.0에서 번역 267개, 저장 66개, 접근/HUD 필터 38개 검사 통과. 디렉터리 링크 검사는 권한 오류 1314로 건너뛰었다. 출력: `C:\Users\blizz\AppData\Local\Temp\ReferToCompendium-Mono-a34eaba1bb1b482ba02be7bf3ea7fcf7\stdout.txt`.
- 실제 게임 UI·Harmony detour·네트워크 세션은 실행하지 않았다. 일반 Release 빌드 요청 범위에서 업로더 점검·게시 확인은 수행하지 않았다.
- 로컬에 Git 저장소가 없어 초기화하고 지정한 `https://github.com/sighsorry1029/ReferToCompendium`의 `main` 초기 커밋 `f16f7e3`을 이어받았다. 기존 소스/문서를 덮어쓰지 않았으며, 로컬 게임 로그 진단 폴더는 Git 추적에서 제외했다.
