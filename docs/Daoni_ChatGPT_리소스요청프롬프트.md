# 다온이 놀이터 리소스 제작 요청

너는 지금부터 아이(유아~초등 저학년)용 미니게임 모음 앱 **"다온이 놀이터"**의 2D 게임 아트 담당이야.
이 대화에 첨부한 이미지가 지금 게임에서 쓰는 그림이야(동물 카드, 풍선, 과일, 숫자/도형 카드, 공룡, 하늘 배경). **새 그림은 전부 이 그림체와 똑같이** 그려 줘.

---

## 1. 그림체 기준 (모든 그림 공통)

- 첨부 이미지와 같은 선 굵기, 채색, 명암 단계, 하이라이트(광택), 눈/얼굴 표현, 머리 큰 귀여운 비율, 채도
- 밝고 부드러운 파스텔 톤. 무섭거나 공격적인 표현 금지
- 아이용 앱이라 **실패·위험·아픔을 표현하는 그림은 만들지 않는다.** 빨간 경고 표시, 해골, 부서지는 캐릭터, 우는 얼굴 금지. 실수했을 때는 "깜짝 놀람", "어지러움(별 빙글)" 정도의 귀여운 표현만
- 유명 게임(테트리스, 퍼즐버블, 애니팡, 2048 등)의 캐릭터, 로고, 고유 디자인은 따라 하지 않는다. 장르만 참고
- **그림 안에 글자나 숫자를 넣지 않는다**(게임에서 글자를 따로 올린다). 예외는 따로 적은 항목만

## 2. 출력 규칙

- **투명 배경 PNG**(배경 그림만 불투명)
- 한 번에 **시트 한 장**만 만든다. 시트는 격자로 배치하고, **그림끼리 절대 닿지 않게** 그림 크기의 1/4 이상 투명 여백을 둔다(나중에 자동으로 잘라 쓴다)
- 같은 시트 안의 그림은 **같은 크기, 같은 중심축, 같은 시점**으로 그린다. 애니메이션 프레임이나 포즈 세트는 발 위치(바닥선)를 맞춘다
- 그림자, 바닥, 테두리 카드 배경을 넣지 않는다. **3D 렌더링 질감 금지**(외곽선 만화풍 유지)(캐릭터는 카드 없이 몸만)
- 타일(바닥, 땅, 배경)은 **이어 붙였을 때 이음매가 안 보이게(seamless)** 만든다
- 판/패널/버튼 배경은 **9-slice로 늘려 쓸 수 있게** 모서리 장식은 네 귀퉁이에만 두고 가운데와 변은 단순하게 만든다
- 배경 그림은 **가로 16:9, 1920×1080 이상**. 화면비에 따라 위아래나 좌우가 조금 잘릴 수 있으니 중요한 것은 가운데에 두고, 가운데(게임판 자리)는 단순하고 밝게 비운다
- 시트마다 그림 아래에 내가 붙일 파일 이름을 순서대로 목록으로 같이 알려 줘(아래 목록의 이름 그대로)

## 3. 진행 방식

1. 먼저 **시트 0(스타일 기준표)**을 만들어서 내 확인을 받는다
2. 내가 "다음" 또는 시트 번호를 말하면 그 시트를 만든다
3. 그림체가 첨부 이미지와 달라 보이면 다시 그려 달라고 할 테니, 그때는 첨부 이미지를 다시 보고 맞춘다

화면은 **가로(1280×720 기준)**이다. 원래 세로인 장르(퍼즐버블, 낙하 블록, 벽돌깨기)는 가로 화면 가운데에 세로 게임판을 두고 양옆을 배경으로 채운다. 그래서 게임판 프레임과 배경을 따로 만든다.

---

# A. 공통

### 시트 0. 스타일 기준표 (확인용)
- 토끼 캐릭터 전신 1장(정면, 기본 포즈). 첨부한 토끼 카드 그림과 같은 얼굴, 몸만 투명 배경으로
- 이것이 앞으로 모든 캐릭터의 기준이 된다

### 시트 1~3. 친구 캐릭터 (투명 배경 전신)
지금은 동물 카드 그림을 캐릭터로 써서 스티커처럼 보인다. 카드 없이 몸 전체를 그린 캐릭터가 필요하다.
캐릭터: **토끼, 판다, 펭귄, 사자, 고양이** (고양이는 첨부에 없으니 같은 그림체로 새로 디자인)
포즈 4종: `idle`(기본), `jump`(점프), `cheer`(두 팔 들고 환호), `walk`(오른쪽을 보고 걷기, 왼쪽은 좌우 반전해서 쓴다)
- 시트 1: 토끼, 판다 → `buddy/rabbit_idle.png`, `rabbit_jump.png`, `rabbit_cheer.png`, `rabbit_walk.png`, `panda_...`(같은 규칙)
- 시트 2: 펭귄, 사자 → `buddy/penguin_...`, `buddy/lion_...`
- 시트 3: 고양이 → `buddy/cat_...`

