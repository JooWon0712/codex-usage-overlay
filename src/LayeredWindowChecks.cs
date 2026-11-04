using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PersonalUsageOverlay
{
    internal static class LayeredWindowChecks
    {
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process,uint flags);
        internal static object Observation;
        public static void Run(Dictionary<string,bool> checks)
        {
            // A test-only badge on no monitor, with no context, worker or user data.
            using(var badge=new Badge {Location=new Point(-32000,-32000),ClientSize=new Size(320,82),Presentation=SettingsPreviewHost.CreateSample(96,"ok")})
            {
                bool prepared=badge.Present(0);
                int firstError=LayeredBitmapPresenter.LastError;
                if(prepared) badge.Show();
                bool shown=prepared && badge.Present();
                checks["layered_badge_accepts_pixel_alpha_before_and_after_show"] = shown;
                checks["layered_badge_keeps_explicit_screen_location"] = badge.Location==new Point(-32000,-32000);
                long style=Native.GetWindowLongPtr(badge.Handle,-20).ToInt64();
                checks["layered_badge_keeps_click_through_noactivate_and_no_tray_window_styles"] = (style&0x80000)!=0
                    && (style&0x20)!=0 && (style&0x8000000)!=0 && !badge.ShowInTaskbar && badge.TransparencyKey.IsEmpty && badge.Opacity==1;
                bool updates=shown;
                foreach(int transparency in new[] {25,50,100,0})
                {
                    var layout=badge.LayoutSettings.Copy(); layout.BackgroundTransparency=transparency;
                    badge.LayoutSettings=layout; updates &= badge.Present();
                }
                checks["layered_badge_repaints_background_changes_without_global_opacity"] = updates;
                badge.Theme=VisualTheme.Light;
                bool themed=badge.Present();
                var compact=badge.LayoutSettings.Copy(); compact.Elements.Find(item=>item.Key=="weekly-reset").Visible=false; compact.Normalize();
                badge.LayoutSettings=compact; badge.ScaleFactor=1.5f; badge.ClientSize=new Size(480,87);
                checks["layered_badge_repaints_theme_dpi_and_compact_size"] = themed && badge.Present();
                badge.Hide();
                bool masked=badge.Present(0); if(masked) badge.Show();
                checks["layered_badge_can_prepare_hidden_content_and_reveal_again"] = masked && badge.Present();
                checks["layered_badge_fixture_never_activates_itself"] = Native.GetForegroundWindow()!=badge.Handle;
                using(var bitmap=BadgeRenderer.CreateBitmap(badge.ClientSize,badge.ScaleFactor,badge.Theme,badge.Presentation,badge.LayoutSettings))
                using(var process=Process.GetCurrentProcess())
                {
                    LayeredBitmapPresenter.Present(badge.Handle,bitmap,badge.Location,0);
                    uint before=GetGuiResources(process.Handle,0);
                    bool successful=true, failures=true;
                    for(int index=0;index<20;index++)
                    {
                        successful &= LayeredBitmapPresenter.Present(badge.Handle,bitmap,badge.Location,0);
                        failures &= !LayeredBitmapPresenter.Present(IntPtr.Zero,bitmap,badge.Location,0);
                    }
                    uint after=GetGuiResources(process.Handle,0);
                    checks["layered_presenter_releases_gdi_resources_on_success_and_failure"] = successful && failures && after<=before+1;
                    Observation=new {prepared=prepared,shown=shown,firstError=firstError,gdiBefore=before,gdiAfter=after};
                }
                badge.Hide();
            }
        }
    }
}
