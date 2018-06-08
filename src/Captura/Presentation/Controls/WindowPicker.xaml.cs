﻿using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screna;
using Point = System.Drawing.Point;
using Window = Screna.Window;

namespace Captura
{
    public partial class WindowPicker
    {
        public WindowPicker()
        {
            InitializeComponent();

            Left = Top = 0;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            UpdateBackground();

            _windows = Window.EnumerateVisible().ToArray();
        }

        readonly Window[] _windows;

        public Window SelectedWindow { get; private set; }

        void UpdateBackground()
        {
            using (var bmp = ScreenShot.Capture())
            {
                var stream = new MemoryStream();
                bmp.Save(stream, ImageFormat.Png);

                stream.Seek(0, SeekOrigin.Begin);

                var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
                Background = new ImageBrush(decoder.Frames[0]);
            }
        }

        void CloseClick(object Sender, RoutedEventArgs E)
        {
            SelectedWindow = null;

            Close();
        }

        void WindowMouseMove(object Sender, MouseEventArgs E)
        {
            var pos = E.GetPosition(this);

            var point = new Point((int) (pos.X * Dpi.X), (int) (pos.Y * Dpi.Y));

            var window = _windows.FirstOrDefault(M => M.Rectangle.Contains(point));

            if (window != null)
            {
                SelectedWindow = window;

                Cursor = Cursors.Hand;

                var rect = window.Rectangle;

                WindowBorder.Margin = new Thickness(rect.Left / Dpi.X, rect.Top / Dpi.Y, 0, 0);

                WindowBorder.Width = rect.Width / Dpi.X;
                WindowBorder.Height = rect.Height / Dpi.Y;
                
                WindowBorder.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedWindow = null;

                Cursor = Cursors.Arrow;

                WindowBorder.Visibility = Visibility.Collapsed;
            }
        }

        void WindowMouseLeftButtonDown(object Sender, MouseButtonEventArgs E)
        {
            if (SelectedWindow != null)
            {
                Close();
            }
        }
    }
}
