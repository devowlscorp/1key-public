Unicode true
; 1Key 설치 파일 (2026-10-02 사용자 결정: 설치 형태 + 제거 항목 등록, Defender 탐지 대응 — 실행 위치를 고정해 자동 실행을 다시 등록하지 않게)
;   makensis /DVERSION=0.2.96 /DSRC=..\build\0.2.96\1Key.exe /DOUT=..\build\0.2.96\1Key-Setup-0.2.96.exe /DICON=..\src\OneKey\app.ico 1Key.nsi
; build-aot.ps1 이 빌드 뒤에 부른다.
; - 사용자별 설치(관리자 권한 없음): %LOCALAPPDATA%\Programs\1Key\1Key.exe. 위치는 고르지 않는다(자동 실행 경로가 늘 같게).
; - 1Key 가 실행 중이면 설치·제거하지 않고 종료를 부탁한다(강제로 끄지 않는다).
; - 제거: 프로그램·바로가기·제거 항목, 그리고 이 설치 위치를 가리키는 자동 실행(시작 프로그램 폴더 바로 가기·예전 Run 값·예약 작업)만. 다른 위치를 가리키는 등록은 두지 않는다.
;   설정·비밀번호 폴더(%LOCALAPPDATA%\1Key)는 물어서 지운다(기본 아니요). 조용한 제거(/S)는 남긴다.
!ifndef VERSION
  !error "VERSION missing: makensis /DVERSION=x.y.z /DSRC=...\1Key.exe /DOUT=...\1Key-Setup-x.y.z.exe /DICON=...\app.ico 1Key.nsi"
!endif
!ifndef SRC
  !error "SRC missing: makensis /DVERSION=x.y.z /DSRC=...\1Key.exe /DOUT=...\1Key-Setup-x.y.z.exe /DICON=...\app.ico 1Key.nsi"
!endif
!ifndef OUT
  !error "OUT missing: makensis /DVERSION=x.y.z /DSRC=...\1Key.exe /DOUT=...\1Key-Setup-x.y.z.exe /DICON=...\app.ico 1Key.nsi"
!endif
!ifndef ICON
  !error "ICON missing: makensis /DVERSION=x.y.z /DSRC=...\1Key.exe /DOUT=...\1Key-Setup-x.y.z.exe /DICON=...\app.ico 1Key.nsi"
!endif
; 시험용 변형: /DTESTSUFFIX=.t 이면 설치 위치·제거 항목·바로가기·자동 실행 이름·설정 폴더가 모두 접미사를 달고, 실행 중 확인을 건너뛴다
; (실제 설치본·실제 설정에 닿지 않는 제거 시험, Codex 11:20). 배포용은 접미사 없음.
!ifndef TESTSUFFIX
  !define TESTSUFFIX ""
!endif
!define APPNAME "1Key${TESTSUFFIX}"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
!define RUNKEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUNVALUE "${APPNAME}"
!define TASKNAME "1Key_AutoStart${TESTSUFFIX}"
!define CFGDIR "$LOCALAPPDATA\${APPNAME}"
; 자동 실행(0.2.149~) = 사용자 시작 프로그램 폴더의 바로 가기 "1Key.lnk"(설치본, --tray). 예전 Run 값·예약 작업은 설치 때 이것으로 옮긴다.
; 시험 변형은 실제 시작 프로그램 폴더를 절대 쓰지 않는다: /DTESTSTARTUP=<시험 폴더> 가 꼭 있어야 하고, 실행 때 실제 폴더와 같으면 멈춘다(Codex D7·R-A2).
!if "${TESTSUFFIX}" == ""
  !define STARTDIR "$SMSTARTUP"
!else
  !ifndef TESTSTARTUP
    !error "TESTSTARTUP missing: a test build (TESTSUFFIX) must get its own startup folder, never the real one"
  !endif
  !define STARTDIR "${TESTSTARTUP}"
!endif
!define STARTLNK "${STARTDIR}\${APPNAME}.lnk"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "TextFunc.nsh"