### 시트 4. 주인공 토끼 추가 포즈 (새 게임용)
새 게임 대부분에서 토끼가 주인공이다.
- `buddy/rabbit_throw.png`: 공을 두 손으로 머리 위로 들고 던지려는 포즈(퍼즐버블, 벽돌깨기 발사대)
- `buddy/rabbit_push.png`: 오른쪽으로 상자를 미는 포즈(상자 밀기)
- `buddy/rabbit_back.png`: 뒷모습 서 있기(상자 밀기 위쪽 이동)
- `buddy/rabbit_front_walk.png`: 정면 걷기(상자 밀기 아래쪽 이동)
- `buddy/rabbit_surprised.png`: 깜짝 놀란 얼굴(실수했을 때, 귀엽게)
- `buddy/rabbit_dizzy.png`: 머리 위에 별이 빙글빙글(부딪혔을 때)

### 시트 5. 공통 버튼 아이콘
첨부한 `play`, `retry` 아이콘과 같은 스타일(둥근 버튼 위 흰 기호). 글자 없음.
- `ui/home.png`(집), `ui/pause.png`(일시정지), `ui/settings.png`(톱니), `ui/next.png`(다음 화살표), `ui/undo.png`(되돌리기), `ui/hint.png`(전구), `ui/reset.png`(처음부터), `ui/erase.png`(지우개), `ui/memo.png`(연필), `ui/sound_on.png`, `ui/sound_off.png`, `ui/fast.png`(빨리 감기)

### 시트 6. 터치 조작 버튼
모바일에서 누르는 큰 버튼. 같은 크기 둥근 버튼.
- `ui/btn_left.png`, `ui/btn_right.png`, `ui/btn_up.png`, `ui/btn_down.png`, `ui/btn_rotate.png`(회전 화살표), `ui/btn_drop.png`(아래로 쿵, 겹화살표), `ui/btn_hold.png`(손바닥/보관함)

### 시트 7. 속도 선택 아이콘
지금은 거북이·펭귄·토끼 카드를 써서 캐릭터 고르기와 헷갈린다. 동물이 아닌 것으로.
- `ui/speed_slow.png`(달팽이), `ui/speed_normal.png`(자전거), `ui/speed_fast.png`(로켓)

### 시트 8. 판/패널 (9-slice)
- `ui/board_frame.png`: 게임판 바깥 테두리(밝은 나무 또는 쿠키 느낌, 가운데는 비움)
- `ui/board_bg.png`: 게임판 안쪽 바탕(연한 단색에 아주 은은한 무늬)
- `ui/panel.png`: 점수, 다음 블록 등을 담는 작은 패널
- `ui/cell_empty.png`: 빈 칸 하나(정사각형, 살짝 들어간 느낌)
- `ui/cell_select.png`: 고른 칸 테두리(밝은 노랑 빛)

### 시트 9. 배경 3장 (각각 한 장씩, 1920×1080, 불투명)
지금 하늘(공원) 배경만 그림이고 나머지는 코드로 그린 단색 그라데이션이다. 첨부한 하늘 배경과 같은 그림체로.
- `backgrounds/space.png`: 귀여운 우주(행성, 별, 달)
- `backgrounds/sea.png`: 바닷속(햇살, 해초, 모래, 거품)
- `backgrounds/sky_high.png`: 구름 위 높은 하늘

### 시트 10. 퍼즐 게임 배경 2장 (각각 한 장씩, 1920×1080, 불투명)
가운데에 게임판이 올라가므로 가운데는 단순하고, 장식은 양옆과 위아래 가장자리에만.
- `backgrounds/playroom.png`: 따뜻한 놀이방(장난감 선반, 창문)
- `backgrounds/garden.png`: 꽃 정원

### 시트 11. 메인 메뉴와 앱
- `ui/title_banner.png`: 제목 글자를 올릴 리본/구름 배너(글자 없이)
- `app_icon.png`: 앱 아이콘 1024×1024 정사각형, 토끼 얼굴 + 장난감 상자(불투명 배경 허용, 글자 없음)

---

# B. 지금 있는 게임 보강

