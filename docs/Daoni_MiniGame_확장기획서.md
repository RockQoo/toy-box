# 다오니 미니게임 확장 기획서

> **이 문서는 원본 참고용이며, 실제 기획은 `docs/games/07-merge.md` ~ `18-sky-flight.md`를 따른다.**

Godot 2D 기준으로 AI가 바로 구현을 시작할 수 있도록 게임 규칙, 상태 흐름, 데이터 구조, 씬 구성, 필수 기능, 완료 조건을 정리한 문서입니다.

> 퍼즐버블, 테트리스, 1945, 애니팡 등은 장르 이해를 위한 참고명입니다. 실제 제작 시 캐릭터, UI, 사운드, 그래픽은 자체 리소스로 구성합니다.

---

# 공통 개발 규칙

## 프로젝트 구조

```text
Main
├─ MainMenu
├─ PuzzleBubbleGame
├─ FallingBlockGame
├─ Game2048
├─ SudokuGame
├─ ShikakuGame
├─ ShootingGame
├─ Match3Game
└─ SwipeBrickGame
```

## 공통 시스템

```text
GameManager
AudioManager
SaveManager
SettingsManager
SceneManager
PausePopup
ResultPopup
```

## 공통 기능

- PC 마우스/키보드 조작
- 모바일 터치 대응
- 일시정지 / 다시하기 / 메인으로
- BGM / 효과음 볼륨
- 최고 기록 저장
- 난이도 선택
- 결과 화면
- 게임 로직과 화면 연출 분리
- 데이터 기반 스테이지 또는 문제 구성
- 리소스 교체가 쉽도록 Sprite, Audio, Data를 코드와 분리

---

# 1. 퍼즐버블 스타일 게임

## 목표

화면 아래의 발사기에서 색깔 공을 발사하여 위쪽 공에 연결한다. 같은 색 공이 3개 이상 연결되면 제거된다. 보드의 공을 모두 제거하면 클리어하며, 공이 하단 위험선까지 내려오면 실패한다.

## 게임 플로우

```text
메인
→ 난이도 선택
→ 보드 생성
→ 현재 공 / 다음 공 생성
→ 조준
→ 발사
→ 충돌 위치 계산
→ Grid 고정
→ 같은 색 연결 검사
→ 제거
→ 천장 연결 검사
→ 공중에 뜬 공 낙하
→ 다음 공 준비
→ 클리어 또는 실패 판정
```

## 보드

육각형처럼 줄마다 반 칸씩 어긋나는 형태.

```text
O O O O O O
 O O O O O
O O O O O O
 O O O O O
```

Bubble 데이터:

```text
row
column
color_id
is_attached
```

## 조작

PC:
- 마우스 이동: 조준
- 마우스 클릭: 발사

모바일:
- 드래그: 조준
- 손 떼기: 발사

## 발사 규칙

- 공은 직선으로 이동
- 좌우 벽에 충돌하면 반사
- 천장 또는 다른 Bubble과 충돌하면 가장 가까운 유효 Grid Cell에 고정
- 조준 시 예상 궤적 점선 표시 권장

## 매치 판정

발사된 Bubble 기준 BFS/Flood Fill.

- 같은 색 연결 수가 3개 이상이면 제거
- 제거 후 천장과 연결되지 않은 Bubble은 낙하
- 낙하 Bubble도 추가 점수

## 점수 예시

```text
직접 제거 1개 = 10점
낙하 제거 1개 = 20점
연속 제거 = Combo 배율
```

## 다음 공

현재 공과 NEXT 공을 표시한다. 현재 보드에 존재하지 않는 색은 새로 생성하지 않는 것을 권장한다.

## 난이도

쉬움:
- 색 3종
- 공 적음
- 천장 하강 없음

보통:
- 색 4종
- 일정 횟수 매치 실패 시 한 줄 하강

어려움:
- 색 5~6종
- 실패 허용 횟수 감소
- 초기 공 배치 증가

## 주요 클래스