Name "${APPNAME}"
OutFile "${OUT}"
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show
SetCompressor /SOLID lzma
BrandingText "1Key ${VERSION}"
!ifndef VERNUM
  !define VERNUM "${VERSION}"
!endif
VIProductVersion "${VERNUM}.0"   ; VERNUM = 숫자만(시험 빌드 0.3.131-A → 0.3.131)
VIAddVersionKey /LANG=1042 "ProductName" "1Key"
VIAddVersionKey /LANG=1042 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1042 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1042 "FileDescription" "1Key 설치"
VIAddVersionKey /LANG=1042 "LegalCopyright" "1Key"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "1Key ${VERSION} 설치"
!define MUI_WELCOMEPAGE_TEXT "1Key 를 이 PC 의 사용자 폴더에 설치합니다.$\r$\n$\r$\n설치 위치: $LOCALAPPDATA\Programs\1Key$\r$\n$\r$\n이미 설치돼 있으면 새 버전으로 바꿔 끼웁니다. 설정과 저장한 비밀번호는 그대로입니다.$\r$\n$\r$\n1Key 가 실행 중이면 트레이 아이콘 메뉴에서 [종료]한 뒤 설치하세요."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "설치 완료"
!define MUI_FINISHPAGE_RUN "$INSTDIR\1Key.exe"
!define MUI_FINISHPAGE_RUN_TEXT "1Key 실행"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "바탕화면에 바로가기 만들기"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION DesktopShortcut
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Korean"

; 1Key 가 실행 중인가(관리자 권한으로 실행 중이어도 이름은 보인다). 실행 중이면 다시 시도 / 취소
!macro CheckRunning PREFIX
Function ${PREFIX}CheckRunning
  !if "${TESTSUFFIX}" == ""
  check:
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq 1Key.exe" /NH | find /I /C "1Key.exe"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    ${WordFind} "$1" " " "+1" $2
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "1Key 가 실행 중입니다(실행 중인 1Key.exe $2개).$\r$\n$\r$\n트레이 아이콘 메뉴에서 [종료]한 뒤 [다시 시도]를 누르세요. 다른 폴더에서 실행한 1Key 일 수도 있습니다(이름으로 확인합니다)." IDRETRY check
    Abort
  ${EndIf}
  !endif
FunctionEnd
!macroend
!insertmacro CheckRunning ""
!insertmacro CheckRunning "un."

; 시험 변형: 시작 프로그램 폴더가 실제 폴더이면 아무것도 하지 않고 멈춘다
!macro GuardStartup
  !if "${TESTSUFFIX}" != ""
  ${If} "${STARTDIR}" == "$SMSTARTUP"
    Abort
  ${EndIf}
  !endif
!macroend

Function .onInit
  !insertmacro GuardStartup
  Call CheckRunning
FunctionEnd

Function un.onInit
  !insertmacro GuardStartup
  Call un.CheckRunning
FunctionEnd

; 명령 줄에서 실행 파일 경로만: "\"C:\a b\1Key.exe\" --tray" -> C:\a b\1Key.exe, 따옴표가 없으면 " --" 앞까지(1Key 의 ExeOf 와 같은 규칙)
!macro ExeOfCommandFn PREFIX
Function ${PREFIX}ExeOfCommand
  Exch $0
  Push $1
  Push $2
  StrCpy $1 $0 1
  ${If} $1 == '"'
    StrCpy $0 $0 "" 1
    ${WordFind} '$0' '"' "+1{" $0
  ${Else}
    ${WordFind} '$0' " --" "+1{" $2
    ${If} $2 != $0
      StrCpy $0 $2
    ${EndIf}
  ${EndIf}
  Pop $2
  Pop $1
  Exch $0
FunctionEnd
!macroend
!insertmacro ExeOfCommandFn ""
!insertmacro ExeOfCommandFn "un."

