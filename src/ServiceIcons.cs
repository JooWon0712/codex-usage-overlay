using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml;

namespace PersonalUsageOverlay
{
    // Source only the currently registered desktop apps' local artwork.
    internal sealed class ServiceIcons : IDisposable
    {
        private const int MaxAssetBytes = 1024 * 1024;
        private const int MaxIconSize = 512;

        internal Bitmap Codex { get; private set; }
        internal Bitmap Claude { get; private set; }
        internal Bitmap CodexLight { get; private set; }

        internal ServiceIcons(Bitmap codex, Bitmap claude, Bitmap codexLight = null)
        {
            Codex = codex;
            Claude = claude;
            CodexLight = codexLight;
        }

        internal static ServiceIcons Load()
        {
            string codexLogo = FindLogo(InstalledCodex.FindPackageDirectory, "App", @"app\ChatGPT.exe", @"app\Codex.exe");
            string claudeLogo = FindLogo(delegate { return InstalledCodex.FindPackageDirectory("Claude", "pzs8sxrjxfjjc"); },
                "Claude", @"app\Claude.exe");
            return new ServiceIcons(LoadVariant(codexLogo, false), LoadVariant(claudeLogo, false), LoadVariant(codexLogo, true));
        }

        private static string FindLogo(Func<string> packageDirectory, string appId, params string[] executables)
        {
            try
            {
                string package = packageDirectory();
                if (package == null) return null;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxAssetBytes };
                var manifest = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(Path.Combine(package, "AppxManifest.xml"), settings))
                    manifest.Load(reader);
                foreach (XmlElement app in manifest.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']"))
                {
                    if (!appId.Equals(app.GetAttribute("Id"), StringComparison.OrdinalIgnoreCase)) continue;
                    string executable = app.GetAttribute("Executable").Replace('/', '\\');
                    if (!Array.Exists(executables, candidate => candidate.Equals(executable, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var visuals = app.SelectSingleNode("*[local-name()='VisualElements']") as XmlElement;
                    if (visuals == null) continue;
                    string asset = visuals.GetAttribute("Square44x44Logo").Replace('/', '\\');
                    if (!".png".Equals(Path.GetExtension(asset), StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(asset))
                        return null;
                    string root = Path.GetFullPath(package).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string logo = Path.GetFullPath(Path.Combine(root, asset));
                    return logo.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? logo : null;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (XmlException) { }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return null;
        }

        private static Bitmap LoadVariant(string logo, bool light)
        {
            if (logo == null) return null;
            string stem = Path.Combine(Path.GetDirectoryName(logo), Path.GetFileNameWithoutExtension(logo));
            string suffix = light ? "_altform-lightunplated.png" : "_altform-unplated.png";
            return LoadPng(stem + ".targetsize-32" + suffix) ?? LoadPng(stem + ".targetsize-24" + suffix)
                ?? (light ? null : LoadPng(logo));
        }

        private static Bitmap LoadPng(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    return DecodePng(stream);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            catch (ArgumentException) { }
            catch (ExternalException) { }
            catch (OutOfMemoryException) { } // GDI+ also uses this for invalid images.
            return null;
        }

        internal static Bitmap DecodePng(Stream stream)
        {
            if (stream.Length < 8 || stream.Length > MaxAssetBytes) return null;
            using (var image = Image.FromStream(stream, false, true))
            {
                if (image.RawFormat.Guid != ImageFormat.Png.Guid || image.Width > MaxIconSize || image.Height > MaxIconSize)
                    return null;
                // Copy the full pixel rectangle regardless of the PNG's physical DPI metadata.
                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                try
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
                            0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            if (Codex != null) Codex.Dispose();
            if (Claude != null && !ReferenceEquals(Claude, Codex)) Claude.Dispose();
            if (CodexLight != null && !ReferenceEquals(CodexLight, Codex) && !ReferenceEquals(CodexLight, Claude)) CodexLight.Dispose();
            Codex = Claude = CodexLight = null;
        }
    }
}
