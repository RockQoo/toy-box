# 필요한 리소스 목록

현재 리소스는 `art-source/`의 스프라이트 시트 5장과 하늘 배경 1장, 그리고 ChatGPT로 만든 시트(`art-source/chatgpt/`, 요청 프롬프트는 `docs/Daoni_ChatGPT_리소스요청프롬프트.md`)이다. 사운드는 모두 코드로 합성한다(`scripts/Core/SfxSynth.cs`).
아래 리소스가 생기면 `art-source/`에 원본을 넣고, 잘라 쓸 그림은 `assets/`에 영문 이름으로 넣는다. 받으면 체크한다.

## 공통

- [x] **투명 배경 캐릭터**(토끼, 고양이, 판다 등 전신). 기본, 점프, 환호 포즈. 지금은 동물 카드 그림을 써서 스티커처럼 보이고, 고양이는 없다(미로 캐릭터도 같은 그림). → 개선 B-8, MZ-3
  - 2026-10-17 받음: `assets/sprites/buddy/` 토끼·판다·펭귄·사자·고양이 × `idle`/`jump`/`cheer`/`walk`(오른쪽 보기) + 토끼 `throw`/`push`/`back`/`front_walk`/`surprised`/`dizzy`. 아직 게임에 적용 안 함
- [x] **배경 이미지**(가로 1280x720 이상): 우주, 바다, 하늘. 지금은 공원만 이미지이고 나머지는 코드로 그렸다. → 개선 B-7
  - 2026-10-17 받음: `assets/backgrounds/space.png`·`sea.png`·`sky_high.png`(1672×941), 새 퍼즐 게임용 `playroom.png`. 아직 `Scenery`에 적용 안 함. 우주는 밝은 하늘색이라 우주 느낌이 약함
- [ ] **효과음**: 풍선 펑, 카드 뒤집기 "팔락", 짝 성공 "띵동", 미로 발소리·벽 "통"·별 줍기 "반짝", 과일 받기 "뿅", 황금 과일 "띠링", 공룡 점프 "보잉"·장애물 통과 "띵"·넘어짐 "뿌우웅", 버튼, 딩동댕(정답), 문제 등장 "띵동", 다시 고르기 "음?", 시작, 끝, 팡파르. 지금은 합성음이다.
  - 2026-10-17: Kenney CC0 후보는 사용자 마음에 들지 않아 빼고(원본은 `art-source/sounds/kenney/`에 보관), 사용자가 Pixabay에서 받아 `art-source/sounds/pixabay/`에 넣는다. 파일 이름 목록은 그 폴더의 `목록.md`
- [ ] **BGM 4곡**(`bgm_menu`, `bgm_puzzle`, `bgm_action`, `bgm_boss`), 반복 재생용. Pixabay 음악에서 사용자가 받음
- [ ] **둥근 한글 폰트 파일**(무료 라이선스). 지금은 시스템 글꼴이라 안드로이드에서 모양이 달라진다.
- [x] 앱 아이콘 → `ui/app_icon.png`(아직 프로젝트·내보내기 설정에 적용 안 함)
- [x] 타이틀 로고 → 배너 `ui/title_banner.png`에 글자를 올려 쓴다(아직 적용 안 함) → 개선 M-2

## 게임별

- [x] 미로 탈출: **벽·바닥 타일, 출구(문/깃발)**. 지금은 코드로 그린다(`MazeBoard`: 크림색 바닥과 둥근 초록 울타리 벽, `MazeExit`: 금색 매트와 분홍 깃발). 투명 배경 캐릭터(위 공통)도 미로 캐릭터로 쓴다. → 개선 MZ-3, MZ-5
  - 2026-10-17 받음: `assets/sprites/maze/` `floor`(흙길+잔디, 테두리가 있어 이으면 칸 경계가 보임), `wall`(덤불), `fence_h`/`fence_v`/`post`(나무 울타리), `exit`(아치+흰 깃발). 아직 적용 안 함
