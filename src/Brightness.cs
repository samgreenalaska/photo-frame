// Monitor brightness over DDC/CI, the same control as the monitor's own buttons.
// A monitor without DDC/CI (most laptop panels, some TVs, many docks and KVMs)
// simply is not changed.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

static class Brightness
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("dxva2.dll")]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll")]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")]
    static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")]
    static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")]
    static extern bool SetMonitorBrightness(IntPtr h, uint value);

    const uint Unknown = uint.MaxValue;

    // Fades every monitor to the target and returns what each was, for Restore.
    public static List<uint> Dim(int percent)
    {
        List<uint> from = null;
        WithMonitors(delegate(List<IntPtr> hs)
        {
            from = ReadAll(hs);
            var to = new List<uint>();
            foreach (uint f in from) to.Add(f == Unknown ? f : (uint)percent);
            Fade(hs, from, to);
        });
        return from ?? new List<uint>();
    }

    public static void Restore(List<uint> saved)
    {
        if (saved == null) return;
        WithMonitors(delegate(List<IntPtr> hs)
        {
            Fade(hs, ReadAll(hs), saved);
        });
    }

    // Reads first and writes only on a difference, to keep DDC/CI writes rare.
    // Called every few seconds rather than once, because a KVM switched away
    // takes DDC/CI with it and a single write would be lost.
    public static void Hold(int percent)
    {
        WithMonitors(delegate(List<IntPtr> hs)
        {
            List<uint> current = ReadAll(hs);
            for (int i = 0; i < hs.Count; i++)
            {
                if (current[i] == Unknown || current[i] == (uint)percent) continue;
                bool ok = SetMonitorBrightness(hs[i], (uint)percent);
                PhotoFrame.Log(string.Format("monitor {0}: brightness {1} to {2}{3}", i, current[i], percent, ok ? "" : " failed"));
            }
        });
    }

    // How many monitors answer DDC/CI, for the settings window.
    public static int Controllable()
    {
        int n = 0;
        WithMonitors(delegate(List<IntPtr> hs)
        {
            foreach (uint v in ReadAll(hs)) if (v != Unknown) n++;
        });
        return n;
    }

    // Handles are opened per use: they go stale across sleep, display changes
    // and reconnects, which a long-running watcher will see.
    static void WithMonitors(Action<List<IntPtr>> body)
    {
        var groups = new List<PhysicalMonitor[]>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, IntPtr r, IntPtr d)
        {
            uint n;
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out n) && n > 0)
            {
                var arr = new PhysicalMonitor[n];
                if (GetPhysicalMonitorsFromHMONITOR(hMon, n, arr)) groups.Add(arr);
            }
            return true;
        }, IntPtr.Zero);
        try
        {
            var handles = new List<IntPtr>();
            foreach (PhysicalMonitor[] g in groups)
                foreach (PhysicalMonitor m in g) handles.Add(m.Handle);
            body(handles);
        }
        finally
        {
            foreach (PhysicalMonitor[] g in groups) DestroyPhysicalMonitors((uint)g.Length, g);
        }
    }

    static List<uint> ReadAll(List<IntPtr> handles)
    {
        var values = new List<uint>();
        foreach (IntPtr h in handles)
        {
            uint min, cur, max;
            values.Add(GetMonitorBrightness(h, out min, out cur, out max) ? cur : Unknown);
        }
        return values;
    }

    // A few steps over about a second, so the change reads as a fade.
    static void Fade(List<IntPtr> handles, List<uint> from, List<uint> to)
    {
        const int steps = 5;
        for (int s = 1; s <= steps; s++)
        {
            for (int i = 0; i < handles.Count && i < from.Count && i < to.Count; i++)
            {
                if (from[i] == Unknown || to[i] == Unknown) continue;
                SetMonitorBrightness(handles[i], (uint)(from[i] + ((int)to[i] - (int)from[i]) * s / steps));
            }
            if (s < steps) Thread.Sleep(200);
        }
    }
}
