# 다오니 게임 리소스 제작 요청서

현재 제작 중인 다오니 미니게임 프로젝트에 아래 신규 게임 리소스를 추가 제작해주세요.

## 가장 중요한 공통 조건

- 현재 다오니 게임에서 사용 중인 기존 캐릭터/배경/UI 아트 스타일을 그대로 유지
- 기존 리소스의 선 굵기, 채색, 명암, 눈/얼굴 표현, 비율, 전체 분위기를 기준으로 제작
- 게임마다 그림체가 달라지지 않도록 통일
- 실제 게임 코드에서 개별 Sprite로 사용할 수 있도록 오브젝트별 분리
- 가능한 리소스는 투명 배경 PNG
- 캐릭터/오브젝트 주변 불필요한 여백 최소화
- 애니메이션 프레임은 크기와 중심축 동일
- UI 글자는 이미지에 넣지 말고 Godot Label 사용
- 특정 유명 게임의 캐릭터/로고/고유 디자인은 복제하지 않고 장르만 참고
- 기존 다오니 게임 메인 캐릭터가 있다면 가능한 게임에서 공통 마스코트로 활용

---

# 1. 퍼즐버블 스타일

## 필수 리소스

### Bubble

색상별 기본 공:

- 빨강
- 파랑
- 노랑
- 초록
- 보라
- 주황

조건:
- 동일 크기
- 원형
- 색 구분 명확
- 작은 하이라이트
- 투명 배경

### Bubble 터짐 Effect

공통 애니메이션 4~6 Frame:

```text
기본
→ 부풀기
→ 갈라짐
→ 작은 조각/별
→ 소멸
```

### Shooter

- 귀여운 발사기
- 가능하면 다오니 캐릭터가 공을 들고 발사하는 버전도 제작

### 조준 UI

- Trajectory Dot
- Aim Arrow
- 발사 방향 표시용 작은 화살표

### 배경

- 밝은 퍼즐 배경 1종
- 중앙 Game Board가 잘 보이도록 중앙은 복잡하지 않게

---

# 2. 낙하 블록 게임

## Block

Tetromino 자체 이미지보다 1칸짜리 Cell Block 7종 제작.

색상:

- 하늘색
- 노랑
- 보라
- 초록
- 빨강
- 파랑
- 주황

### 상태

- Normal
- Ghost/반투명
- Line Clear Flash

### 배경/UI

- 어두운 Game Board
- 다오니 스타일 외곽 Frame
- Hold Panel
- Next Panel
- Score Panel
- Level Panel

글자는 제외.

---

# 3. 2048

## Tile Background

단계별 Tile 배경:

```text
2
4
8
16
32
64
128
256
512
1024
2048
```

숫자는 이미지에 넣지 않는다.

숫자가 커질수록:
- 색상 강화
- 광택 강화
- 장식 조금씩 증가

### Effect

- 새 Tile Spawn Glow
- Merge Flash
- 2048 완성 축하 Effect

### Board

- 4x4 Board Frame
- Empty Cell Background

---

# 4. 스도쿠

## Board UI

- 9x9 Board 스타일
- 3x3 구역을 구분하는 굵은 구분선
- 선택 Cell Overlay
- 같은 행/열 Overlay
- 오답 Cell Overlay

Grid는 코드로 그릴 수 있으므로 Panel/Overlay 중심 제작 가능.

## Number Button

1~9 숫자 버튼 배경.

상태:
- Normal
- Pressed
- Selected
- Disabled

숫자는 Godot Label 사용.

## 기능 아이콘

- 메모
- 지우개
- 힌트
- Undo

---

# 5. Shikaku

## Grid UI

- 기본 Cell
- 선택 중 Cell
- 올바른 Region
- 잘못된 Region
- Hint Region

숫자는 Label로 표시.

## Region Overlay

완료 후 각 Rectangle을 구분할 수 있도록 6~8개의 연한 색 Overlay.

## 기능 아이콘

- Hint
- Reset
- Undo

---

# 6. 탄막 슈팅

이 게임은 리소스 양이 많으므로 단계별 제작.

## Player

다오니 게임 스타일의 플레이어 기체 또는 탈것.

기존 세계관과 어울리는 방향으로:
- 귀여운 소형 비행기
- 드래곤
- 마법 탈것

중 하나를 선택.

필수 상태:

```text
Idle/비행
좌측 이동
우측 이동
피격
파괴
```

무적 Blink는 코드 처리 가능.

