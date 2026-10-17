#!/usr/bin/env bash
# 효과음 후보 표(TSV)로 듣기 페이지를 만들고, 1번 후보를 게임 폴더에 복사한다.
# 사용: tools/asset/build_sfx_page.sh <sounds 폴더> <출력 sfx 폴더>
#   <sounds 폴더>/sfx_candidates.tsv : name<TAB>purpose<TAB>cand1<TAB>cand2<TAB>cand3<TAB>note
#   후보 표기 "<묶음>/<파일>" → Kenney 압축을 푼 <sounds 폴더>/kenney/ 아래 경로로 바꾼다(res 참고).
#   후보를 바꾸려면 TSV에서 원하는 후보를 cand1 칸으로 옮기고 다시 실행한다.
set -e
SRC=$1; OUT=$2
cd "$SRC"; mkdir -p "$OUT"
res(){ local p=${1%%/*} f=${1#*/}; case $p in
 interface) echo "kenney/interface-sounds/Audio/$f.ogg";; ui) echo "kenney/ui-audio/Audio/$f.ogg";; digital) echo "kenney/digital-audio/Audio/$f.ogg";;
 impact) echo "kenney/impact-sounds/Audio/$f.ogg";; casino) echo "kenney/casino-audio/Audio/$f.ogg";; rpg) echo "kenney/rpg-audio/Audio/$f.ogg";;
 jingles) local k=${f%%[0-9]*} d; case $k in PIZZI) d="Pizzicato jingles";; STEEL) d="Steel jingles";; NES) d="8-Bit jingles";; SAX) d="Sax jingles";; HIT) d="Hit jingles";; esac
   echo "kenney/music-jingles/Audio/$d/jingles_$f.ogg";;
 file) echo "$f";;   # file/<상대 경로> : Pixabay 등에서 직접 받은 파일
 esac; }
H=listen.html
cat > $H <<'EOF'
<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>효과음 고르기</title>
<style>body{font-family:"Malgun Gothic",sans-serif;margin:16px;background:#fdf8ef;color:#333}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #e5dccb;padding:6px 8px;text-align:left;vertical-align:middle}th{background:#f3e6c8}.pick{background:#fff3b0}.note{color:#c06000;font-size:12px}audio{height:30px;width:200px}code{font-size:12px;color:#777}</style></head><body>
<h2>효과음 후보</h2>
<p>각 소리마다 후보 3개. <b>노란 칸(1번)</b>이 지금 게임 폴더에 들어간 것. 바꾸고 싶으면 "이름: 번호"로 알려 주세요(예: <code>button: 2</code>). 셋 다 별로면 "Pixabay"라고 해 주세요.</p>
<table><tr><th>이름</th><th>용도</th><th>1</th><th>2</th><th>3</th></tr>
EOF
tail -n +2 sfx_candidates.tsv | while IFS=$'\t' read -r name purpose c1 c2 c3 note; do
  row="<tr><td><b>$name</b>"; [ -n "$note" ] && row="$row<div class=note>$note</div>"; row="$row</td><td>$purpose</td>"
  i=0; for c in "$c1" "$c2" "$c3"; do i=$((i+1)); [ -z "$c" ] && { row="$row<td></td>"; continue; }
    p=$(res "$c"); [ -f "$p" ] || echo "MISSING $name $c -> $p" >&2
    cls=""; [ $i = 1 ] && cls=" class=pick"; row="$row<td$cls><audio controls preload=none src=\"${p// /%20}\"></audio><br><code>$c</code></td>"; done
  echo "$row</tr>" >> $H
  p1=$(res "$c1"); cp "$p1" "$OUT/$name.${p1##*.}"
done
echo "</table></body></html>" >> $H
echo "OK: $SRC/$H"