### 시트 12. 과일 받기
- `fruit/golden_apple.png`: 반짝이는 황금 사과(첨부한 사과와 같은 크기, 지금은 동전으로 대신하고 있음)
- 바구니 3종 추가(첨부한 바구니와 같은 크기, 같은 시점): `fruit/basket_bucket.png`(양동이), `fruit/basket_hat.png`(뒤집은 밀짚모자), `fruit/basket_cart.png`(작은 손수레)

### 시트 13. 숫자 카드 6~9
첨부한 숫자 카드 1~5와 **완전히 같은 카드 모양, 크기, 광택**으로 숫자만 다르게. (이 시트만 숫자가 그림에 들어간다)
- `quiz/num_6.png`, `quiz/num_7.png`, `quiz/num_8.png`, `quiz/num_9.png`

### 시트 14. 퀴즈 물감 방울
색깔 문제 예시로 쓰는 물감 얼룩 6색(광택 있는 동글동글한 물감 방울).
- `quiz/paint_red.png`, `paint_yellow.png`, `paint_green.png`, `paint_blue.png`, `paint_purple.png`, `paint_orange.png`

### 시트 15. 미로 타일 (상자 밀기에도 같이 씀)
위에서 내려다본 시점. 지금은 코드로 그린 크림색 바닥과 초록 울타리다.
- `maze/floor.png`: 잔디/흙길 바닥 타일(정사각형, seamless)
- `maze/wall.png`: 둥근 덤불 울타리 블록(정사각형 한 칸을 꽉 채우는 벽)
- `maze/fence_h.png`, `maze/fence_v.png`: 칸 사이에 놓는 얇은 울타리(가로, 세로)
- `maze/post.png`: 울타리 기둥
- `maze/exit.png`: 출구(아치 문 + 깃발)

### 시트 16. 공룡 점프: 넘어짐 포즈
첨부한 초록 공룡과 같은 공룡, 같은 크기.
- `dino/dino_bump.png`: 엉덩방아 찧고 머리 위에 별이 도는 포즈(아파 보이지 않게, 멋쩍게 웃는 얼굴)

### 시트 17~18. 공룡 점프: 다른 공룡
첨부한 초록 공룡과 같은 크기, 같은 포즈 세트: `run`(달리기), `jump`(점프), `crouch`(엎드림), `bump`(엉덩방아)
- 시트 17: 트리케라톱스(파랑) → `dino/trike_run.png`, `trike_jump.png`, `trike_crouch.png`, `trike_bump.png`
- 시트 18: 스테고사우루스(분홍) → `dino/stego_...`(같은 규칙)

### 시트 19. 공룡 점프: 땅과 수집품
- `dino/ground_tile.png`: 잔디+흙 땅 조각. **좌우 끝이 잘려 있어서 옆으로 이어 붙이면 끊김 없이 이어지게**(지금 그림은 양 끝이 둥글어 이음매가 보인다)
- `dino/star_pickup.png`: 공중에 떠 있는 반짝 별(점프해서 먹는 것)

---

# C. 새 게임

### 시트 20. 새 게임 메뉴 아이콘
메인 메뉴 카드에 올리는 대표 그림(정사각형 안에 들어가는 크기, 글자 없음). 각 게임 그림체를 미리 보여 주는 그림으로.
- `menu/merge.png`(2048: 쌓인 반짝 타일), `menu/snake.png`(애벌레), `menu/flappy.png`(날개 단 아기새), `menu/breakout.png`(공과 벽돌), `menu/match3.png`(과일 3개 나란히), `menu/swipe_brick.png`(공 여러 개와 벽돌), `menu/bubble.png`(색 방울 무더기), `menu/blocks.png`(쌓인 블록), `menu/picture_sudoku.png`(동물 얼굴 격자), `menu/shikaku.png`(색 네모 조각), `menu/push_box.png`(상자와 토끼), `menu/sky_flight.png`(작은 비행기 탄 토끼)

### 시트 21. 숫자 합치기(2048 방식) 타일
숫자는 게임에서 올린다. **같은 모양 둥근 정사각형 타일**을 단계마다 색을 바꾸고, 단계가 오를수록 광택과 장식(별, 반짝이, 리본, 왕관)이 조금씩 늘어나게. 가운데에 4자리 숫자가 들어갈 자리를 비운다.
- `merge/tile_01.png` ~ `merge/tile_12.png` (2, 4, 8, … 4096 순서, 12단계. 1~4는 파스텔, 5~8은 진한 색, 9~12는 금빛/무지개)