## Player Bullet

- 기본 탄
- 강화 탄
- 보조 탄

## Enemy

최소 4종:

1. 소형 적
2. 중형 적
3. 원거리 공격 적
4. Elite

각 적:
- 기본 비행
- 피격 Flash는 코드 처리
- 파괴 4~6 Frame

## Boss

1종.

필수:
- Idle
- 공격 준비
- 피격
- 파괴

Boss는 다른 적보다 확실히 크고 실루엣이 구분되도록 제작.

## Enemy Bullet

형태:

- 작은 원
- 큰 원
- 타원탄
- 별/마법탄
- 조준탄

색상:

- 빨강
- 파랑
- 보라
- 노랑

배경과 대비가 확실하게.

## Item

- Power Up
- Bomb
- Life/Recovery
- Score

## Effect

- 작은 폭발
- 큰 폭발
- Boss 폭발
- Power Up
- Bomb
- Hit

## 배경

세로 스크롤 배경 2~3종.

예:
- 하늘
- 구름
- 판타지 숲/마을 상공

상하 Seamless Scroll 가능하게 제작.

---

# 7. Match-3

## 기본 Tile

6종.

다오니 게임 스타일에 맞춰 아래 중 한 테마 선택:

- 동물 얼굴
- 과일
- 보석
- 장난감
- 디저트

한 세트 안에서는 테마 혼합 금지.

조건:
- 동일 크기
- 실루엣 확실히 구분
- 작은 화면에서도 색/모양으로 구별 가능

## 특수 Tile

### 4 Match
- 가로 Line 제거
- 세로 Line 제거

### 5 Match
- Rainbow / Color Bomb

### L/T Match
- Bomb Tile

## Effect

- 기본 Match Pop
- Line Clear
- Bomb
- Rainbow
- Combo

## Board

- 8x8 Grid 배경
- Tile Slot
- 선택 Highlight

---

# 8. 스와이프 벽돌깨기

## Ball

- 기본 Ball
- Glow Ball
- 발사 순간 Effect

## Brick

Brick Background 5~6종.

숫자는 Label로 표시.

HP 단계에 따라 색이 바뀌도록:

```text
Low
Medium
High
Very High
Danger
```

## Brick Effect

- Hit Flash
- Crack Overlay
- Break Effect

## Ball Add Item

공 추가용 원형 아이콘.

`+` 문자는 Godot에서 표시 가능.

## Guide

- Trajectory Dot
- Aim Arrow

## Background

- 세로형 Arcade Board
- 공의 궤적이 잘 보이도록 중앙은 단순하고 대비가 높은 구성

---

# 공통 결과/메뉴 리소스

## 버튼

- Play
- Retry
- Home
- Pause
- Resume
- Settings
- Next

글자는 넣지 않고 버튼 배경/Icon 중심.

상태:

```text
Normal
Pressed
Disabled
```

## 결과 Effect

- 별
- 반짝이
- Confetti
- 최고 기록 왕관/메달

## 난이도 Icon

- 쉬움
- 보통
- 어려움

텍스트 없이 아이콘만.

---

# 제작 우선순위

```text
1. 2048
2. Match-3
3. 스와이프 벽돌깨기
4. 퍼즐버블
5. 낙하 블록
6. 스도쿠
7. Shikaku
8. 탄막 슈팅
```

탄막 슈팅은 리소스 수가 많으므로 마지막에 별도 묶음으로 제작.

---

# 파일 정리 요청

```text
Assets/
├─ Common/
│  ├─ UI/
│  ├─ Effects/
│  └─ Icons/
│
├─ PuzzleBubble/
├─ FallingBlock/
├─ Game2048/
├─ Sudoku/
├─ Shikaku/
├─ Shooting/
├─ Match3/
└─ SwipeBrick/
```

파일명 예:

```text
bubble_red.png
bubble_blue.png
bubble_pop_01.png

match3_tile_01.png
match3_tile_02.png

shooting_enemy_small_01.png

brick_low.png
brick_high.png
```

영문 소문자 + snake_case 권장.

---

# 제작 전 기준 확인

기존 다오니 게임 리소스를 먼저 확인하고 아래 항목을 그대로 맞춰주세요.

1. 기존 캐릭터 얼굴과 비율
2. 기존 UI 모서리 형태
3. 선 굵기
4. 명암 단계
5. 색상 채도
6. 이미지 해상도
7. Sprite 중심축
8. Outline 사용 여부
