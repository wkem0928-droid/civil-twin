# README용 화면 (2026-11-20). 첫 현장 = 현대건설 소양강댐.

wait 4
mouse 0.5 0.5
key G                       # 부지
wait 1.5
key E                       # 균형 계획고
wait 2
capture 01_pad_balanced     # 절·성토 색 + 부지 글상자

key T                       # 건물과 장비
wait 1
key F                       # 부지 가까이
wait 2.5
capture 02_building_close

key B                       # 0일
key Space                   # 재생
wait 4
key Space                   # 토공 중간쯤 멈춤
wait 1
capture 03_earthwork_mid

key Space
wait 16
key Space                   # 건물이 올라가는 중
wait 1
capture 04_building_rising

key Tab
key Tab                     # 글상자 모두 숨김
wait 0.5
capture 05_building_clean
key Tab                     # 다시 전체

key P                       # CPM 공정표
wait 1
capture 06_cpm
key P

key F                       # 현장 전체로
wait 2
key T                       # 건물 끄기 (부지 글상자 전체 보기)

# 대안: 트럭 4대(A), 8대(B), 굴착기 8대 + 트럭 8대(C)
key LeftShift+Comma
key LeftShift+Comma
key LeftShift+Comma
key LeftShift+Comma
key Digit1
key LeftShift+Period
key LeftShift+Period
key LeftShift+Period
key LeftShift+Period
key Digit2
key Period
key Period
key Period
key Period
key Digit3
key Comma
key Comma
key Comma
key Comma                   # 다시 굴착기 4대
wait 1
capture 07_table
key O
wait 1
capture 08_graph
key O                       # 비교 화면 닫기

button 장비                 # 위쪽 메뉴를 펼친 모습 (README "조작")
wait 0.5
capture 09_menu
button 장비

quit