; 예약 작업이 가리키는 실행 파일(없거나 읽지 못하면 빈 글). XML 의 <Command> 줄만 받는다(전체 XML 은 NSIS 문자열보다 길다)
!macro TaskExeFn PREFIX
Function ${PREFIX}TaskExe
  Push $0
  Push $1
  nsExec::ExecToStack 'cmd /c schtasks /Query /TN "${TASKNAME}" /XML | findstr /I /C:"<Command>"'
  Pop $0
  Pop $1
  ${If} $0 == 0
    ${WordFind2X} "$1" "<Command>" "</Command>" "+1" $1
    ${WordReplace} "$1" "&quot;" '"' "+" $1
    ${WordReplace} "$1" "&amp;" "&" "+" $1
    Push $1
    Call ${PREFIX}ExeOfCommand
    Pop $1
  ${Else}
    StrCpy $1 ""
  ${EndIf}
  StrCpy $0 $1
  Pop $1
  Exch $0
FunctionEnd
!macroend
!insertmacro TaskExeFn ""
!insertmacro TaskExeFn "un."

; 설치·제거 중 쓰는 도우미 스크립트를 임시 폴더에 푼다(끝나면 지워진다)
!macro PrepHelperFn PREFIX
Function ${PREFIX}PrepHelper
  InitPluginsDir
  ${IfNot} ${FileExists} "$PLUGINSDIR\cleanup-old.ps1"
    File "/oname=$PLUGINSDIR\cleanup-old.ps1" "cleanup-old.ps1"
  ${EndIf}
FunctionEnd
!macroend
!insertmacro PrepHelperFn ""
!insertmacro PrepHelperFn "un."

; 시작 프로그램 폴더의 자동 실행 바로 가기 상태(읽기만): missing / ours(설치본, --tray) / oursdiff(설치본, 인자 다름) / other / unknown.
; PowerShell 을 쓸 수 없으면 unknown(그러면 바로 가기도 예전 등록도 건드리지 않는다).
!macro LinkStateFn PREFIX
Function ${PREFIX}LinkState
  Push $0
  Push $1
  Call ${PREFIX}PrepHelper
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\cleanup-old.ps1" -Keep "$INSTDIR\1Key.exe" -Link "${STARTLNK}"'
  Pop $0
  Pop $1
  ${TrimNewLines} "$1" $1
  ${If} $0 != 0
    StrCpy $1 "unknown"
  ${EndIf}
  StrCpy $0 $1
  Pop $1
  Exch $0
FunctionEnd
!macroend
!insertmacro LinkStateFn ""
!insertmacro LinkStateFn "un."

; 자동 실행 바로 가기가 설치본을 가리키게 한다. 이미 맞으면 그대로, 없거나 우리 것인데 다르면 만들고 다시 읽어 확인한다.
; 같은 이름의 다른 바로 가기·읽지 못한 것은 건드리지 않는다. 결과: 1 = 확인됨, 0 = 아님.
Function EnsureLink
  Push $0
  Call LinkState
  Pop $0
  ${If} $0 == "missing"
  ${OrIf} $0 == "oursdiff"
    CreateDirectory "${STARTDIR}"
    SetOutPath "$INSTDIR"
    ClearErrors
    CreateShortcut "${STARTLNK}" "$INSTDIR\1Key.exe" "--tray" "$INSTDIR\1Key.exe" 0 SW_SHOWNORMAL
    Call LinkState
    Pop $0
  ${EndIf}
  ${If} $0 == "ours"
    StrCpy $0 1
  ${Else}
    DetailPrint "자동 실행 바로 가기를 만들거나 확인하지 못했습니다(상태: $0)."
    StrCpy $0 0
  ${EndIf}
  Exch $0
FunctionEnd

