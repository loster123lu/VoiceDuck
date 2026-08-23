using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceDuck
{
    internal sealed class PreciseTrackBar : TrackBar
    {
        private const int LeftButtonDown = 0x0201;
        private const int LeftButtonDoubleClick = 0x0203;
        private const int GetThumbRectangle = 0x0400 + 25;
        private const int GetChannelRectangle = 0x0400 + 26;

        protected override void WndProc(ref Message message)
        {
            if (Enabled && Orientation == Orientation.Horizontal &&
                (message.Msg == LeftButtonDown || message.Msg == LeftButtonDoubleClick))
            {
                Point point = PointFromMessage(message.LParam);
                NativeRectangle thumbRectangle = new NativeRectangle();
                SendMessage(Handle, GetThumbRectangle, IntPtr.Zero, ref thumbRectangle);

                if (!thumbRectangle.Contains(point))
                {
                    NativeRectangle channelRectangle = new NativeRectangle();
                    SendMessage(Handle, GetChannelRectangle, IntPtr.Zero, ref channelRectangle);
                    int mappedValue = TrackBarValueMapper.FromPosition(
                        Minimum,
                        Maximum,
                        SmallChange,
                        channelRectangle.Left,
                        channelRectangle.Right,
                        point.X,
                        RightToLeft == RightToLeft.Yes);

                    Focus();
                    if (Value != mappedValue)
                    {
                        Value = mappedValue;
                        OnScroll(EventArgs.Empty);
                    }
                    message.Result = IntPtr.Zero;
                    return;
                }
            }

            base.WndProc(ref message);
        }

        private static Point PointFromMessage(IntPtr lParam)
        {
            int packed = lParam.ToInt32();
            int x = (short)(packed & 0xffff);
            int y = (short)((packed >> 16) & 0xffff);
            return new Point(x, y);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public bool Contains(Point point)
            {
                return point.X >= Left && point.X <= Right &&
                       point.Y >= Top && point.Y <= Bottom;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr window,
            int message,
            IntPtr wParam,
            ref NativeRectangle lParam);
    }
}
