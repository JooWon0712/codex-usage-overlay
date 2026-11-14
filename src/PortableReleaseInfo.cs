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
            "1.0.2\r\n"
            + "• 실행 중 사용하던 Codex 경로가 사라지면 새 경로로 다시 연결합니다.\r\n"
            + "• 설정 창에서 CLI 경로와 한도 조회 상태, 최근 자동·수동 조회 기록을 확인할 수 있습니다.\r\n"
            + "• 현재 버전과 버전별 변경 내역을 확인할 수 있습니다.\r\n\r\n"
            + "1.0.1\r\n"
            + "• 설정과 PATH에서 CLI를 찾지 못하면 Windows에 등록된 Codex 데스크톱 설치를 사용합니다.\r\n\r\n"
            + "1.0.0\r\n"
            + "• 포터블 ZIP을 정식 배포했습니다.";
    }
}