; 실행 파일이 1Key 인가: 설치본이거나(대소문자 무시), 파일이 있고 파일 정보(제품명·회사)가 1Key. 결과 "1Key" / "other".
; 확인할 수 없으면(PowerShell 을 쓸 수 없음 포함) "other"(2026-10-02 Codex 13:05 규칙).
Function IsOneKeyExe
  Exch $0
  Push $1
  Push $2
  ${If} $0 == "$INSTDIR\1Key.exe"
    StrCpy $0 "1Key"
  ${Else}
    Call PrepHelper
    nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\cleanup-old.ps1" -Keep "$INSTDIR\1Key.exe" -Check "$0"'
    Pop $1
    Pop $2
    ${TrimNewLines} "$2" $2
    ${If} $1 == 0
    ${AndIf} $2 == "1Key"
      StrCpy $0 "1Key"
    ${Else}
      StrCpy $0 "other"
    ${EndIf}
  ${EndIf}
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; 예전 자동 실행(Run 값 "1Key", 예약 작업 "1Key_AutoStart")을 시작 프로그램 폴더 바로 가기로 옮긴다(0.2.149, Codex D4·D8·R-A4).
; - 1Key 를 가리키는 등록만 옮긴다. 다른 프로그램·없는 파일·확인할 수 없는 것은 그대로 둔다.
; - 바로 가기를 만들고 다시 읽어 대상·인자가 맞을 때만 예전 등록을 지운다. 바로 가기가 실패하면 예전 등록을 그대로 둔다(자동 실행이 끊기지 않게).
; - 관리자 권한으로 만든 예약 작업은 이 설치 파일(일반 권한)이 지우지 못할 수 있다. 남으면 알리고 지우는 법을 적는다.
; - 예전 등록이 없으면 바로 가기를 만들지 않는다(자동 실행은 사용자가 설정에서 켠다).
; 예전 파일 정리보다 먼저 한다: 정리에서 예전 파일을 휴지통으로 옮겨도 자동 실행이 끊기지 않게.
Function MigrateAutostart
  ReadRegStr $0 HKCU "${RUNKEY}" "${RUNVALUE}"
  ${If} $0 != ""
    Push $0
    Call ExeOfCommand
    Pop $1
    Push $1
    Call IsOneKeyExe
    Pop $2
    ${If} $2 == "1Key"
      Call EnsureLink
      Pop $3
      ${If} $3 == 1
        DeleteRegValue HKCU "${RUNKEY}" "${RUNVALUE}"
        ReadRegStr $4 HKCU "${RUNKEY}" "${RUNVALUE}"
        ${If} $4 == ""
          DetailPrint "자동 실행(Run 값, 전: $1)을 시작 프로그램 폴더 바로 가기로 옮겼습니다."
        ${Else}
          DetailPrint "바로 가기는 만들었지만 예전 자동 실행(Run 값)을 지우지 못했습니다."
          ${IfNot} ${Silent}
            MessageBox MB_ICONEXCLAMATION "Windows 시작 시 자동 실행을 새 방식(시작 프로그램 폴더 바로 가기)으로 옮겼지만, 예전 등록(레지스트리 Run 값)을 지우지 못했습니다.$\r$\n$\r$\n그대로 두면 Windows 시작 때 1Key 가 두 번 실행될 수 있습니다. 작업 관리자 › 시작 앱에서 예전 1Key 항목을 꺼 주세요."
          ${EndIf}
        ${EndIf}
      ${Else}
        DetailPrint "바로 가기를 확인하지 못해 예전 자동 실행(Run 값)을 그대로 둡니다."
      ${EndIf}
    ${Else}
      DetailPrint "자동 실행(Run 값)은 그대로 둡니다(1Key 가 아니거나 확인할 수 없음: $1)."
    ${EndIf}
  ${EndIf}

  Call TaskExe
  Pop $1
  ${If} $1 != ""
    Push $1
    Call IsOneKeyExe
    Pop $2
    ${If} $2 == "1Key"
      Call EnsureLink
      Pop $3
      ${If} $3 == 1
        nsExec::ExecToStack 'schtasks /Delete /TN "${TASKNAME}" /F'
        Pop $4
        Pop $4
        Call TaskExe
        Pop $4
        ${If} $4 == ""
          DetailPrint "자동 실행 작업(${TASKNAME}, 전: $1)을 시작 프로그램 폴더 바로 가기로 옮겼습니다."
        ${Else}
          DetailPrint "바로 가기는 만들었지만 예전 자동 실행 작업(${TASKNAME})을 지우지 못했습니다."
          ${IfNot} ${Silent}
            MessageBox MB_ICONEXCLAMATION "Windows 시작 시 자동 실행을 새 방식(시작 프로그램 폴더 바로 가기)으로 옮겼지만, 예전 작업(${TASKNAME})은 관리자 권한으로 만든 것이라 지우지 못했습니다.$\r$\n$\r$\n그대로 두면 Windows 시작 때 1Key 가 두 번 실행될 수 있습니다. 작업 스케줄러를 관리자 권한으로 열어 '${TASKNAME}' 작업을 삭제해 주세요."
          ${EndIf}
        ${EndIf}
      ${Else}
        DetailPrint "바로 가기를 확인하지 못해 예전 자동 실행 작업(${TASKNAME})을 그대로 둡니다."
      ${EndIf}
    ${Else}
      DetailPrint "자동 실행 작업은 그대로 둡니다(1Key 가 아니거나 확인할 수 없음: $1)."
    ${EndIf}
  ${EndIf}
