using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PersonalUsageOverlay
{
    internal static class LayeredBitmapPresenter
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Size { public int Width,Height; }
        [StructLayout(LayoutKind.Sequential,Pack=1)]
        private struct Blend { public byte Operation,Flags,Alpha,Format; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public uint HeaderSize;
            public int Width,Height;
            public ushort Planes,BitCount;
            public uint Compression,ImageSize;
            public int XPelsPerMeter,YPelsPerMeter;
            public uint ColorsUsed,ColorsImportant,Color;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll",SetLastError=true)] private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref Point destination,ref Size size,IntPtr source,ref Point origin,uint key,ref Blend blend,uint flags);

        internal static int LastError { get; private set; }
        internal static bool Present(IntPtr window,Bitmap bitmap,System.Drawing.Point destination,byte visibility)
        {
            LastError=0;
            if(bitmap.PixelFormat!=PixelFormat.Format32bppPArgb) throw new ArgumentException("A premultiplied bitmap is required.");
            IntPtr screen=IntPtr.Zero, memory=IntPtr.Zero, dib=IntPtr.Zero, previous=IntPtr.Zero;
            try
            {
                screen=GetDC(IntPtr.Zero); if(screen==IntPtr.Zero) return false;
                memory=CreateCompatibleDC(screen); if(memory==IntPtr.Zero) return false;
                var info=new BitmapInfo {HeaderSize=40,Width=bitmap.Width,Height=-bitmap.Height,Planes=1,BitCount=32};
                IntPtr bits;
                dib=CreateDIBSection(screen,ref info,0,out bits,IntPtr.Zero,0);
                if(dib==IntPtr.Zero || bits==IntPtr.Zero) { LastError=Marshal.GetLastWin32Error(); return false; }
                var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try
                {
                    int stride=checked(bitmap.Width*4); var row=new byte[stride];
                    for(int y=0;y<bitmap.Height;y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,stride);
                        Marshal.Copy(row,0,IntPtr.Add(bits,y*stride),stride);
                    }
                }
                finally { bitmap.UnlockBits(data); }
                previous=SelectObject(memory,dib);
                if(previous==IntPtr.Zero || previous==new IntPtr(-1)) return false;
                var point=new Point {X=destination.X,Y=destination.Y}; var origin=new Point();
                var size=new Size {Width=bitmap.Width,Height=bitmap.Height};
                var blend=new Blend {Operation=0,Flags=0,Alpha=visibility,Format=1};
                bool result=UpdateLayeredWindow(window,screen,ref point,ref size,memory,ref origin,0,ref blend,2);
                if(!result) LastError=Marshal.GetLastWin32Error();
                return result;
            }
            finally
            {
                if(previous!=IntPtr.Zero && previous!=new IntPtr(-1)) SelectObject(memory,previous);
                if(dib!=IntPtr.Zero) DeleteObject(dib);
                if(memory!=IntPtr.Zero) DeleteDC(memory);
                if(screen!=IntPtr.Zero) ReleaseDC(IntPtr.Zero,screen);
            }
        }
    }
}