- [x] 숫자/색깔 퀴즈: **숫자 6~9 카드**(`assets/sprites/quiz/num_6.png`~`num_9.png`, 1~5와 같은 스타일). 지금은 어려움(1~9)에서 6~9를 코드로 그린다(`QuizCard.DrawNumberCard`). 받으면 `QuizCard.PaintedNumbers`를 9로 바꾼다. → 개선 Q-5
  - 2026-10-17 받음: `quiz/num_6`~`num_9`(1~5보다 살짝 넓음). `PaintedNumbers` 변경은 아직 안 함. 색깔 문제용 물감 `quiz/paint_<색>.png` 6색도 받음
- [ ] 동물 짝 맞추기: 동물 이름 음성(또는 울음소리) 8종. 지금은 짝을 찾으면 이름이 글자로만 나온다. → 개선 A-3
- [ ] 숫자/색깔 퀴즈: 문제 읽어 주는 음성("빨간색", "숫자 3" 등, 색 6개 + 숫자 1~9). → 개선 Q-2
- [x] 공룡 점프: **넘어지는 포즈**(엉덩방아 등. 지금은 구르다가 엎드린 그림 `dino_crouch.png`으로 대신함) → 개선 D-3 — 2026-10-17 받음 `dino/dino_bump.png`
- [x] 공룡 점프: **다른 색·종류 공룡**(공룡 선택용, `dino_run`·`dino_jump`·`dino_crouch`와 같은 포즈 세트로. 예: 파랑·분홍 공룡, 트리케라톱스). 지금은 초록 공룡의 피부색만 셰이더로 바꿔 5가지 색을 만든다(`Dino.MaterialFor`) → 개선 D-4 — 2026-10-17 받음 `dino/trike_*`(파랑 트리케라톱스), `dino/stego_*`(분홍 스테고사우루스) × run/jump/crouch/bump. 공중 별 `dino/star_pickup.png`(개선 D-7)도 받음
- [x] 공룡 점프: 이어 붙일 수 있는 땅 타일(양 끝이 잘린 모양). 지금은 `ground.png`를 겹쳐 이어 붙여 이음매가 보인다 → 개선 D-5 — 2026-10-17 받음 `dino/ground_tile.png`(거울 반복으로 깔기)
- [x] 과일 받기: 황금 사과(지금은 동전 `fruit/coin.png`으로 대신 씀) → 개선 F-7, 다른 모양 바구니(지금은 한 그림을 셰이더로 색만 바꿈) → 개선 F-8
  - 2026-10-17 받음: `fruit/golden_apple.png`, `basket_cart`(손수레). `basket_bucket`은 기존 바구니와 거의 같고, `basket_hat`은 모자를 바로 세운 모양이라 받는 그릇으로 안 보여 다시 받아야 함

## 이미 있어서 필요 없는 것

- 동물 짝 맞추기: 동물 카드 8종(사자, 코끼리, 펭귄, 토끼, 여우, 판다, 개구리, 거북이)과 카드 뒷면이 있어 어려움(6쌍)까지 충분하다(구현에 사용).
- 공룡 점프: 달리기·점프·엎드린 공룡, 선인장, 돌, 산, 구름, 땅, 익룡(배경 장식으로 사용), 알(아직 안 씀, 개선 D-7)이 `assets/sprites/dino/`에 있다.

## 새 공통 UI (2026-10-17 받음, 아직 적용 안 함)

- `assets/sprites/ui/`: 버튼 아이콘(`home`, `pause`, `settings`, `next`, `undo`, `hint`, `reset`, `erase`, `memo`, `sound_on`, `sound_off`, `fast`), 터치 버튼(`btn_left`/`right`/`up`/`down`/`rotate`/`drop`/`hold`), 속도 아이콘(`speed_slow` 달팽이·`speed_normal` 자전거·`speed_fast` 로켓 → 개선 B-9, F-5), 판/패널(`board_frame`, `board_bg`, `panel`, `cell_empty`, `cell_select`)
- `assets/sprites/menu/`: 새 게임 12개 메뉴 아이콘

## 새 게임 (기획서 `docs/games/07`~`18`)

