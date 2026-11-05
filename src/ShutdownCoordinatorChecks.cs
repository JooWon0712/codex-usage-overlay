using System;
using System.Collections.Generic;

namespace PersonalUsageOverlay
{
    internal static class ShutdownCoordinatorChecks
    {
        internal static void Run(Dictionary<string, bool> checks)
        {
            string target; int timeout; bool noClose;
            checks["install_bridge_requires_explicit_absolute_target_and_bounded_timeout"] =
                ShutdownCoordinator.TryPrepareArguments(new[] { "--prepare-install", "--target", "C:\\fixture\\overlay", "--wait", "30000" }, out target, out timeout, out noClose)
                && target == "C:\\fixture\\overlay\\UsageOverlay.exe" && timeout == 30000 && !noClose;
            checks["install_bridge_no_close_is_explicit_and_does_not_change_target"] =
                ShutdownCoordinator.TryPrepareArguments(new[] { "--prepare-install", "--target", "C:\\fixture\\overlay", "--wait", "1000", "--no-close" }, out target, out timeout, out noClose)
                && noClose && target == "C:\\fixture\\overlay\\UsageOverlay.exe";
            bool invalid = true;
            foreach (var args in new[] {
                new[] { "--prepare-install", "--target", "relative", "--wait", "30000" },
                new[] { "--prepare-install", "--target", "C:relative", "--wait", "30000" },
                new[] { "--prepare-install", "--target", "\\\\server\\share", "--wait", "30000" },
                new[] { "--prepare-install", "--target", "C:\\fixture", "--wait", "999" },
                new[] { "--prepare-install", "--target", "C:\\fixture", "--wait", "60001" },
                new[] { "--prepare-install", "--target", "C:\\fixture", "--wait", "30000", "--unknown" },
                new[] { "--prepare-install" } })
                invalid &= !ShutdownCoordinator.TryPrepareArguments(args, out target, out timeout, out noClose);
            checks["install_bridge_rejects_relative_unc_unknown_and_unbounded_commands"] = invalid;
            checks["install_bridge_identity_binds_protocol_pid_creation_and_cleanup_contract"] =
                ShutdownCoordinator.ValidIdentity("CUO-LIFETIME-1|42|123456|drain", 42, 123456)
                && !ShutdownCoordinator.ValidIdentity("CUO-LIFETIME-1|43|123456|drain", 42, 123456)
                && !ShutdownCoordinator.ValidIdentity("CUO-LIFETIME-1|42|123457|drain", 42, 123456)
                && !ShutdownCoordinator.ValidIdentity("CUO-LIFETIME-0|42|123456|drain", 42, 123456)
                && !ShutdownCoordinator.ValidIdentity("CUO-LIFETIME-1|42|123456", 42, 123456);
            checks["install_fixture_refuses_normal_user_environment_before_creating_identity"] =
                LifetimeFixture.AllowedEnvironment("true", "github-hosted")
                && !LifetimeFixture.AllowedEnvironment(null, null)
                && !LifetimeFixture.AllowedEnvironment("true", "self-hosted")
                && !LifetimeFixture.AllowedEnvironment("false", "github-hosted");
        }
    }
}