### 시트 22. 애벌레 (스네이크 방식)
위에서 내려다본 시점, 정사각형 한 칸 크기. 오른쪽을 보는 방향 기준(게임에서 회전시킨다).
- `snake/head.png`(웃는 얼굴), `snake/head_eat.png`(입 벌림), `snake/body.png`(곧은 몸통), `snake/body_corner.png`(꺾인 몸통), `snake/tail.png`
- `snake/leaf.png`(먹이 잎사귀), `snake/grass_tile.png`(바닥 타일, seamless)

### 시트 23. 아기새 (플래피 방식)
- `flappy/bird_01.png` ~ `bird_03.png`: 날갯짓 3프레임(같은 크기, 같은 중심)
- `flappy/bird_bump.png`: 부딪혀서 어지러운 얼굴(귀엽게)
- `flappy/pillar_body.png`: 세로로 늘려 쓰는 나무 기둥 몸통(위아래 seamless)
- `flappy/pillar_cap.png`: 기둥 끝 장식(나뭇잎 뭉치)

### 시트 24. 벽돌깨기 (고전 + 스와이프 공용)
벽돌 숫자(남은 횟수)는 게임에서 올린다.
- 벽돌 6색 단계(같은 크기 둥근 사각형, 숫자 자리 비움. 빨강 계열은 쓰지 말고 연두→하늘→파랑→보라→분홍→금색 순): `brick/brick_1.png` ~ `brick_6.png`
- `brick/crack_1.png`, `brick/crack_2.png`: 벽돌 위에 겹치는 금 간 자국(반투명 흰 선, 2단계)
- `brick/ball.png`(작은 공), `brick/ball_glow.png`(빛나는 공)
- `brick/item_ball_add.png`: 공 늘리기 아이템(동그란 방울 안에 작은 공들)
- `brick/paddle.png`: 고전 벽돌깨기 받침판(구름 모양, 가로로 긴)
- `brick/aim_dot.png`(조준 점), `brick/aim_arrow.png`(조준 화살표)
- `brick/floor_line.png`: 바닥 선(가로로 늘려 쓰는 꽃줄/풀줄, 경고 느낌 없이)

### 시트 25. 과일 맞추기 (Match-3 방식) 타일
첨부한 과일 그림(사과, 딸기, 바나나, 오렌지, 포도, 수박)을 **같은 크기 정사각형 안에 꽉 차게 들어가는 아이콘**으로 다시 그린다. 작은 화면에서도 모양만으로 구분되게 실루엣을 확실히.
- `match3/apple.png`, `strawberry.png`, `banana.png`, `orange.png`, `grape.png`, `watermelon.png`
- 특수 타일은 과일 위에 겹치는 오버레이로: `match3/fx_line_h.png`(가로 줄무늬 빛), `match3/fx_line_v.png`(세로 줄무늬 빛), `match3/fx_bomb.png`(동그란 반짝 테두리)
- `match3/rainbow.png`: 무지개 사탕 특수 타일
- `match3/slot.png`: 타일 자리 바탕(연한 반투명 둥근 사각형)

### 시트 26. 방울 쏘기 (퍼즐버블 방식)
- 방울 6색, 같은 크기 원형, 작은 하이라이트. **색을 못 구분하는 아이도 알 수 있게 방울 안에 작은 모양을 넣는다**: `bubble/red.png`(하트), `yellow.png`(별), `green.png`(잎), `blue.png`(물방울), `purple.png`(달), `orange.png`(동그라미)
- `bubble/pop_01.png` ~ `pop_04.png`: 터지는 4프레임(흰색 위주로 그려서 게임에서 색을 입힌다. 부풀기 → 갈라짐 → 작은 별 조각 → 사라짐)
- `bubble/launcher_base.png`: 바닥에 놓인 귀여운 발사대 받침(토끼가 옆에 설 자리)
- `bubble/launcher_barrel.png`: 발사대 포신만 따로. **똑바로 위를 향하게**, 아래 끝에 받침대에 끼우는 둥근 회전 축(게임에서 포신만 회전)
- `bubble/next_holder.png`: 다음 방울을 올려 두는 작은 받침
- `bubble/ceiling.png`: 위에서 한 칸씩 내려오는 천장 판(가로로 늘려 쓰는 구름/나무판)

### 시트 27. 블록 쌓기 (낙하 블록 방식)
1칸짜리 정사각형 블록. **색마다 다른 작은 무늬**를 넣어 색을 못 구분해도 알 수 있게.
- `blocks/cyan.png`, `yellow.png`, `purple.png`, `green.png`, `pink.png`, `blue.png`, `orange.png` (빨강 대신 분홍)
- `blocks/ghost.png`: 떨어질 자리 표시용 점선 테두리 칸(흰색, 반투명)
- `blocks/clear_flash.png`: 줄이 사라질 때 겹치는 가로 빛줄기(가로로 늘려 쓸 수 있게)