FunctionEnd

; 예전에 받아 쓰던 1Key 단일 파일(다운로드·바탕 화면·%LOCALAPPDATA%\1Key·자동 실행이 가리키던 폴더의 1Key*.exe, 제품명 1Key, 설치본 제외)을
; 찾아 보여 주고, 사용자가 [예]를 누르면 휴지통으로 옮긴다(2026-10-02 사용자 결정). 설정 파일은 건드리지 않는다. PowerShell 이 막혀 있으면 건너뛴다.
Function CleanupOld
  Call PrepHelper
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\cleanup-old.ps1" -Keep "$INSTDIR\1Key.exe" -Out "$PLUGINSDIR\old.txt" -StartLink "${STARTLNK}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
  ${OrIfNot} ${FileExists} "$PLUGINSDIR\old.txt"
    DetailPrint "예전 1Key 파일 찾기를 건너뜁니다(PowerShell 을 쓸 수 없음)."
    Return
  ${EndIf}
  FileOpen $2 "$PLUGINSDIR\old.txt" r
  FileReadUTF16LE $2 $3
  ${TrimNewLines} "$3" $3
  StrCpy $4 ""
  StrCpy $5 0
  ${Do}
    FileReadUTF16LE $2 $6
    ${If} ${Errors}
      ${ExitDo}
    ${EndIf}
    ${TrimNewLines} "$6" $6
    ${If} $6 == ""
      ${Continue}
    ${EndIf}
    IntOp $5 $5 + 1
    ${If} $5 <= 8
      StrCpy $4 "$4$\r$\n  $6"
    ${EndIf}
  ${Loop}
  FileClose $2
  ${If} $3 == "hold"
    DetailPrint "자동 실행 등록을 확인하지 못해 예전 1Key 파일 정리를 건너뜁니다(등록이 가리키는 파일을 지키기 위해)."
    Return
  ${EndIf}
  ${If} $3 == "0"
  ${OrIf} $3 == ""
    DetailPrint "예전 1Key 파일은 없습니다."
    Return
  ${EndIf}
  ${If} $5 > 8
    StrCpy $4 "$4$\r$\n  … 외 더 있음"
  ${EndIf}
  MessageBox MB_YESNO|MB_ICONQUESTION "파일 정보가 1Key 인 예전 파일 $3개를 찾았습니다(전체 경로).$\r$\n$4$\r$\n$\r$\n휴지통으로 옮길까요? (나중에 휴지통에서 되살릴 수 있습니다. 설정과 저장한 비밀번호는 그대로입니다.)" IDNO keepold
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\cleanup-old.ps1" -Keep "$INSTDIR\1Key.exe" -Recycle "$PLUGINSDIR\old.txt" -StartLink "${STARTLNK}"'
  Pop $0
  Pop $1
  ${TrimNewLines} "$1" $1
  ${If} $0 != 0
    StrCpy $1 "0"
  ${EndIf}
  DetailPrint "예전 1Key 파일 $3개 중 $1개를 휴지통으로 옮겼습니다."
  ${If} $1 != $3
    MessageBox MB_OK|MB_ICONINFORMATION "예전 1Key 파일 $3개 중 $1개를 휴지통으로 옮겼습니다. 나머지는 옮기지 못했으니(사용 중이거나 바뀌었거나 권한 없음) 필요하면 직접 지우세요."
  ${EndIf}
  Return
  keepold:
  DetailPrint "예전 1Key 파일은 그대로 두었습니다."
