using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EpsBootRepair
{
    public enum DiskStyle { Unknown = -1, Mbr = 0, Gpt = 1 }

    /// <summary>一个卷（能挂载的分区）。ESP 通常没有盘符，也一样在列表里。</summary>
    public sealed class VolumeInfo
    {
        public string VolumePath = "";      // \\?\Volume{...}\
        public char DriveLetter = '\0';
        public string FileSystem = "";
        public string Label = "";
        public ulong Size;
        public int DiskNumber = -1;
        public int PartitionNumber;
        public ulong StartOffset;
        public DiskStyle Style = DiskStyle.Unknown;
        public Guid GptType;
        public byte MbrType;
        public string GptName = "";
        public int DriveType = -1;         // GetDriveType 结果；-1 = 没取到

        public bool HasLetter { get { return DriveLetter != '\0'; } }

        /// <summary>有盘符返回 "C:\"，没有就返回卷 GUID 路径（两者都能直接当路径用）。</summary>
        public string Root
        {
            get { return HasLetter ? DriveLetter + ":\\" : VolumePath; }
        }

        /// <summary>能当 bcdboot 的 /s 参数用吗（必须带盘符和冒号）。</summary>
        public string RootArg
        {
            get { return HasLetter ? DriveLetter + ":" : null; }
        }

        public bool IsFat
        {
            get { return FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase); }
        }

        public bool HasWindows
        {
            get { return Volumes.DirExists(Root, "Windows", "System32"); }
        }

        /// <summary>
        /// 能当"Windows 系统分区"候选吗。光驱和内存盘（PE 的 X:）也走 FindFirstVolume 枚举出来，
        /// 但它们没有磁盘属主（DiskNumber &lt; 0）；让它们进列表会把自动选择搅黄 —— 候选不唯一就不猜，
        /// 用户于是只能自己挑，一挑就容易挑到 ESP 上。
        /// </summary>
        public bool CanBeSystem
        {
            get
            {
                return DiskNumber >= 0
                    && DriveType != Volumes.DriveCdrom
                    && DriveType != Volumes.DriveRamdisk;
            }
        }

        /// <summary>ESP 判定：FAT + (GPT 的 ESP 类型 GUID / MBR 的 0xEF)；再兜一层"里面真有 EFI\Microsoft\Boot"。</summary>
        public bool IsEsp()
        {
            if (!IsFat) return false;
            if (Style == DiskStyle.Gpt && GptType == Volumes.EspGuid) return true;
            if (Style == DiskStyle.Mbr && MbrType == 0xEF) return true;
            return Volumes.DirExists(Root, "EFI", "Microsoft", "Boot");
        }
    }

    /// <summary>
    /// 固件模式 + 卷枚举。全走 Win32：不依赖 WMI 服务（精简 PE 里经常没装 Winmgmt）。
    /// </summary>
    public static class Volumes
    {
        public static readonly Guid EspGuid = new Guid("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");

        public const int DriveCdrom = 5;
        public const int DriveRamdisk = 6;

        const uint GENERIC_READ = 0x80000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint FILE_SHARE_WRITE = 0x00000002;
        const uint OPEN_EXISTING = 3;
        const uint IOCTL_DISK_GET_PARTITION_INFO_EX = 0x00070048;
        const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr FindFirstVolume(StringBuilder volumeName, uint bufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool FindNextVolume(IntPtr findHandle, StringBuilder volumeName, uint bufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FindVolumeClose(IntPtr findHandle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
                                        uint creationDisposition, uint flags, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
                                           byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern uint GetDriveType(string rootPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool GetVolumeInformation(string rootPathName, StringBuilder volumeNameBuffer, uint volumeNameSize,
                                                out uint volumeSerialNumber, out uint maximumComponentLength,
                                                out uint fileSystemFlags, StringBuilder fileSystemNameBuffer,
                                                uint fileSystemNameSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool GetVolumePathNamesForVolumeName(string volumeName, StringBuilder volumePathNames,
                                                           uint bufferLength, out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetVolumeMountPoint(string mountPoint, string volumeName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool DeleteVolumeMountPoint(string mountPoint);

        // ---------------------------------------------------------------- 分区表

        public static string StyleName(DiskStyle s)
        {
            return Lang.T(s == DiskStyle.Gpt ? "Partition.Gpt"
                        : s == DiskStyle.Mbr ? "Partition.Mbr" : "Partition.Unknown");
        }

        // ---------------------------------------------------------------- 卷枚举

        public static List<VolumeInfo> Enumerate()
        {
            var list = new List<VolumeInfo>();
            var sb = new StringBuilder(260);
            IntPtr h = FindFirstVolume(sb, (uint)sb.Capacity);
            if (h == InvalidHandle || h == IntPtr.Zero) return list;

            try
            {
                while (true)
                {
                    VolumeInfo v = Describe(sb.ToString());
                    if (v != null) list.Add(v);
                    sb.Length = 0;
                    if (!FindNextVolume(h, sb, (uint)sb.Capacity)) break;
                }
            }
            finally { FindVolumeClose(h); }
            return list;
        }

        static VolumeInfo Describe(string volumePath)
        {
            if (string.IsNullOrEmpty(volumePath)) return null;
            var v = new VolumeInfo { VolumePath = volumePath };

            var nameBuf = new StringBuilder(261);
            var fsBuf = new StringBuilder(261);
            uint serial, maxComponent, flags;
            if (GetVolumeInformation(volumePath, nameBuf, (uint)nameBuf.Capacity, out serial, out maxComponent,
                                     out flags, fsBuf, (uint)fsBuf.Capacity))
            {
                v.Label = nameBuf.ToString();
                v.FileSystem = fsBuf.ToString();
            }

            var paths = new StringBuilder(1024);
            uint need;
            if (GetVolumePathNamesForVolumeName(volumePath, paths, (uint)paths.Capacity, out need))
            {
                string s = paths.ToString();
                int cut = s.IndexOf('\0');
                string first = cut >= 0 ? s.Substring(0, cut) : s;
                if (first.Length >= 2 && first[1] == ':') v.DriveLetter = char.ToUpperInvariant(first[0]);
            }

            // 有盘符就用盘符问（最准）；没有盘符的卷 GUID 路径一样认。
            // 只在"确实问出是光驱/内存盘"时才排除，取不到结果不会误伤无盘符的真实分区。
            v.DriveType = (int)GetDriveType(v.HasLetter ? v.DriveLetter + ":\\" : volumePath);

            IntPtr h = OpenVolume(volumePath, GENERIC_READ);
            if (h == InvalidHandle) h = OpenVolume(volumePath, 0);
            if (h != InvalidHandle)
            {
                try { ReadLayout(h, v); }
                finally { CloseHandle(h); }
            }
            return v;
        }

        static IntPtr OpenVolume(string volumePath, uint access)
        {
            string dev = volumePath.TrimEnd('\\');
            return CreateFile(dev, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        static void ReadLayout(IntPtr h, VolumeInfo v)
        {
            uint ret;
            byte[] buf = new byte[256];

            // PARTITION_INFORMATION_EX：0 style / 8 offset / 16 length / 24 分区号 / 32 起是 MBR|GPT 联合体
            if (DeviceIoControl(h, IOCTL_DISK_GET_PARTITION_INFO_EX, IntPtr.Zero, 0, buf, (uint)buf.Length, out ret, IntPtr.Zero)
                && ret >= 144)
            {
                int style = BitConverter.ToInt32(buf, 0);
                v.StartOffset = (ulong)BitConverter.ToInt64(buf, 8);
                long len = BitConverter.ToInt64(buf, 16);
                v.Size = len > 0 ? (ulong)len : 0;
                v.PartitionNumber = BitConverter.ToInt32(buf, 24);

                if (style == 1)
                {
                    v.Style = DiskStyle.Gpt;
                    v.GptType = new Guid(new ReadOnlySpan<byte>(buf, 32, 16));
                    v.GptName = Encoding.Unicode.GetString(buf, 72, 72).TrimEnd('\0');
                }
                else if (style == 0)
                {
                    v.Style = DiskStyle.Mbr;
                    v.MbrType = buf[32];
                }
            }

            // VOLUME_DISK_EXTENTS：头 8 字节（4 计数 + 4 对齐），之后每条 24 字节
            byte[] ext = new byte[8 + 24 * 16];
            if (DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, ext, (uint)ext.Length, out ret, IntPtr.Zero)
                && ret >= 8)
            {
                int n = BitConverter.ToInt32(ext, 0);
                if (n > 0) v.DiskNumber = BitConverter.ToInt32(ext, 8);
            }
        }

        // ---------------------------------------------------------------- 小工具

        public static bool DirExists(string root, params string[] parts)
        {
            try
            {
                string p = root;
                foreach (string s in parts) p = Path.Combine(p, s);
                return Directory.Exists(p);
            }
            catch { return false; }
        }

        /// <summary>
        /// 给一个还没盘符的卷挂上盘符，返回 Win32 错误码（0 = 成功）。
        ///
        /// 走 SetVolumeMountPoint 而不是 diskpart：这一步只是"挂个盘符"，
        /// 却要为此启动一个依赖 VDS 服务的进程，是整套流程里最脆的一环 ——
        /// USM 系统总裁 PE 里内置的那份 diskpart 直接以 0x80070057
        /// （E_INVALIDARG）退出，且一个字的输出都没有。这个 API 就在本进程里，
        /// PE 精不精简服务都拦不住它。
        ///
        /// 挂上去的盘符和 diskpart 的 assign 一样是临时的，重启即失。
        /// </summary>
        public static int AssignLetter(VolumeInfo v, char letter)
        {
            if (v == null || string.IsNullOrEmpty(v.VolumePath)) return 87;   // ERROR_INVALID_PARAMETER
            letter = char.ToUpperInvariant(letter);
            if (letter < 'A' || letter > 'Z') return 87;

            string mountPoint = letter + ":\\";
            if (SetVolumeMountPoint(mountPoint, v.VolumePath)) return 0;
            return Marshal.GetLastWin32Error();
        }

        /// <summary>挑一个没被占用的盘符给无盘符的分区用。</summary>
        public static char PickFreeLetter(string preferred)
        {
            var used = new HashSet<char>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    string n = d.Name;
                    if (n.Length >= 2 && n[1] == ':') used.Add(char.ToUpperInvariant(n[0]));
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(preferred))
            {
                char p = char.ToUpperInvariant(preferred[0]);
                if (p >= 'A' && p <= 'Z' && !used.Contains(p)) return p;
            }
            for (char c = 'Z'; c >= 'D'; c--)
                if (!used.Contains(c)) return c;
            return '\0';
        }
    }

    public static class Fmt
    {
        public static string Size(ulong bytes)
        {
            if (bytes == 0) return "-";
            double b = bytes;
            if (b >= 1024.0 * 1024 * 1024) return (b / (1024.0 * 1024 * 1024)).ToString("0.#") + " GB";
            if (b >= 1024.0 * 1024) return (b / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (b / 1024.0).ToString("0.#") + " KB";
        }
    }
}
