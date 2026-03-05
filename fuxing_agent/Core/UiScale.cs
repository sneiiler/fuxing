using System;
using System.Drawing;
using System.Windows.Forms;

namespace FuXingAgent.Core
{
    internal static class UiScale
    {
        private const float BaseDpi = 96f;

        public static float GetScaleForControl(Control control)
        {
            if (control == null) return 1f;
            try
            {
                using (var g = control.CreateGraphics())
                {
                    return NormalizeScale(g.DpiX / BaseDpi);
                }
            }
            catch
            {
                return 1f;
            }
        }

        public static float GetScaleForHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 1f;
            try
            {
                using (var g = Graphics.FromHwnd(hwnd))
                {
                    return NormalizeScale(g.DpiX / BaseDpi);
                }
            }
            catch
            {
                return 1f;
            }
        }

        public static int Scale(Control control, int value)
        {
            return Scale(value, GetScaleForControl(control));
        }

        public static int Scale(int value, float scale)
        {
            if (value <= 0) return 0;
            return Math.Max(1, (int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
        }

        public static Size Scale(Control control, Size value)
        {
            return new Size(Scale(control, value.Width), Scale(control, value.Height));
        }

        public static Padding Scale(Control control, Padding value)
        {
            return new Padding(
                Scale(control, value.Left),
                Scale(control, value.Top),
                Scale(control, value.Right),
                Scale(control, value.Bottom));
        }

        private static float NormalizeScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) return 1f;
            return Math.Max(1f, Math.Min(3f, scale));
        }
    }
}