FunctionEnd

Function DesktopShortcut
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\1Key.exe"
FunctionEnd

Section "1Key"
  SetOutPath "$INSTDIR"
  File "/oname=1Key.exe" "${SRC}"
  ; 자동 실행 등록 도구(0.3.103 시험 A, 2026-10-06 사용자): 1Key 설정의 스위치를 바꾸고 [저장]하면 1Key 가 이 스크립트를 연다.
  ; 설치 파일은 등록하지 않는다(설치 파일이 만들면 탐지됨 — 0.2.154).
  SetOutPath "$INSTDIR\tools"
  File "..\tools\Register-1Key-AdminStartup.cmd"
  File "..\tools\Register-1Key-Startup.cmd"
  File "..\tools\Remove-1Key-Startup.cmd"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\1Key.exe"
  ; 설정 › 앱 (제거 항목)
  WriteRegStr HKCU "${UNKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNKEY}" "Publisher" "1Key"
  WriteRegStr HKCU "${UNKEY}" "DisplayIcon" "$INSTDIR\1Key.exe"
  WriteRegStr HKCU "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNKEY}" "EstimatedSize" "$0"
  ; 자동 실행은 잠시 중단한다(2026-10-04 사용자 결정): 1Key 앱·설치 프로그램 어느 쪽이 시작 프로그램 바로 가기를 만들어도 그 순간
  ; Defender 가 탐지했다(0.2.150·0.2.152·0.2.154). Microsoft 오탐 분석 판정이 날 때까지 설치는 자동 실행을 만들지도, 예전 등록을
  ; 옮기거나 지우지도 않는다(MigrateAutostart 를 부르지 않음). 제거는 예전처럼 이 설치본을 가리키는 등록을 정리한다.
  !if "${TESTSUFFIX}" == ""
  ${IfNot} ${Silent}
    Call CleanupOld
  ${EndIf}
  !endif
  ; 작업 표시줄에 고정한 1Key 아이콘을 새 그림으로(0.5.15-N, 2026-10-09 사용자: 설치해도 작업 표시줄 아이콘이 안 바뀜): 같은 경로의 exe 아이콘이
  ; 바뀌어도 고정 항목은 캐시해 둔 그림을 계속 쓴다. 셸에 '이 파일이 바뀜' + '아이콘 연결 바뀜'을 알려 다시 읽게 한다(설정을 바꾸지 않는다)
  System::Call 'shell32::SHChangeNotify(i 0x2000, i 0x5, w "$INSTDIR\1Key.exe", p 0)'
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0x1000, p 0, p 0)'
SectionEnd