- [x] 벽돌깨기 공용(`assets/sprites/brick/`): 벽돌 6단계, 금 간 자국 2단계, 공, 빛나는 공, 공 늘리기 아이템, 구름 받침판, 조준 점·화살표, 바닥 선
  - 벽돌깨기에서 사용(그리기는 `scripts/Games/Breakout/BrickArt.cs`). 벽돌 그림은 152×121로 거의 네모라 판에서는 약 1.7:1로 늘려 그린다(테두리 두께가 가로·세로 조금 다름). `crack_1/2`는 벽돌보다 조금 크고 테두리가 그려져 있어 벽돌보다 4% 크게 겹쳐 그림. `ui/board_frame.png`·`ui/panel.png`·`backgrounds/playroom.png`는 쓰지 않음(판은 코드로 그린 반투명 패널, 배경은 공용 `Scenery` 선택)
  - 공 쏘기 벽돌깨기에서도 사용(2026-10-17): 벽돌은 정사각형 칸이라 늘리지 않고 그린다. `paddle.png`·`ball_glow.png`는 안 씀. 발사대 토끼는 `buddy/rabbit_throw.png`가 큰 비치볼을 들고 있어 게임 공과 안 맞아 `rabbit_throw_empty.png`(두 손 든 포즈) 위에 게임 공을 그렸다. 꽃줄에 닿은 벽돌이 바뀌는 꽃은 그림이 없어 코드로 그림
- [x] 과일 맞추기(`assets/sprites/match3/`): 과일 6종, 무지개 사탕, 타일 자리
- [x] 과일 맞추기: 가로줄·세로줄·폭탄 투명 오버레이 `match3/fx_line_h`·`fx_line_v`·`fx_bomb`(금빛)
- [x] 숫자 합치기: 타일 12단계 `merge/tile_01`~`tile_12`(3과 10이 둘 다 노랑 계열, 7과 12가 보라 계열이라 가까움. 장식 단계는 없음)
- [x] 애벌레: `snake/head`, `head_eat`, `body`, `body_corner`, `tail`, `leaf`. 머리 뒤에 마디가 하나 붙어 있어 칸보다 조금 큼. 바닥 잔디 타일은 가장자리가 울퉁불퉁해 쓰지 않음(코드로 그리거나 다시 받기) — 2026-10-17 사용: 머리는 칸의 1.5배로 그리고 얼굴이 머리 칸에 오도록 0.16칸 뒤로 민다. 위·아래로 90도 돌리면 얼굴이 누워서 최대 약 50도만 기울임. `body_corner`는 보간 이동과 안 맞아 안 씀. 황금 잎 그림이 없어 `leaf`를 셰이더로 금빛 처리. 바닥은 코드 체크무늬
- [x] 토끼 던지기 포즈 공 없는 버전 `buddy/rabbit_throw_empty.png`(두 팔을 옆으로 든 모습이라 환호와 비슷함)
- [x] 방울 쏘기 `bubble/`: 6색(red/yellow/green/blue/purple/orange, 안에 모양), `pop_01`~`04`, `launcher_base`, `launcher_barrel`(위를 향함, 아래 회전 축), `next_holder`, `ceiling`
  - 방울 쏘기에서 사용(2026-10-17, 그리기 `scripts/Games/Bubble/BubbleArt.cs`). `pop_01`~`04`는 흰색·금색이라 앞 두 장에 방울 색을 옅게 입혀 씀. `ceiling`은 좌우로 맞물리지 않지만 판 폭에 두 장이 딱 들어가 그대로 이어 붙이고, 천장이 내려오면 같은 판을 위로 겹쳐 쌓는다(가리비 무늬가 층층이 보임). 토끼는 12번처럼 `rabbit_throw_empty.png`. 꽃줄은 `brick/floor_line.png`, 꽃잎은 12번의 코드 꽃(`FlowerPop`)
