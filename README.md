# 1Key

비밀번호·자주 쓰는 문장을 단축키 한 번으로 입력해 주는 Windows 11용 작은 프로그램입니다.
순수 Win32(C#, NativeAOT)로 만들어 런타임 설치 없이 exe 하나로 동작합니다.

![목록 화면 — 밝은 테마와 어두운 테마](docs/images/main-list.png)

## 주요 기능

### 단축키 한 번으로 입력

- 내용(비밀번호·문장)을 99개까지 저장하고 항목마다 단축키를 지정합니다.
- 단축키를 누르면 지금 커서가 있는 칸에 입력합니다(특수문자·대소문자 그대로). 입력 방식은 5가지(자동 / 스캔코드 / 유니코드 / 창 메시지 / 클립보드) 중에서 고를 수 있어, 잘 안 먹히는 창에서도 방식을 바꿔 대응합니다.
- 단축키를 외우지 않은 항목은 목록의 [입력]을 누르고, 화면 위에 뜬 작은 입력 칩으로 넣을 칸을 골라 넣습니다.
- 웹사이트·윈도우 프로그램의 입력칸에 항목을 연결하면 아이디와 비밀번호를 한 번에 채웁니다.

### 마스터 비밀번호로 잠금

모든 내용은 마스터 비밀번호(Argon2id)로 잠깁니다. 자동 잠금, 백업·복원을 지원합니다.
잠긴 동안에는 작은 잠금 위젯이 뜨고, 비밀번호를 넣으려고 칸을 누르면 고양이가 "냐옹" 합니다.

![잠금 위젯 — 좁을 때, 칸을 눌러 넓어졌을 때(냐옹), 어두운 테마](docs/images/lock-widget.png)

### 작업 표시줄 고양이 (선택)

설정에서 "트레이 위젯 모드"를 켜고 창을 트레이로 내리면, 작업 표시줄 오른쪽 위에 고양이가 앉아 마우스 쪽을 쳐다봅니다. 누르면 1Key 창이 열립니다.
매시 정각에는 화면 모서리에서 플립시계가 올라와 :59 → :00 으로 넘어가고, 고양이는 시계를 올려다봅니다.
고양이 색은 Windows 테마를 따라 밝은 회색 고양이 / 검은 고양이로 바뀝니다.

![작업 표시줄 고양이와 정시 플립시계](docs/images/taskbar-cat.png)

### 설정

설정은 한 화면에 모여 있고, 바꾼 내용은 [저장]을 눌러야 적용됩니다. Windows 시작 시 자동 실행, 테마(시스템 / 밝게 / 어둡게), 언어(한국어 · English · 简体中文 · 日本語 · Tiếng Việt), 자동 잠금, 입력 전송 키 등을 고릅니다.

<img src="docs/images/settings.png" alt="설정 화면" width="320">

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
- `tools/readme-shots.ps1` — 이 README 의 스크린샷을 예시 항목으로 다시 찍는 스크립트(`docs/images`)

## 알림

- 마스코트 고양이 그림은 AI(Codex)로 만든 그림입니다.
- 스크린샷의 항목과 값은 모두 예시입니다.
- 코드 서명이 없는 exe 라 처음 실행할 때 SmartScreen 경고가 나올 수 있습니다.

## 라이선스

[MIT](LICENSE). 함께 들어 있는 Pretendard 글꼴은 SIL Open Font License 1.1(`src/OneKey/Fonts/OFL-Pretendard.txt`)을 따릅니다.
