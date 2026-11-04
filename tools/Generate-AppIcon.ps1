# Developer asset generator. This does not install or launch the application.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    # Use the same .NET Framework drawing stack as the Windows application.
    $taskWindowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $taskWindowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -OutputDirectory ([IO.Path]::GetFullPath($OutputDirectory))
    if ($LASTEXITCODE -ne 0) { throw 'App icon generation failed.' }
    return
}
Add-Type -AssemblyName System.Drawing
if (-not ('CodexUsageOverlayAssets.IconGenerator' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CodexUsageOverlayAssets
{
    public static class IconGenerator
    {
        private static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void Fill(Graphics graphics, string color, float x, float y, float width, float height, float radius)
        {
            using (var path = Rounded(x, y, width, height, radius))
            using (var brush = new SolidBrush(ColorTranslator.FromHtml(color)))
                graphics.FillPath(brush, path);
        }

        public static Bitmap Render(int size)
        {
            // Supersampling keeps rounded edges smooth at the small caption sizes.
            using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(large))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.ScaleTransform(size * 4 / 256f, size * 4 / 256f);
                    Fill(graphics, "#202630", 8, 8, 240, 240, 52);
                    using (var path = Rounded(8, 8, 240, 240, 52))
                    using (var pen = new Pen(ColorTranslator.FromHtml("#A8B7CC"), 8))
                        graphics.DrawPath(pen, path);
                    Fill(graphics, "#526171", 48, 54, 160, 44, 12);
                    Fill(graphics, "#80D3BA", 48, 54, 112, 44, 12);
                    Fill(graphics, "#E7EBF0", 48, 130, 128, 16, 8);
                    Fill(graphics, "#A8B7CC", 48, 174, 92, 16, 8);
                }
                var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(large, new Rectangle(0, 0, size, size));
                }
                return result;
            }
        }

        private static byte[] Frame(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                // Small frames use the Windows DIB format for broad icon API support.
                if (bitmap.Width <= 64)
                {
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    {
                        int size = bitmap.Width, maskStride = ((size + 31) / 32) * 4;
                        writer.Write(40); writer.Write(size); writer.Write(size * 2);
                        writer.Write((short)1); writer.Write((short)32);
                        writer.Write(0); writer.Write(size * size * 4 + maskStride * size);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                        for (int y = size - 1; y >= 0; y--)
                            for (int x = 0; x < size; x++)
                            {
                                Color color = bitmap.GetPixel(x, y);
                                writer.Write(color.B); writer.Write(color.G); writer.Write(color.R); writer.Write(color.A);
                            }
                        for (int y = size - 1; y >= 0; y--)
                        {
                            var row = new byte[maskStride];
                            for (int x = 0; x < size; x++)
                                if (bitmap.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                            writer.Write(row);
                        }
                    }
                }
                else bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public static void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var bitmap = Render(sizes[i])) frames[i] = Frame(bitmap);
            using (var stream = File.Create(Path.Combine(directory, "app.ico")))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((short)1); writer.Write((short)32);
                    writer.Write(frames[i].Length); writer.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var frame in frames) writer.Write(frame);
            }
        }
    }
}
'@
}
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
[CodexUsageOverlayAssets.IconGenerator]::Save($taskOutput)
Write-Output "Generated: $(Join-Path $taskOutput 'app.ico') (16, 20, 24, 32, 40, 48, 64, 128, 256 px)"