- [x] 블록 쌓기 `blocks/`: 7색(cyan 구름, yellow 해, purple 달, green 잎, pink 하트, blue 별, orange 동그라미), `ghost`(점선), `clear_flash`
  - 블록 쌓기에서 사용(`scripts/Games/Blocks/BlocksArt.cs`). `ghost.png`는 흰 점선이라 크림색 판에서 안 보여서 블록 색(조금 어둡게)으로 물들이고 옅은 색 칸을 깔았다. 노란 O 블록의 Ghost는 그래도 옅은 편. 터치 버튼 `ui/btn_left`·`right`·`down`·`rotate`·`drop`·`hold`, 판 테두리 `ui/board_frame.png`(0.36배로 그림), 메뉴 아이콘 `menu/blocks.png` 사용. `ui/board_bg.png`·`ui/panel.png`·`backgrounds/playroom.png`는 쓰지 않음(판은 코드로 그린 패널, 배경은 공용 `Scenery`)
- [x] 그림 스도쿠 `sudoku/`: 동물 얼굴 6종(lion, elephant, penguin, rabbit, fox, panda) + `_memo` 연필 스케치 6종, `btn_bg`, `btn_bg_selected`
  - 그림 스도쿠에서 사용(2026-10-17, `scripts/Games/Sudoku/`). 칸 `ui/cell_empty.png`(처음 동물 칸은 조금 진하게 물들임), 고른 칸 `ui/cell_select.png`, 도구 `ui/erase`·`memo`·`hint`·`undo.png`, 판 테두리 `ui/board_frame.png`(0.5배 NinePatch), 메뉴 아이콘 `menu/picture_sudoku.png`(그림에 개구리가 있는데 게임에는 개구리가 없음). `_memo` 스케치는 연한 회색이라 칸(6x6에서 약 98px) 안 3x2 자리(약 30px)에 그리면 거의 안 보여서 어둡게 물들여 씀. `backgrounds/garden.png`는 쓰지 않음(배경은 공용 `Scenery`, 기본 공원)
- [x] 공룡 땅 `dino/ground_tile.png`(양 끝은 잘렸지만 좌우가 완전히 맞물리지는 않음 → 거울 반복으로 깔기), 미로 잔디 `maze/floor_grass.png`(512, 무늬가 진해서 쓸 때 밝게 낮출 것, 거울 반복)
- [x] 네모 나누기 `shikaku/clue_badge`, `region_fill`(흰 바탕+파란 테두리, 색 입혀 씀), `drag_frame`(점선)
  - 네모 나누기에서 사용(2026-10-17, `scripts/Games/Shikaku/ShikakuBoardView.cs`). `region_fill`은 9-slice(여백 56, 칸 크기에 맞춰 0.22~0.5배)로 늘리고 조각마다 파스텔 색을 곱해 씀(남색 테두리는 그대로 진하게 남음). `drag_frame`은 흰 점선이라 크림색 칸 위에서 옅어서 파랑(맞는 크기면 초록, 힌트는 금색)으로 물들이고 옅은 바탕을 깔았다. 가장자리는 타일 반복(`TileFit`). 칸 `ui/cell_empty.png`, 판 테두리 `ui/board_frame.png`, 도구 `ui/undo`·`reset`·`hint.png`, 메뉴 아이콘 `menu/shikaku.png` 사용. `backgrounds/garden.png`는 쓰지 않음(배경은 공용 `Scenery`, 기본 공원)
- [x] 상자 밀기 `pushbox/box`, `box_done`, `goal`(튀어나온 별 모양이라 미로 별과 헷갈릴 수 있음 → 반투명하게 바닥에 깔아 쓰기)
  - 상자 밀기에서 사용(2026-10-17, `scripts/Games/PushBox/`). `goal`은 칸의 74% 크기, 불투명도 50%로 바닥에 깔았다. `box_done`은 반짝이 때문에 그림이 커서 안의 상자 폭(약 195px)이 `box`(222px)와 같은 크기로 보이게 키워 그림(반짝이는 칸 밖으로 조금 나감). 바닥은 미로 그림 `maze/floor_grass`(칸 2.6개에 한 장, 거울 반복, 크림색 50%를 덮어 밝게. 거울 축에 대칭 무늬 줄이 조금 보임), 안쪽 벽 `maze/wall`(덤불), 바깥 테두리 `maze/fence_h`·`fence_v`(칸 중심을 잇는 난간) + 칸마다 `post`. 토끼 `buddy/rabbit_idle`·`walk`·`push`·`back`·`front_walk`·`cheer`(모두 같은 배율, 왼쪽은 뒤집기), `ui/undo`·`reset`, 메뉴 아이콘 `menu/push_box.png`. 미로 탈출 자체(`MazeBoard`)는 아직 코드 그림 그대로
