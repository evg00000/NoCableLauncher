// NoCableLauncher 2 — Core Audio interop.
// Rewritten from the CoreAudioApi wrapper by Ray Molenkamp (zlib license),
// as used in the original NoCableLauncher by Maxx53. Altered source.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NoCableLauncher
{
    public enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    public enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [Flags]
    public enum DeviceState
    {
        Active = 0x1, Disabled = 0x2, NotPresent = 0x4, Unplugged = 0x8, All = 0xF
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
        public PROPERTYKEY(Guid g, int p) { fmtid = g; pid = p; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out DeviceState state);
    }

    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    // Undocumented IPolicyConfig (Windows 7 .. 11). Used by mmsys.cpl itself.
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool useDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigComObject { }

    public class AudioDevice
    {
        public string Id;
        public string Name;
        public string HardwareId;
        public EDataFlow Flow;
        public DeviceState State;
        public string Vid = "0000";
        public string Pid = "0000";

        public bool IsActive { get { return State == DeviceState.Active; } }

        public override string ToString()
        {
            string s = Name;
            if (Vid != "0000" || Pid != "0000") s += "  [" + Vid + ":" + Pid + "]";
            if (!IsActive) s += "  (" + StateText(State) + ")";
            return s;
        }

        public static string StateText(DeviceState st)
        {
            switch (st)
            {
                case DeviceState.Active: return "активно";
                case DeviceState.Disabled: return "отключено";
                case DeviceState.Unplugged: return "не подключено";
                case DeviceState.NotPresent: return "отсутствует";
                default: return st.ToString();
            }
        }
    }

    /// <summary>Format of an audio endpoint as raw WAVEFORMATEX(TENSIBLE) bytes.</summary>
    public class WaveFormat
    {
        public byte[] Raw;

        public WaveFormat(byte[] raw) { Raw = raw; }

        public ushort Tag { get { return BitConverter.ToUInt16(Raw, 0); } }
        public int Channels { get { return BitConverter.ToUInt16(Raw, 2); } }
        public int SampleRate { get { return BitConverter.ToInt32(Raw, 4); } }
        public int ContainerBits { get { return BitConverter.ToUInt16(Raw, 14); } }
        public bool IsExtensible { get { return Tag == 0xFFFE && Raw.Length >= 40; } }
        public int ValidBits
        {
            get
            {
                if (IsExtensible)
                {
                    int v = BitConverter.ToUInt16(Raw, 18);
                    return v != 0 ? v : ContainerBits;
                }
                return ContainerBits;
            }
        }
        public int ChannelMask
        {
            get
            {
                if (IsExtensible) return BitConverter.ToInt32(Raw, 20);
                return Channels == 1 ? 0x4 : (Channels == 2 ? 0x3 : 0);
            }
        }
        public bool IsFloat
        {
            get
            {
                if (Tag == 3) return true;
                if (IsExtensible) return new Guid(Sub(24, 16)) == SubtypeFloat;
                return false;
            }
        }

        byte[] Sub(int offset, int len)
        {
            byte[] r = new byte[len];
            Array.Copy(Raw, offset, r, 0, len);
            return r;
        }

        public static readonly Guid SubtypePcm = new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static readonly Guid SubtypeFloat = new Guid("00000003-0000-0010-8000-00AA00389B71");

        public static WaveFormat Create(int channels, int rate, int containerBits, int validBits, int channelMask, bool isFloat)
        {
            byte[] b = new byte[40];
            int blockAlign = channels * containerBits / 8;
            Put16(b, 0, 0xFFFE);
            Put16(b, 2, channels);
            Put32(b, 4, rate);
            Put32(b, 8, rate * blockAlign);
            Put16(b, 12, blockAlign);
            Put16(b, 14, containerBits);
            Put16(b, 16, 22);
            Put16(b, 18, validBits);
            Put32(b, 20, channelMask);
            Array.Copy((isFloat ? SubtypeFloat : SubtypePcm).ToByteArray(), 0, b, 24, 16);
            return new WaveFormat(b);
        }

        static void Put16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        static void Put32(byte[] b, int o, int v) { Put16(b, o, v & 0xFFFF); Put16(b, o + 2, (v >> 16) & 0xFFFF); }

        public string ToHex() { return BitConverter.ToString(Raw).Replace("-", ""); }

        public static WaveFormat FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return null;
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return new WaveFormat(b);
        }

        public override string ToString()
        {
            return string.Format("{0} Гц, {1} бит{2}, {3} кан.", SampleRate, ValidBits, IsFloat ? " float" : "", Channels);
        }
    }

    public static class CoreAudio
    {
        static readonly PROPERTYKEY PKEY_Device_FriendlyName =
            new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        // Same key the original launcher used; returns "{1}.USB\VID_xxxx&PID_xxxx..." for USB devices.
        static readonly PROPERTYKEY PKEY_Device_HardwareId =
            new PROPERTYKEY(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PROPVARIANT pvar);

        const ushort VT_LPWSTR = 31;
        const int STGM_READ = 0;

        static IMMDeviceEnumerator NewEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        static IPolicyConfig NewPolicy()
        {
            IPolicyConfig p = new PolicyConfigComObject() as IPolicyConfig;
            if (p == null) throw new NotSupportedException("IPolicyConfig недоступен в этой версии Windows");
            return p;
        }

        static void Check(int hr, string what)
        {
            if (hr != 0) throw new COMException(what + " (HRESULT 0x" + hr.ToString("X8") + ")", hr);
        }

        static string ReadString(IPropertyStore store, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (store.GetValue(ref key, out pv) != 0) return null;
            try
            {
                if (pv.vt == VT_LPWSTR && pv.pointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringUni(pv.pointerValue);
                return null;
            }
            finally { PropVariantClear(ref pv); }
        }

        static AudioDevice Describe(IMMDevice dev, EDataFlow flow)
        {
            AudioDevice d = new AudioDevice();
            d.Flow = flow;
            string id;
            Check(dev.GetId(out id), "IMMDevice.GetId");
            d.Id = id;
            DeviceState st;
            if (dev.GetState(out st) == 0) d.State = st;

            IPropertyStore store;
            if (dev.OpenPropertyStore(STGM_READ, out store) == 0 && store != null)
            {
                try
                {
                    d.Name = ReadString(store, PKEY_Device_FriendlyName);
                    d.HardwareId = ReadString(store, PKEY_Device_HardwareId);
                }
                finally { Marshal.ReleaseComObject(store); }
            }
            if (string.IsNullOrEmpty(d.Name)) d.Name = "Неизвестное устройство";
            if (d.HardwareId == null) d.HardwareId = "";

            string hw = d.HardwareId.ToUpperInvariant();
            int iv = hw.IndexOf("VID_");
            int ip = hw.IndexOf("PID_");
            if (iv >= 0 && ip >= 0 && iv + 8 <= hw.Length && ip + 8 <= hw.Length)
            {
                d.Vid = hw.Substring(iv + 4, 4);
                d.Pid = hw.Substring(ip + 4, 4);
            }
            return d;
        }

        public static List<AudioDevice> GetDevices(EDataFlow flow, DeviceState mask)
        {
            List<AudioDevice> list = new List<AudioDevice>();
            IMMDeviceEnumerator en = NewEnumerator();
            try
            {
                IMMDeviceCollection col;
                Check(en.EnumAudioEndpoints(flow, mask, out col), "EnumAudioEndpoints");
                int count;
                col.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IMMDevice dev;
                    if (col.Item(i, out dev) != 0 || dev == null) continue;
                    try
                    {
                        // When enumerating "All" flows we don't know the flow per device here,
                        // so callers should enumerate Render and Capture separately.
                        list.Add(Describe(dev, flow));
                    }
                    catch (Exception ex) { Log.Write("Пропущено устройство: " + ex.Message); }
                    finally { Marshal.ReleaseComObject(dev); }
                }
                Marshal.ReleaseComObject(col);
            }
            finally { Marshal.ReleaseComObject(en); }
            return list;
        }

        public static string GetDefaultId(EDataFlow flow, ERole role)
        {
            IMMDeviceEnumerator en = NewEnumerator();
            try
            {
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return null;
                try
                {
                    string id;
                    return dev.GetId(out id) == 0 ? id : null;
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            finally { Marshal.ReleaseComObject(en); }
        }

        public static void SetVisibility(string id, bool visible)
        {
            IPolicyConfig p = NewPolicy();
            try { Check(p.SetEndpointVisibility(id, visible), "SetEndpointVisibility"); }
            finally { Marshal.ReleaseComObject(p); }
        }

        public static void SetDefault(string id, ERole role)
        {
            IPolicyConfig p = NewPolicy();
            try { Check(p.SetDefaultEndpoint(id, role), "SetDefaultEndpoint"); }
            finally { Marshal.ReleaseComObject(p); }
        }

        static WaveFormat ReadFormatPtr(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try
            {
                int cbSize = Marshal.ReadInt16(ptr, 16) & 0xFFFF;
                ushort tag = (ushort)Marshal.ReadInt16(ptr, 0);
                int len = (tag == 1 || tag == 3) && cbSize == 0 ? 18 : 18 + cbSize;
                if (len < 18 || len > 1024) len = 18;
                byte[] b = new byte[len];
                Marshal.Copy(ptr, b, 0, len);
                return new WaveFormat(b);
            }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }

        /// <summary>Format the device is set to in "Sound" control panel (Advanced tab).</summary>
        public static WaveFormat GetDeviceFormat(string id)
        {
            IPolicyConfig p = NewPolicy();
            try
            {
                IntPtr ptr;
                if (p.GetDeviceFormat(id, false, out ptr) != 0) return null;
                return ReadFormatPtr(ptr);
            }
            finally { Marshal.ReleaseComObject(p); }
        }

        public static WaveFormat GetMixFormat(string id)
        {
            IPolicyConfig p = NewPolicy();
            try
            {
                IntPtr ptr;
                if (p.GetMixFormat(id, out ptr) != 0) return null;
                return ReadFormatPtr(ptr);
            }
            finally { Marshal.ReleaseComObject(p); }
        }

        public static void SetDeviceFormat(string id, WaveFormat endpoint, WaveFormat mix)
        {
            IPolicyConfig p = NewPolicy();
            IntPtr pe = IntPtr.Zero, pm = IntPtr.Zero;
            try
            {
                pe = Marshal.AllocCoTaskMem(endpoint.Raw.Length);
                Marshal.Copy(endpoint.Raw, 0, pe, endpoint.Raw.Length);
                if (mix != null)
                {
                    pm = Marshal.AllocCoTaskMem(mix.Raw.Length);
                    Marshal.Copy(mix.Raw, 0, pm, mix.Raw.Length);
                }
                Check(p.SetDeviceFormat(id, pe, pm), "SetDeviceFormat");
            }
            finally
            {
                if (pe != IntPtr.Zero) Marshal.FreeCoTaskMem(pe);
                if (pm != IntPtr.Zero) Marshal.FreeCoTaskMem(pm);
                Marshal.ReleaseComObject(p);
            }
        }
    }
}