```text
PuzzleBubbleGame
BubbleGrid
Bubble
BubbleShooter
BubbleProjectile
TrajectoryGuide
MatchResolver
CeilingConnectionResolver
ScoreManager
```

## 완료 조건

- 벽 반사 정상 동작
- 충돌 후 유효 Cell 고정
- 3개 이상 같은 색 제거
- 떠 있는 Bubble 제거
- NEXT 표시
- 클리어/게임오버 판정

---

# 2. 테트리스 스타일 낙하 블록 게임

## 목표

위에서 떨어지는 블록을 이동/회전하여 가로 한 줄을 완성한다. 완성된 줄은 제거되며, 블록이 상단까지 쌓이면 종료된다.

## 보드

```text
가로 10
세로 20
숨김 영역 2칸 권장
```

## 블록

기본 7종:

```text
I O T S Z J L
```

## 게임 플로우

```text
블록 생성
→ 자동 낙하
→ 이동 / 회전
→ 충돌
→ 현재 블록 고정
→ 완성 줄 검사
→ 줄 제거
→ 점수 계산
→ 다음 블록 생성
```

## 조작

```text
← → : 좌우 이동
↓ : Soft Drop
SPACE : Hard Drop
↑ 또는 X : 시계 방향 회전
Z : 반시계 방향 회전
C : Hold
```

## 점수 예시

| 삭제 줄 | 점수 |
|---|---:|
| 1 | 100 |
| 2 | 300 |
| 3 | 500 |
| 4 | 800 |

## 추가 기능

- Next Queue 3개
- Hold
- Ghost Piece
- Level 증가에 따른 낙하 속도 상승

## 데이터 구조

실제 게임 상태는 Sprite가 아닌 배열이 기준.

```text
board[y][x]
```

## 주요 클래스

```text
FallingBlockGame
Board
Tetromino
TetrominoData
NextQueue
HoldSystem
GhostPiece
LineClearManager
```

## 완료 조건

- 7종 블록 생성
- 이동/회전 충돌 판정
- 줄 제거
- Next/Hold
- Hard Drop
- 상단 Game Over

---

# 3. 2048

## 목표

4x4 보드의 숫자 타일을 밀어 같은 숫자를 합친다. 2048을 만들거나 더 이상 움직일 수 없을 때까지 플레이한다.

## 보드

```text
4 x 4
```

## 조작

- PC: 방향키
- 모바일: Swipe

## 이동 처리

예: 왼쪽 이동

```text
1. Tile 왼쪽 압축
2. 같은 숫자 인접 Tile 합치기
3. 다시 압축
4. 실제 이동이 있었다면 새 Tile 생성
```

## 합치기 규칙

한 번의 Move에서 하나의 Tile은 한 번만 합쳐진다.

```text
2 2 2 .
→
4 2 . .
```

## 새 Tile

```text
2 = 90%
4 = 10%
```

## 점수

합쳐져서 만들어진 숫자를 점수에 추가.

## 승리

2048 생성 시:

```text
2048 완성!
[계속하기]
[종료]
```

## 게임오버

- 빈 칸 없음
- 상하좌우 인접한 같은 숫자 없음

두 조건 모두 만족하면 종료.

## 주요 클래스

```text
Game2048
Board2048
Tile2048
MoveResolver
SpawnResolver
ScoreManager
```

## 완료 조건

- 네 방향 Swipe
- 이중 합체 오류 없음
- 유효 이동에서만 새 Tile 생성
- 승리/게임오버 판정

---

# 4. 스도쿠

## 목표

9x9 Grid를 채워 각 행, 열, 3x3 영역에 1~9가 한 번씩 등장하도록 한다.

## 데이터

```text
puzzle[9][9]
solution[9][9]
difficulty
```

초기 버전은 미리 검증한 JSON 문제 사용 권장.

## 기능

- 고정 숫자
- 사용자 입력 숫자
- 메모 숫자
- 행/열/3x3 강조
- 같은 숫자 강조
- 지우기
- 힌트
- 오답 즉시 표시 ON/OFF

