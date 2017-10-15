using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Screna
{
    public class ImageWrapper : IDisposable
    {
        ImageWrapper() { }

        public ImageWrapper(int Width, int Height)
        {
            Bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);

            Graphics = Graphics.FromImage(Bitmap);
        }

        public Bitmap Bitmap { get; }

        public Graphics Graphics { get; }

        public bool Written { get; set; }

        public void CopyTo(byte[] Output)
        {
            var bits = Bitmap.LockBits(new Rectangle(Point.Empty, Bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            Marshal.Copy(bits.Scan0, Output, 0, Output.Length);
            Bitmap.UnlockBits(bits);

            Written = true;
        }

        public void Dispose()
        {
            Graphics.Dispose();

            Bitmap.Dispose();
        }

        public static ImageWrapper Repeat { get; } = new ImageWrapper();
    }
}