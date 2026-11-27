# 공개용 저장소 만들기 (2026-11-27 개발자 결정: 새 저장소에 마지막 상태만, 취업 준비 문서는 빼고)
# 지금 저장소(비공개)의 마지막 커밋을 C:\Dev\CivilTwin-public 폴더에 풀고, 아래 파일을 뺀 뒤 커밋 하나로 새 git 저장소를 만든다.
# GitHub에 올리는 것은 이 스크립트가 하지 않는다 (개발자가 "올려 줘"라고 할 때 새 공개 저장소를 만들어 올린다).
#
# 빼는 것:
#   CLAUDE.md, .claude/, skills-lock.json  — 개발 도구 설정 (Windows 사용자 폴더 경로에 실명이 있다)
#   Docs/INTERVIEW.md, Docs/PROJECT_STORY.md — 자소서 초안·예상 질문 (면접관이 미리 보지 않게)
# 실행: powershell -ExecutionPolicy Bypass -File Tools\make-public-snapshot.ps1

$ErrorActionPreference = 'Stop'
$src = 'C:\Dev\CivilTwin'
$dst = 'C:\Dev\CivilTwin-public'
$git = 'C:\Program Files\Git\cmd\git.exe'
$exclude = @('CLAUDE.md', '.claude', 'skills-lock.json', 'Docs\INTERVIEW.md', 'Docs\PROJECT_STORY.md')

if (Test-Path $dst) { throw "$dst 가 이미 있다. 확인한 뒤 지우고 다시 실행한다." }
New-Item -ItemType Directory $dst | Out-Null
$zip = Join-Path $env:TEMP 'civiltwin-public.zip'
& $git -C $src archive --format=zip -o $zip HEAD
Expand-Archive -Path $zip -DestinationPath $dst
Remove-Item $zip
foreach ($e in $exclude) { $p = Join-Path $dst $e; if (Test-Path $p) { Remove-Item -Recurse -Force $p } }

# 남은 파일에 실명·이메일이 없는지 확인 (있으면 멈춘다)
# 검사할 글자는 문자 코드로 만든다 (이 스크립트 안에 실명을 쓰지 않기 위해)
$name = -join [char[]](0xC804, 0xC815, 0xD6C8)
$mailDomain = '@' + 'gmail'
$hits = Get-ChildItem -Recurse -File $dst | Select-String -Pattern $name, $mailDomain -SimpleMatch -List
if ($hits) { $hits | ForEach-Object { $_.Path }; throw '공개본에 실명 또는 이메일이 남아 있다.' }

# README의 INTERVIEW·PROJECT_STORY 링크가 남았는지 확인
$links = Get-ChildItem -Recurse -File $dst -Include *.md | Select-String -Pattern 'INTERVIEW.md', 'PROJECT_STORY.md' -SimpleMatch
# PROGRESS.md는 공개한다 (2026-11-27 개발자 결정). 그 파일은 맨 위에 "INTERVIEW·PROJECT_STORY는 공개본에 없다"고 적어 두었으니 빼고 본다.
$links = $links | Where-Object { $_.Path -notlike '*\Docs\PROGRESS.md' }
if ($links) { '공개본 문서가 뺀 파일을 가리킨다 (고칠 것):'; $links | ForEach-Object { "  $($_.Path):$($_.LineNumber)" } }

& $git -C $dst init -q -b main
& $git -C $dst add -A
$name = & $git -C $src config user.name
$mail = & $git -C $src config user.email   # 지금 저장소와 같은 작성자 (GitHub noreply 이메일)
& $git -C $dst -c user.name="$name" -c user.email="$mail" commit -q -m "CIVIL TWIN — 3D·4D 시공계획 시뮬레이터 (공개본)"
& $git -C $dst log --oneline
"파일 수: " + (& $git -C $dst ls-files | Measure-Object).Count
