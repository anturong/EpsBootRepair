using System;
using System.Collections.Generic;

namespace EpsBootRepair
{
    /// <summary>一个分区能当引导目标时的类别（从分区的角度）。None = 不能当目标。</summary>
    public enum BootKind { None = 0, Esp, GptFat, MbrActive, MbrInactive }

    /// <summary>
    /// 一块物理盘，和它上面所有"能当引导目标"的分区。
    /// 高级修复是按盘选的：用户指定盘，引导分区由这块盘上的候选推出来。
    /// </summary>
    public sealed class DiskInfo
    {
        public int Number = -1;            // 物理盘号
        public string Model = "";          // 型号（VendorId+ProductId）；取不到为空
        public ulong Size;                 // 整盘容量；取不到为 0
        public readonly List<VolumeInfo> Candidates = new List<VolumeInfo>();

        public string ModelText
        {
            get { return Disks.ModelOf(Model); }
        }
    }

    /// <summary>
    /// 把卷列表折成盘列表。规则和原来的 TargetKind 一模一样，只是先按盘分组。
    /// 判定仍以 <see cref="VolumeInfo"/> 为准（ESP/活动分区都在卷上），盘只用来分组和显示。
    /// </summary>
    public static class Disks
    {
        /// <summary>探测物理盘的盘号上限。连续 3 个盘号不存在就停，凑不出空洞。</summary>
        const int MaxProbe = 64;
        const int MaxMiss = 3;

        /// <summary>
        /// 按盘分组。<paramref name="vols"/> 里出现过的盘号全部收进来，
        /// 另外补上"一个能挂载的卷都没有"的盘（裸盘 / 未分区），这样它在列表里至少露个面。
        /// </summary>
        public static List<DiskInfo> Enumerate(List<VolumeInfo> vols)
        {
            var byDisk = new Dictionary<int, DiskInfo>();
            var order = new List<int>();

            if (vols != null)
            {
                foreach (VolumeInfo v in vols)
                {
                    if (v == null || v.DiskNumber < 0) continue;
                    DiskInfo d;
                    if (!byDisk.TryGetValue(v.DiskNumber, out d))
                    {
                        d = new DiskInfo { Number = v.DiskNumber, Model = v.DiskModel, Size = v.DiskSize };
                        byDisk[v.DiskNumber] = d;
                        order.Add(v.DiskNumber);
                    }
                    if (IsTargetCandidate(v)) d.Candidates.Add(v);
                }
            }

            int miss = 0;
            for (int n = 0; n < MaxProbe && miss < MaxMiss; n++)
            {
                if (byDisk.ContainsKey(n)) { miss = 0; continue; }
                string model; ulong size;
                if (Volumes.QueryDisk(n, out model, out size))
                {
                    byDisk[n] = new DiskInfo { Number = n, Model = model, Size = size };
                    order.Add(n);
                    miss = 0;
                }
                else miss++;
            }

            // 候选按分区号排，显示顺序别随卷枚举顺序漂
            foreach (DiskInfo d in byDisk.Values)
                d.Candidates.Sort(delegate (VolumeInfo a, VolumeInfo b)
                                  { return a.PartitionNumber.CompareTo(b.PartitionNumber); });

            order.Sort();
            var list = new List<DiskInfo>(order.Count);
            foreach (int n in order) list.Add(byDisk[n]);
            return list;
        }

        /// <summary>取不到型号时的占位文字。</summary>
        public static string ModelOf(string model)
        {
            return string.IsNullOrEmpty(model) ? Lang.T("Disk.UnknownModel") : model;
        }

        public static string ModelOf(VolumeInfo v)
        {
            return v == null ? Lang.T("Disk.UnknownModel") : ModelOf(v.DiskModel);
        }

        /// <summary>分区的目标类别。判据与高级窗口原来的一致：ESP 优先，再看 MBR，最后 GPT FAT。</summary>
        public static BootKind KindOf(VolumeInfo v)
        {
            if (v == null) return BootKind.None;
            if (v.IsEsp()) return BootKind.Esp;
            if (v.Style == DiskStyle.Mbr) return v.IsActive ? BootKind.MbrActive : BootKind.MbrInactive;
            if (v.Style == DiskStyle.Gpt && v.IsFat) return BootKind.GptFat;
            return BootKind.None;
        }

        public static bool IsTargetCandidate(VolumeInfo v)
        {
            return KindOf(v) != BootKind.None;
        }

        /// <summary>分区级别的类别名（下拉里那一列）。</summary>
        public static string KindName(BootKind k)
        {
            switch (k)
            {
                case BootKind.Esp: return Lang.T("Target.KindEsp");
                case BootKind.GptFat: return Lang.T("Target.KindGptFat");
                case BootKind.MbrActive: return Lang.T("Target.KindMbrActive");
                case BootKind.MbrInactive: return Lang.T("Target.KindMbrInactive");
                default: return "";
            }
        }

        /// <summary>这块盘上候选里各有几个 ESP 路 / MBR 路。</summary>
        public static void CountKinds(DiskInfo d, out int esp, out int mbr)
        {
            esp = 0;
            mbr = 0;
            if (d == null) return;
            foreach (VolumeInfo v in d.Candidates)
            {
                BootKind k = KindOf(v);
                if (k == BootKind.Esp || k == BootKind.GptFat) esp++;
                else if (k == BootKind.MbrActive || k == BootKind.MbrInactive) mbr++;
            }
        }

        /// <summary>盘级的短标记：EPS / MBR / EPS+MBR / EPS×2 / MBR×2；没有候选就是空串。</summary>
        public static string KindText(DiskInfo d)
        {
            int esp, mbr;
            CountKinds(d, out esp, out mbr);
            var parts = new List<string>(2);
            if (esp > 0) parts.Add(Token("Disk.KindEsp", esp));
            if (mbr > 0) parts.Add(Token("Disk.KindMbr", mbr));
            return string.Join("+", parts.ToArray());
        }

        static string Token(string kindKey, int n)
        {
            string kind = Lang.T(kindKey);
            return n == 1 ? kind : Lang.T("Disk.KindCount", n, kind);
        }

        /// <summary>盘列表里的一行：HD0:型号(容量) |EPS。候选不唯一时给的是 EPS×2 / EPS+MBR，让用户接着指定那一个。</summary>
        public static string RowText(DiskInfo d)
        {
            string head = Lang.T("Disk.Row", d.Number, d.ModelText, Fmt.DiskSize(d.Size));
            string kind = KindText(d);
            return kind.Length == 0 ? head : Lang.T("Disk.RowKind", d.Number, d.ModelText, Fmt.DiskSize(d.Size), kind);
        }
    }
}
