# 1Key

비밀번호·자주 쓰는 문장을 단축키 한 번으로 입력해 주는 Windows 11용 작은 프로그램입니다.
순수 Win32(C#, NativeAOT)로 만들어 런타임 설치 없이 exe 하나로 동작합니다.

## 주요 기능

- 내용(비밀번호·문장)을 99개까지 저장하고 항목마다 단축키 지정
- 단축키를 누르면 지금 커서가 있는 칸에 입력(특수문자·대소문자 그대로). 입력 방식 5종(자동 / 스캔코드 / 유니코드 / 창 메시지 / 클립보드)
- 웹사이트·윈도우 프로그램의 입력칸에 항목을 연결해 아이디·비밀번호를 한 번에 채우기
- 목록에서 넣기: 행의 [입력] → 화면 위 작은 입력 칩 → 넣을 칸을 클릭하고 확정
- 마스터 비밀번호로 잠금(Argon2id), 자동 잠금, 백업·복원
- 잠금 위젯과 작업 표시줄 위 고양이 마스코트(선택). 고양이는 마우스 쪽을 쳐다보고, 매시 정각에는 플립시계를 띄웁니다. Windows 테마에 따라 밝은 회색 고양이 / 검은 고양이로 바뀝니다
- 한국어 · English · 简体中文 · 日本語 · Tiếng Việt

## 빌드

준비(한 번만): .NET 8 SDK, Visual Studio 2022 Build Tools("C++ 데스크톱 개발" + Windows 11 SDK). 설치 파일까지 만들려면 NSIS.

```
powershell -ExecutionPolicy Bypass -File build-aot.ps1
```

결과물: `build\<버전>\1Key.exe` 와 `build\<버전>\1Key-Setup-<버전>.exe`.

## 폴더

- `src/OneKey` — 프로그램 소스. 그림 자원은 `Assets/cat`, 글꼴은 `Fonts`(Pretendard, OFL 1.1 — `Fonts/OFL-Pretendard.txt`)
- `installer` — NSIS 설치 스크립트
- `tools/tests` — 창을 띄워 실제로 눌러 보는 시험 스크립트(시험용 설정 폴더만 씀: `ONEKEY_TEST=1`, `ONEKEY_CONFIG_DIR`, `ONEKEY_INSTANCE_SUFFIX`)
- `tools/i18n` — `src/OneKey/i18n/strings.tsv` 로 문구 코드를 만드는 생성기

## 알림

- 마스코트 고양이 그림은 AI(Codex)로 만든 그림입니다.
- 코드 서명이 없는 exe 라 처음 실행할 때 SmartScreen 경고가 나올 수 있습니다.