Section "Uninstall"
  ; 시작 프로그램 폴더의 자동 실행 바로 가기(0.2.149): 대상이 이 설치본일 때만 지운다. 다른 대상·읽지 못한 것은 그대로 둔다(Codex D8).
  Call un.LinkState
  Pop $0
  ${If} $0 == "ours"
  ${OrIf} $0 == "oursdiff"
    Delete "${STARTLNK}"
    ${If} ${FileExists} "${STARTLNK}"
      DetailPrint "자동 실행 바로 가기를 지우지 못했습니다: ${STARTLNK}"
      ${IfNot} ${Silent}
        MessageBox MB_ICONEXCLAMATION "Windows 시작 시 자동 실행 바로 가기를 지우지 못했습니다.$\r$\n${STARTLNK}$\r$\n$\r$\n이 파일을 직접 지우거나 작업 관리자 › 시작 앱에서 1Key 를 꺼 주세요."
      ${EndIf}
    ${Else}
      DetailPrint "자동 실행 바로 가기를 지웠습니다."
    ${EndIf}
  ${ElseIf} $0 == "other"
    DetailPrint "시작 프로그램 폴더의 같은 이름 바로 가기는 다른 파일을 가리켜 그대로 둡니다."
  ${ElseIf} $0 == "unknown"
    DetailPrint "자동 실행 바로 가기를 확인하지 못해 그대로 둡니다: ${STARTLNK}"
  ${EndIf}

  ; 자동 실행: 등록된 실행 파일이 정확히 이 설치본($INSTDIR\1Key.exe, 대소문자 무시)일 때만 지운다(1Key 자신이 아니라 제거 프로그램이 지운다).
  ; 다른 위치를 가리키는 등록은 그대로 둔다. 지운 뒤 다시 읽어 남았으면 알린다.
  ReadRegStr $0 HKCU "${RUNKEY}" "${RUNVALUE}"
  ${If} $0 != ""
    Push $0
    Call un.ExeOfCommand
    Pop $0
    ${If} $0 == "$INSTDIR\1Key.exe"
      DeleteRegValue HKCU "${RUNKEY}" "${RUNVALUE}"
      DetailPrint "자동 실행(Run 값)을 지웠습니다."
    ${Else}
      DetailPrint "자동 실행(Run 값)은 다른 위치($0)를 가리켜 그대로 둡니다."
    ${EndIf}
  ${EndIf}
  Call un.TaskExe
  Pop $0
  ${If} $0 == "$INSTDIR\1Key.exe"
    nsExec::ExecToStack 'schtasks /Delete /TN "${TASKNAME}" /F'
    Pop $3
    Pop $4
    Call un.TaskExe
    Pop $1
    ${If} $1 != ""
      DetailPrint "자동 실행 작업(${TASKNAME})이 남았습니다: $1"
      ${IfNot} ${Silent}
        MessageBox MB_ICONEXCLAMATION "Windows 시작 시 자동 실행 작업(${TASKNAME})을 지우지 못했습니다.$\r$\n가리키는 파일: $1$\r$\n$\r$\n관리자 권한으로 만든 작업이라 그럴 수 있습니다. 작업 스케줄러(taskschd.msc)에서 ${TASKNAME} 을 지우세요. 파일이 없으면 Windows 시작 때 아무 일도 하지 않습니다."
      ${EndIf}
    ${Else}
      DetailPrint "자동 실행 작업(${TASKNAME})을 지웠습니다."
    ${EndIf}
  ${ElseIf} $0 != ""
    DetailPrint "자동 실행 작업은 다른 위치($0)를 가리켜 그대로 둡니다."
  ${EndIf}

  Delete "$INSTDIR\1Key.exe"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$INSTDIR\tools\Register-1Key-AdminStartup.cmd"
  Delete "$INSTDIR\tools\Register-1Key-Startup.cmd"
  Delete "$INSTDIR\tools\Remove-1Key-Startup.cmd"
  RMDir "$INSTDIR\tools"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$DESKTOP\${APPNAME}.lnk"
  DeleteRegKey HKCU "${UNKEY}"

  ${IfNot} ${Silent}
  ${AndIf} ${FileExists} "${CFGDIR}\*.*"
    MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "설정과 저장한 비밀번호도 지울까요?$\r$\n(${CFGDIR})$\r$\n$\r$\n[아니요]를 고르면 남겨 둡니다. 다시 설치하면 그대로 씁니다." IDNO keep
    RMDir /r "${CFGDIR}"
    keep:
  ${EndIf}
SectionEnd