- [x] 정원 배경 `assets/backgrounds/garden.png`(1672×941)
- [x] 하늘 비행 `flight/`: 비행기 `plane_idle`/`up`/`down`/`surprised`, 탄 `shot_bubble`/`star`/`small`, 적 `enemy_cloud`/`bee`/`ufo`/`bigcloud`, 웃는 구름 `enemy_happy`, 보스 `boss_idle`/`charge`/`hit`/`happy`(적의 약 2.8배)
- [x] 하늘 비행 적 탄 `flight/ebullet_small`/`big`/`oval`/`star`(흰 바탕 + 연한 색 테두리), 아이템 `item_power`(번개)/`item_bomb`(폭죽)/`item_heart`/`item_score`(사탕), 배경 `bg_far`(구름만, 1536×256)·`bg_near`(숲과 마을, 1536×235). 배경은 좌우가 완전히 맞물리지 않아 거울 반복으로 깔기
  - 하늘 비행 모험에서 사용(2026-10-17, `scripts/Games/Flight/`). 배경은 `backgrounds/sky_high.png`를 화면에 꽉 채우고(움직이지 않음) 그 위에 `bg_far`(화면 높이 30%, 숲 뒤로 숨는 높이, 숲의 0.22배 속도)·`bg_near`(22%, 화면 맨 아래)를 한 장씩 좌우로 뒤집어 이어 붙임. 대장은 적과 같은 배율이면 작아서 화면 높이 30%(`boss_idle` 기준 한 배율로 네 그림 모두)로 키움. 적 방울은 흰 바탕이라 분홍·하늘·연보라로 곱해 물들임(테두리도 같이 물듦). `shot_star`(4단계 방울)는 `ui/star.png`(줍는 별)와 거의 같아 보여서 `shot_bubble` 안에 작게 넣어 그림. 보호 방울도 `shot_bubble`을 비행기 위에 32% 불투명도로. 비행기 네 그림은 `plane_idle` 폭 기준 한 배율(얼굴마다 그림 크기가 조금 달라 바뀔 때 살짝 커지거나 작아 보일 수 있음). `item_*`는 뒤에 옅은 빛 원과 반짝이 별을 더함. 대장 등장용 음악·"뽀잉"·"펑-반짝" 전용 합성음은 없어 기존 소리를 씀
- [x] 타이틀 배너 `ui/title_banner.png`(글자 없음), 앱 아이콘 `ui/app_icon.png`(632×620, 모서리 둥근 사각형. 토끼 + 과일 상자)
- [x] 아기새 `flappy/bird_01`~`03`, `bird_bump`(머리에 혹이 있음), `pillar_body`, `pillar_cap`(기둥은 한 그림으로 왔지만 잎 장식과 몸통이 떨어져 있어 자동으로 나뉨) — 아기새 날기에서 사용. `bird_bump`에는 머리 위 별이 그려져 있어 따로 도는 별을 더하지 않음. `pillar_body`는 세로로 늘리지 않고 몸통 띠(위에서 30~180px)를 거울 반복으로 이어 붙이고 아래 풀 뿌리(180~290px)는 끝에 한 번 그림. 땅 `dino/ground_tile.png`은 양 끝 2~3px 열이 투명해서 그대로 거울 반복하면 세로 이음선이 보임 → 양 끝 3px를 잘라 내고 한 장씩 번갈아 뒤집어 그림
- [x] 양동이 `fruit/basket_bucket`, 뒤집은 밀짚모자 `fruit/basket_hat`(입구가 위)