### 시트 28. 그림 스도쿠
아이용이라 숫자 대신 그림을 쓰는 4×4 / 6×6 스도쿠다.
- 동물 얼굴 아이콘 6종(첨부한 동물 카드의 얼굴만, 정사각형에 꽉 차게, 카드 배경 없이): `sudoku/lion.png`, `elephant.png`, `penguin.png`, `rabbit.png`, `fox.png`, `panda.png`
- 같은 6종의 연필 스케치 버전(메모용, 연한 회색 선만): `sudoku/lion_memo.png` …
- `sudoku/btn_bg.png`(그림 고르는 버튼 바탕), `sudoku/btn_bg_selected.png`(고른 상태)

### 시트 29. 네모 나누기 (Shikaku 방식)
- `shikaku/clue_badge.png`: 숫자가 올라갈 동그란 배지(숫자 자리 비움)
- `shikaku/region_fill.png`: 영역을 칠하는 둥근 사각형(흰색 위주, 9-slice. 게임에서 파스텔 색을 입힌다)
- `shikaku/drag_frame.png`: 드래그 중인 네모의 점선 테두리(9-slice)

### 시트 30. 상자 밀기 (Sokoban 방식)
위에서 비스듬히 내려다본 시점. 바닥과 벽은 시트 15 미로 타일을 같이 쓴다. 토끼는 시트 4의 포즈를 쓴다.
- `pushbox/box.png`: 나무 장난감 상자
- `pushbox/box_done.png`: 목표 자리에 들어가서 반짝이는 상자
- `pushbox/goal.png`: 상자를 넣을 자리(바닥에 그려진 별 모양 자리)

### 시트 31~33. 하늘 비행 모험 (탄막 슈팅을 아이용으로 바꾼 횡스크롤 게임)
토끼가 작은 비행기를 타고 **오른쪽으로 날아가며** 비눗방울을 쏴서 장난꾸러기들을 "깨끗하게" 만든다. 적은 사라질 때 터지는 대신 **웃으며 별이나 사탕으로 바뀐다.**
- 시트 31 플레이어와 탄
  - `flight/plane_idle.png`(토끼가 탄 비행기, 오른쪽을 봄), `plane_up.png`(위로 기울기), `plane_down.png`(아래로 기울기), `plane_surprised.png`(맞았을 때 깜짝 놀람)
  - 내 탄: `flight/shot_bubble.png`(비눗방울), `shot_star.png`(강화 별), `shot_small.png`(보조 작은 방울)
- 시트 32 장난꾸러기(모두 왼쪽을 봄)
  - `flight/enemy_cloud.png`(작은 먹구름), `enemy_bee.png`(꿀벌), `enemy_ufo.png`(꼬마 UFO, 방울을 쏨), `enemy_bigcloud.png`(조금 큰 번개구름)
  - 보스 "대장 먹구름" 4포즈(다른 적보다 3배 이상 크게): `flight/boss_idle.png`, `boss_charge.png`(볼 빵빵하게 준비), `boss_hit.png`(움찔), `boss_happy.png`(마지막에 웃는 하얀 구름으로 바뀜)
  - `flight/enemy_happy.png`: 깨끗해져 웃는 작은 구름(적이 사라질 때 공통으로 씀)
- 시트 33 적 탄, 아이템, 배경
  - 적 탄 모양 4종을 **흰색 바탕에 연한 테두리**로(게임에서 색을 입힌다): `flight/ebullet_small.png`(작은 원), `ebullet_big.png`(큰 원), `ebullet_oval.png`(타원), `ebullet_star.png`(별)
  - 아이템 4종(글자 없이 그림으로): `flight/item_power.png`(번개), `item_bomb.png`(폭죽), `item_heart.png`(하트), `item_score.png`(사탕)
  - `flight/bg_far.png`, `flight/bg_near.png`: 가로로 이어지는 배경 2층(먼 하늘 구름, 가까운 숲과 마을 지붕). **좌우 seamless**, 1920×1080, 가까운 층은 아랫부분만 그리고 나머지는 투명

---

# 제작 순서

시트 0 → 1~4(캐릭터) → 5~8(공통 UI) → 20~21(메뉴 아이콘, 숫자 합치기) → 25(과일 맞추기) → 24(벽돌) → 22~23 → 26~27 → 12~19(기존 게임 보강) → 9~11(배경) → 28~30 → 31~33(하늘 비행)