## 난이도

```text
쉬움
보통
어려움
전문가
```

## 완료 화면

```text
완료!
시간 05:42
실수 1
힌트 2
```

## 주요 클래스

```text
SudokuGame
SudokuBoard
SudokuCell
SudokuPuzzleData
SudokuValidator
MemoSystem
HintSystem
NumberInputPanel
```

---

# 5. Shikaku

## 목표

Grid 전체를 직사각형 영역으로 나눈다.

각 직사각형은:
- 숫자 Cell을 정확히 1개 포함
- 영역 넓이가 그 숫자와 동일

해야 한다.

## 조작

- PC: 시작 Cell 클릭 후 드래그
- 모바일: Touch Drag

## 영역 판정

```text
숫자 0개 → 실패
숫자 2개 이상 → 실패
숫자 1개 → 넓이 검사
넓이 != 숫자 → 실패
넓이 == 숫자 → 확정
```

## 난이도

```text
쉬움 5x5
보통 7x7
어려움 10x10
```

## 데이터

```text
width
height
clues[]
solution_regions[]
```

## 주요 클래스

```text
ShikakuGame
ShikakuBoard
ShikakuCell
RectangleSelector
Region
PuzzleValidator
HintSystem
```

---

# 6. 탄막 슈팅 게임

## 목표

플레이어 기체를 조작해 적과 탄환을 피하면서 적을 파괴하고, 스테이지 마지막 Boss를 처치한다.

## 조작

PC:

```text
WASD / 방향키 = 이동
SPACE = 공격
SHIFT = 저속 이동
B = Bomb
```

모바일:

```text
Drag = 이동
자동 공격
Bomb 버튼
```

## 플레이어 공격

```text
Lv1 = 2발 직선
Lv2 = 3Way
Lv3 = 5Way
Lv4 = 강화 5Way
```

## 적

- Small: 직선 이동
- Medium: 좌우 이동 + 공격
- Shooter: 플레이어 방향 조준탄
- Elite: 높은 HP + 복합 패턴

## 탄막 패턴

- 원형
- 부채꼴
- 조준탄
- 나선형
- 교차형

## 플레이어

- 작은 HitBox 사용
- Life x3
- 피격 시 Life -1
- 1.5초 무적

## 아이템

```text
P = 공격 강화
B = Bomb
H = Life/Recovery
S = Score
```

## Bomb

- 화면 적탄 삭제
- 전체 적 Damage
- 잠시 무적

## Boss

```text
Phase 1 → HP 70%
Phase 2 → HP 40%
Phase 3 → HP 0%
```

## 성능

Bullet과 Enemy는 Object Pool 사용.

## 주요 클래스

```text
ShootingGame
PlayerAircraft
PlayerWeapon
Enemy
EnemySpawner
EnemyBullet
PlayerBullet
BulletPattern
Item
Boss
StageController
ObjectPool
```

---

# 7. Match-3

## 목표

인접한 블록 두 개를 교환하여 같은 종류를 가로 또는 세로로 3개 이상 연결한다.

## 보드

```text
8 x 8
```

## Swap

- 인접 블록만 교환
- Match가 없으면 원래 위치로 복귀

## Match

```text
3개 = 일반 제거
4개 = 가로/세로 줄 제거 특수 블록
5개 = Rainbow / Color Bomb
L/T = Bomb Tile
```

## Cascade

```text
Match 제거
→ 빈 칸
→ 위 블록 낙하
→ 새 블록 생성
→ 자동 Match 검사
→ 추가 제거
```

## 모드

초기 버전:

```text
Time Attack 60초
```

## 초기 보드 조건

- 이미 완성된 3 Match 없음
- 최소 1개 이상의 가능한 Move 존재
- Move가 없으면 Shuffle

## 주요 클래스

```text
Match3Game
Match3Board
Match3Tile
SwapController
MatchFinder
GravityResolver
SpecialTile
ComboManager
ShuffleResolver
```

---

# 8. 스와이프 벽돌깨기

## 목표

