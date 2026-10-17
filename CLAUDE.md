# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Language

**Always respond to the user in Korean (한글), without exception.**

## 프로젝트 개요

Toy Box: 아이와 함께 하는 미니게임 모음 앱. 메인 메뉴에서 게임을 고르면 그 게임 씬이 실행된다. **Godot 4.7.2 (.NET/C#)**, `net8.0`. 대상은 PC(Windows)와 안드로이드, 가로 1280x720 기준(`canvas_items` + `expand`, 화면비에 따라 가로/세로가 늘어나므로 좌표는 항상 `GetViewportRect().Size` 기준으로 잡는다).
구조와 연출 방식은 `C:\Work\Projects\color-blast`(같은 사용자의 이전 프로젝트)를 참고해 만들었다.

- 기획서: `docs/GDD.md`(공통 사항, 게임 목록, 확정된 결정) + `docs/games/NN-<id>.md`(게임별). 게임을 추가하면 게임별 파일을 새로 만들고 GDD.md 목록에 추가한다.
- 구현된 게임: 6개 전부(풍선 터뜨리기, 과일 받기, 숫자 / 색깔 퀴즈, 동물 짝 맞추기, 미로 탈출, 공룡 점프). 새 게임을 더하면 `GameCatalog`에 넣는다(`ScenePath`가 null이면 메뉴에 "준비 중").
- 아이용 원칙: 감점, 실패 연출, 붉은 경고를 쓰지 않는다. 오답은 다시 고를 수 있게 하고, 시간이 갈수록 화면이 신나지는 방향으로 만든다.

## 명령어

Godot 실행 파일은 PATH에 있다(`Godot_v4.7.2-stable_mono_win64_console.exe`, winget 설치).

```bash
dotnet build ToyBox.csproj                              # 게임 어셈블리 빌드
dotnet test tests/ToyBox.Tests                          # Core 단위 테스트 (엔진 불필요)
dotnet test tests/ToyBox.Tests --filter "FullyQualifiedName~LeaderboardTests"   # 테스트 클래스 하나만
Godot_v4.7.2-stable_mono_win64_console.exe --path . -e                      # 에디터
Godot_v4.7.2-stable_mono_win64_console.exe --path .                         # 실행
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --import     # 리소스 추가 후 .godot 캐시 갱신
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --quit-after 600 -- --game=balloon --autoplay   # 런타임 오류 스모크 테스트 (과일: --game=fruit, 퀴즈·짝 맞추기·미로·공룡: --game=quiz|memory|maze|dino --quit-after 8000)
# 화면 확인: 프레임을 PNG로 저장(.wav도 함께 저장됨). 출력 경로는 프로젝트 밖(스크래치)으로
Godot_v4.7.2-stable_mono_win64_console.exe --path . --write-movie <dir>/f.png --fixed-fps 10 --quit-after 400 -- --game=balloon --autoplay
```

개발용 인자(`--` 뒤): `--game=<id>`(메뉴를 건너뛰고 바로 그 게임), `--autoplay`(게임이 스스로 플레이하고 이름 입력도 자동, 카운트다운 단축), `--open=records` / `--open=records:<게임 id>` / `--open=settings`(메인 메뉴에서 팝업을 연 채 시작. `records:quiz`는 퀴즈 탭).
자동 플레이로 랭킹과 최근 이름이 쌓인다(`%APPDATA%/Godot/app_userdata/Toy Box/records.json`, `settings.cfg`). **이 파일에는 가족이 실제로 플레이한 기록이 들어 있으므로 지우지 말고**, 테스트 전에 복사해 두었다가 끝나면 되돌린다.

## 구조

- **`scripts/Core/`**: Godot에 의존하지 않는 순수 C#(`using Godot` 금지). 테스트 프로젝트가 이 폴더 소스를 직접 링크해 엔진 없이 테스트한다. 규칙·판정·수치 로직은 여기에 둔다.
  - `Leaderboard`/`RecordBook`: 게임별 Top 10과 JSON 직렬화. 정렬은 `RankOrder`(점수 높은 순 / 시간 짧은 순 / 점수 후 시간: 미로는 `Score`에 별 수 / 시간 후 점수 낮은 순: 짝 맞추기는 `Score`에 뒤집은 횟수). 동점이면 먼저 세운 기록이 위. 빈 표는 저장하지 않는다.
  - `BoardKey`/`GameVariant`: **난이도별 랭킹**. 설정에 따라 결과를 비교할 수 없는 게임은 `GameInfo`에 변형 목록(쉬운 것부터, 예: `QuizRules.Variants` = easy/normal/hard → 쉬움/보통/어려움)을 주면 변형마다 Top 10을 따로 둔다. `records.json` 키는 `"<게임 id>:<변형 id>"`(`quiz:hard`), 변형 없는 게임은 그대로 `"balloon"`. `App.I.Board(id, variantId)`, `SubmitRecord(id, variantId, entry)`, `ResultSpec.VariantId`, `RankingList.Create(game, variantId)`로 쓰고, 메뉴 카드는 기록이 있는 가장 어려운 변형(`BoardKey.Featured`), 기록 화면은 변형 탭을 자동으로 보여 준다. 짝 맞추기(`MemoryRules.Variants`)와 미로(`MazeRules.Variants`)도 쓴다.
  - `RecentNames`: 결과 화면 이름 버튼에 쓰는 최근 이름 목록(최대 3개, 최신순, 중복 없음). 예전 설정은 `last_name`과 기록의 이름으로 채운다.
  - `BalloonRules`, `FruitRules`, `QuizRules`(출제 `QuizDeck`: 연속 같은 정답 없음, 점수 `QuizRound`: 한 번에 맞힌 문제 수와 카드를 볼 수 있던 시간): 게임별 난이도 표(기획서 표 그대로)와 판정·점수. 속도는 "화면 높이/초" 단위.
  - `MemoryRules`(카드 수·격자·무작위 배치 `Deal`)와 `MemoryBoard`(짝 맞추기 상태: 뒤집기 결과 First/Match/Mismatch, 실패 쌍은 `Hide()` 전까지 입력 잠금, 뒤집은 횟수, 플레이 시간).
  - `MazeRules`(크기·난이도별 미리 고른 시드 5개, 기록 표시), `Maze`(벽, 최단 경로 `ShortestPath`, 막다른 길), `MazeGenerator`(자체 난수로 결정적인 생성: 길 파기 → 막다른 길 잇기 → 별 3개를 최단 경로 밖에 배치), `MazeRun`(이동·벽 부딪힘·별 줍기·첫 이동부터 재는 시간). 시드를 바꾸면 `MazeRulesTests`(풀 수 있음, 막다른 길 수, 별 위치, 첫 미로 그림 고정)도 함께 맞춘다.
  - `DinoRules`(속도 단계 표·점프 포물선·충돌 상자·장애물 간격. 거리는 "화면 높이" 단위. 최소 간격 `MinGap`은 가장 늦게 넘은 아이도 착지 후 반응 시간이 남게 하고, `DinoRulesTests`가 모든 속도·장애물 조합으로 확인한다)와 `DinoJump`(한 번 점프, 공중 점프 없음, 착지 직전 입력 버퍼).
  - `StartSpeed`/`RoundClock`: 꾸미기의 "속도" 옵션 배율과 남은 시간 초 계산(시간 제한 게임 공용).
  - `SfxSynth`: 효과음과 BGM을 코드로 합성한다(오실레이터, 스윕, 노이즈, 감쇠; BGM은 `"C5:1 E5:0.5 ..."` 음표 문자열). 사운드 파일이 없다.
- **`scripts/Common/`**: 모든 게임이 공유하는 Godot 코드.
  - `App`(autoload): 설정(`user://settings.cfg`, 최근 이름 `RecentNames`/`RememberName`, 창 크기 `ApplyResolution`: 1280×720·1600×900·1920×1080·전체 화면, PC만, 헤드리스·`--write-movie`에서는 저장만 하고 적용 안 함), 기록(`user://records.json`), UI 테마 적용, 페이드 씬 전환(`App.I.GoTo`), 개발용 인자 처리.
  - `Audio`(autoload): 합성음을 `AudioStreamWav`로 바꿔 16개 플레이어로 돌려 재생. BGM/SFX 버스를 런타임에 만들어 설정 볼륨을 적용. `PlayMusic`은 같은 곡이면 아무것도 안 하므로, 판을 시작할 때 `ResetMusic()`으로 결과 화면의 덕킹과 빨라진 템포를 되돌린다.
  - `GameCatalog`: 게임 목록(id, 제목, 아이콘, 씬 경로, 랭킹 정렬, 표시 형식). **`ScenePath`가 null이면 메뉴에 "준비 중"**. 새 게임은 씬을 만들고 여기에 경로를 넣으면 메뉴·기록 화면에 연결된다.
  - `Ui/`: `UiTheme`(색, 시스템 한글 굵은 글꼴, 테마), `Ui`(버튼·라벨·패널 팩토리, PopIn/Punch/Wiggle 트윈), `ResultPanel`(공통 결과 화면. `ResultSpec`으로 값 글자 크기, 값 아래 보조 줄(`Detail`, 예: 뒤집은 횟수), 별 줄(`StarsTotal`/`StarsEarned`, 옆 글자 `StarsLabel`), 변형 랭킹(`VariantId`)을 정한다: count-up → 최고 기록 축하 → Top 10이면 이름 입력 → 랭킹 강조 → 버튼. 이름은 최근 이름 버튼을 한 번 누르거나 "새 이름"으로 키보드 입력. 한글 IME의 조합 중인 글자는 `LineEdit.Text`에 없으므로 제출 전에 `ApplyIme()`로 확정한다), `RankingList`, `Countdown`(3·2·1·시작!, 일시정지 중에는 멈춤), `TimerBar`(남은 시간 막대: `Create(전체 초)` → 매 프레임 `Remaining` 설정, 판 시작 때 `Reset()`, 마지막 5초 주황색. 시간 제한 게임 공용), `ChoiceRow`(꾸미기 선택 칩. `CreateMulti`는 여러 개 켜기, 마지막 하나는 못 끔. 공용 속도 칩은 `Choice.Speeds`), `RoundHud`(시간 제한 게임의 판 HUD: 점수·그만하기·남은 시간+`TimerBar`·단계 문구·"끝!"), `QuitConfirm`("정말 그만할까요?", 열려 있는 동안 일시정지).
  - `Fx/`: `Burst`, `FloatingText`(color-blast에서 가져옴), `SpriteBurst`(별 튀기기), `Shards`(고무 조각처럼 돌며 떨어지는 조각), `Confetti`, `Ripple`(빈 곳 탭 표시), `ScreenShake`.
  - `Scenery`(배경: 하늘/공원/우주/바다, 공원만 이미지, 나머지는 코드로 그림. `CloudDrift`로 구름 방향·속도를 바꿀 수 있다: 공룡 점프는 왼쪽으로 흘려 먼 패럴랙스 층으로 씀), `Buddy`(응원 캐릭터: 통통 튀기, 말풍선. 말풍선은 화면 밖으로 나가지 않게 왼쪽으로 당겨진다).
- **`scripts/Games/<Game>/`**: 게임별 코드. 씬(`scenes/games/*.tscn`)에는 스크립트가 붙은 루트 노드 하나만 있고 나머지 노드는 `_Ready`에서 코드로 만든다(color-blast와 같은 방식). UI는 CanvasLayer에, 게임 오브젝트는 `World` Node2D 아래에 둔다(흔들림 대상).
- 입력: `emulate_touch_from_mouse`가 켜져 있어 마우스 클릭도 `InputEventScreenTouch`로 들어온다. 게임 입력은 터치 이벤트만 처리한다(마우스 이벤트까지 처리하면 두 번 들어옴).
- 한글 글꼴은 `SystemFont`(Windows 맑은 고딕, 안드로이드 Noto Sans CJK)를 쓴다. `ThemeDB.FallbackFont`도 이것으로 바꿔 `_Draw()` 텍스트에도 한글이 나온다.

## 리소스

- `art-source/`: 받은 원본 스프라이트 시트(`.gdignore`로 Godot import 제외). 시트 한 장에 여러 그림이 있다.
- `assets/sprites/<분류>/<이름>.png`: 시트를 투명 영역 기준으로 잘라 60% 크기로 줄인 개별 이미지. `assets/backgrounds/sky.png`: 하늘 배경(1280x720).
- 시트를 다시 자를 때는 System.Drawing으로 연결 영역을 나누는 방식으로 했다(Python 없음). 동물 시트 첫 줄(사자·코끼리·펭귄)은 카드가 붙어 있어 열 경계로 나눴다.

## 검토

- 연출/완성도 최종 검토는 `/polish-review` 스킬(`.claude/skills/polish-review/`)로 한다. 검토만 하고, 사용자가 고른 항목만 구현한다.
- 필요한 리소스 목록은 `docs/resources.md`. 리소스를 받으면 체크하고 관련 개선 항목을 진행한다.
- 개선 사항은 `docs/improvements/`에 범위별로 모은다(`common.md`, `main-menu.md`, 게임별 `NN-<id>.md`). 검토 결과는 여기에 ID(C-/M-/B-…)와 우선순위(P1~P3)를 붙여 추가하고, 구현하면 체크한다. 작성 규칙은 그 폴더의 README.md에 있다.
