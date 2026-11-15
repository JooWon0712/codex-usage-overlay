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
            "1.0.3\r\n"
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
