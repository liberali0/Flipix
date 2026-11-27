# Flipix Workshop 앱 (src/ImageConverter.App)

- 위쪽 "문의하기" 버튼: 기본 브라우저로 개발자 X 프로필(https://x.com/Liberali_0)을 연다.
- WPF (.NET 10), 로직은 `ImageConverter.Core` (플랫폼 비의존)에 있다.
- 기능: 드래그앤드롭/파일·폴더 추가(하위 폴더 재귀), 변환 큐, 전체/항목별 진행률, 병렬 처리(동시 수 조절),
  대용량(128MB+) 파일 동시 1개 제한, 일시정지/재개/취소, 실패 항목 재시도, 충돌 정책(번호/덮어쓰기/건너뛰기), 지정 폴더 + 하위 구조 유지.
- **엔진 연결 지점**: `Services/EngineFactory.cs`. `EngineAdapter`가 `ImageConverter.Engine`(Magick.NET)을 앱 큐 계약 `IConversionEngine`에 맞춘다. 손실/투명 채움 등 엔진 경고는 항목의 결과 칸에 한국어로 표시하고, [변환 후 픽셀 검증]을 켜면 저장 결과를 다시 읽어 원본과 비교한다.
- **설정 파일(우클릭 메뉴와 공유)**: `%LOCALAPPDATA%\Flipix\settings.json` (v0.1.4까지 쓰던 `QuickImageConverter` 폴더의 설정은 처음 실행할 때 복사해 옴)
  - `quickMenu.formats`(포맷 id 목록, 순서 = 메뉴 순서), `quickMenu.showDetails`, `quickMenu.deleteOriginal`(기본 false, true면 우클릭 변환 성공 후 원본을 휴지통으로), `quickMenu.moveOriginal` + `quickMenu.moveOriginalTo`(true이고 폴더가 있으면 성공한 원본을 그 폴더로 옮김, deleteOriginal보다 우선). 셸 확장은 이 섹션만 읽는다. 포맷 id/확장자/손실 여부는 `FormatCatalog`가 기준.
- **명령줄**: `Flipix.exe <경로...|@목록파일> [--to <포맷id>]` (목록파일은 UTF-8, 한 줄에 경로 하나) — 우클릭 [자세히]에서 앱을 열 때 사용(경로는 파일/폴더 모두 가능).