화면 아래에서 여러 개의 공을 같은 방향으로 발사하여 숫자가 적힌 벽돌을 제거한다. 벽돌이 화면 아래까지 내려오면 게임 종료.

## 조작

```text
Drag = 발사 방향
손 떼기 = 발사
```

조준 중 예상 궤적 표시.

## 공

BallCount만큼 동일한 각도로 일정 간격을 두고 순차 발사.

## Brick

숫자 = HP.

```text
[10]
→ 충돌
[9]
```

0이 되면 제거.

## Ball Add Item

보드의 공 추가 아이템과 충돌하면 다음 턴부터:

```text
BallCount +1
```

## 턴 종료

- 모든 공이 바닥으로 복귀하면 종료
- 가장 먼저 내려온 공의 X 위치를 다음 발사 위치로 사용

## 다음 턴

- Brick 한 줄 하강
- 새 Row 생성
- Ball Add Item 생성 가능
- Turn 증가

## 게임오버

Brick이 Dead Line에 도달하면 종료.

## 성능

Ball 수가 많아질 수 있으므로 Object Pool 사용.

## 주요 클래스

```text
SwipeBrickGame
BallLauncher
Ball
Brick
BrickGrid
TurnManager
TrajectoryGuide
BallAddItem
ObjectPool
```

---

# 추가 추천 게임

## 9. Snake

먹이를 먹으면 몸 길이가 증가하는 Grid 게임.

```text
SnakeGame
Snake
SnakeSegment
FoodSpawner
GridManager
```

## 10. 지뢰찾기

Cell을 열어 주변 지뢰 개수를 확인하고, 숫자 0이면 주변을 자동 오픈.

```text
MinesweeperGame
MineBoard
MineCell
MineGenerator
FloodOpenResolver
```

## 11. Sokoban

상자를 밀어 Goal에 모두 넣는 퍼즐.

```text
SokobanGame
SokobanBoard
Player
Box
Goal
StageData
```

## 12. Flappy 스타일

터치할 때 상승하고 Gravity로 떨어지며 기둥 사이를 통과하면 점수 +1.

```text
FlappyGame
Player
PipeSpawner
Pipe
ScoreTrigger
```

## 13. 미로 추격 게임

미로를 돌아다니며 점을 수집하고 적을 피하는 구조.

```text
MazeChaseGame
MazeBoard
Player
EnemyAI
Collectible
```

## 14. 과일 슬라이스

Swipe Line과 과일 Collider가 만나면 과일이 잘리는 게임.

```text
FruitSliceGame
FruitSpawner
Fruit
SwipeTrail
SliceResolver
```

## 15. Pong

좌우 Paddle로 공을 주고받는 게임.

```text
PongGame
Ball
PlayerPaddle
AIPaddle
ScoreManager
```

## 16. 고전 벽돌깨기

Paddle로 공을 튕겨 위쪽 Brick을 제거.

```text
BreakoutGame
Paddle
Ball
Brick
Item
LevelManager
```

## 17. SameGame

붙어 있는 같은 색 블록 그룹을 선택해 제거.

```text
SameGame
SameBoard
SameTile
GroupFinder
GravityResolver
ColumnResolver
```

---

# 권장 개발 순서

```text
1. 2048
2. Snake
3. Flappy
4. 고전 벽돌깨기
5. Match-3
6. 스와이프 벽돌깨기
7. 퍼즐버블
8. 테트리스 스타일
9. 스도쿠
10. Shikaku
11. Sokoban
12. 탄막 슈팅
```

---

# AI 개발 요청 시 공통 지시사항

```text
- Godot 2D 기준
- 게임 하나당 독립 Scene
- 로직과 리소스 분리
- PC + 모바일 입력 지원
- 하드코딩 최소화
- 데이터는 Resource 또는 JSON으로 분리
- AI가 임의 기능을 추가하지 말고 기획서 우선
- 각 단계 완료 후 항상 실행 가능한 상태 유지
- 한 번에 전체 구현하지 말고 단계별 구현
```
