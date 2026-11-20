using System.Reflection;

namespace PersonalUsageOverlay
{
    internal static class PortableReleaseInfo
    {
        public static string VersionText
        {
            get { return "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " 포터블"; }
        }

        public const string Changes =
            "1.2.2\r\n"
            + "• Codex 업데이트 후 세션 제목 아래에 배지가 다시 표시되도록 제목 위치 탐색을 수정했습니다.\r\n"
            + "• 배지 전환 화살표를 상세형에서는 아래쪽, 간결형에서는 위쪽으로 표시합니다.\r\n\r\n"
            + "1.2.1\r\n"
            + "• 배지 렌더링의 레이아웃 정규화와 색상 계산 중복을 줄였습니다.\r\n"
            + "• 설정 창의 화면 구성과 Codex 조회 프로세스의 시작·출력·종료 처리를 정리했습니다.\r\n\r\n"
            + "1.2.0\r\n"
            + "• 로그인된 Claude Code CLI로 현재 세션과 주간 한도를 자동 조회합니다. 래퍼를 통한 Claude Code 호출이 끝나면 한도를 다시 조회합니다.\r\n"
            + "• 상세형과 간결형을 배지의 화살표로 전환할 수 있습니다. 두 형식에 현재 세션·주간 세션 이름과 구독 요금제를 표시합니다.\r\n"
            + "• 상세형에 서비스 아이콘과 잔여율 막대를 표시하고, 초기화 시각과 남은 시간을 오른쪽에 맞췄습니다. 간결형에도 실제 초기화 시각을 오른쪽에 표시합니다.\r\n"
            + "• 조회 기록에 Claude Code 갱신 결과를 추가하고, 설정 창 제목 표시줄 정렬과 여백을 조정했습니다.\r\n\r\n"
            + "1.1.0\r\n"
            + "• 오버레이에 Claude Code 5시간·주간 잔여율, 마지막 수신 시각과 초기화 시각을 표시할 수 있습니다.\r\n"
            + "• Windows용 Claude Desktop의 로컬 Code 세션에서 한도를 전달하는 플러그인을 포터블 ZIP에 포함했습니다.\r\n\r\n"
            + "1.0.3\r\n"
            + "• 버전 정보와 조회 기록 창의 글꼴 및 다시 그리기 오류를 수정했습니다.\r\n"
            + "• Codex의 세션 제목 메뉴를 인식하고, 배지 왼쪽을 폴더 아이콘에 맞췄습니다.\r\n"
            + "• 사이드바 배지의 기본 하단 여백을 줄였습니다. 기존에 저장된 여백은 유지합니다.\r\n\r\n"
            + "1.0.2\r\n"
            + "• 실행 중 사용하던 Codex 경로가 사라지면 새 경로로 다시 연결합니다.\r\n"
            + "• 설정 창에서 CLI 경로와 한도 조회 상태, 최근 자동·수동 조회 기록을 확인할 수 있습니다.\r\n"
            + "• 현재 버전과 버전별 변경 내역을 확인할 수 있습니다.\r\n\r\n"
            + "1.0.1\r\n"
            + "• 설정과 PATH에서 CLI를 찾지 못하면 Windows에 등록된 Codex 데스크톱 설치를 사용합니다.\r\n\r\n"
            + "1.0.0\r\n"
            + "• 포터블 ZIP을 정식 배포했습니다.";
    }
}
