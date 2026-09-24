using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EpsBootRepair
{
    public enum LogKind { Info, Command, Ok, Warn, Error, Tool }

    /// <summary>
    /// 跑一条外部命令，stdout/stderr 实时回显。
    ///
    /// 输出**不设** StandardOutputEncoding，直接读原始字节自己解。原因：这一批工具里
    /// 编码并不统一 —— bcdboot 往重定向的 stdout 写的是 UTF-16LE
    /// （"Boot files successfully created." 会变成 B\0o\0o\0t\0... 全带 NUL），
    /// diskpart 写的是老代码页 ANSI(936)。父进程事先没法知道是哪一种，
    /// 只能逐行看字节判定：偶数长度且 NUL 占比很高就是 UTF-16LE，否则按 ANSI 解。
    /// </summary>
    public sealed class Runner
    {
        readonly Action<string, LogKind> _log;
        readonly Encoding _ansi;
        Process _current;

        public Runner(Action<string, LogKind> log, Encoding ansi)
        {
            _log = log;
            _ansi = ansi;
        }

        /// <summary>中止当前子进程（用于卡死的工具）。</summary>
        public void Kill()
        {
            try
            {
                Process p = _current;
                if (p != null && !p.HasExited) p.Kill();
            }
            catch { }
        }

        public int Run(string exe, string args, string workDir, string stdin = null, int timeoutMs = 0,
                       List<string> capture = null)
        {
            if (string.IsNullOrEmpty(exe)) return -1;
            _log(exe + " " + args, LogKind.Command);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null
            };
            if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir)) psi.WorkingDirectory = workDir;

            // 工具有时会去"自己所在目录"找同伴文件，把工具目录塞进子进程 PATH 兜一层
            string dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(dir))
            {
                string old = psi.EnvironmentVariables["PATH"];
                psi.EnvironmentVariables["PATH"] = dir + ";" + (old ?? "");
            }

            var p = new Process { StartInfo = psi };
            _current = p;
            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                _log(ex.Message, LogKind.Error);
                _current = null;
                return -1;
            }

            try
            {
                Thread tOut = Pump(p.StandardOutput.BaseStream, LogKind.Info, capture);
                Thread tErr = Pump(p.StandardError.BaseStream, LogKind.Warn, capture);

                if (stdin != null)
                {
                    try { p.StandardInput.Write(stdin); p.StandardInput.Flush(); } catch { }
                    try { p.StandardInput.Close(); } catch { }
                }

                if (timeoutMs > 0 && !p.WaitForExit(timeoutMs))
                {
                    _log(Lang.T("Error.Timeout", Path.GetFileName(exe)), LogKind.Error);
                    try { p.Kill(); } catch { }
                }
                p.WaitForExit();
                tOut.Join(5000);
                tErr.Join(5000);

                // 每条命令的退出码都记一行。有些工具成功失败都不吐一个字，
                // 不打出来就没法判断它到底跑没跑、跑成没跑成。
                _log(Lang.T("Log.Exit", p.ExitCode, Path.GetFileName(exe)), LogKind.Info);
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                _log(ex.Message, LogKind.Error);
                return -1;
            }
            finally
            {
                _current = null;
                p.Dispose();
            }
        }

        /// <summary>逐行读一条字节流并解码。返回线程，调用方等它排空。</summary>
        Thread Pump(Stream s, LogKind kind, List<string> capture)
        {
            var t = new Thread(delegate ()
            {
                var line = new MemoryStream(256);
                var buf = new byte[4096];
                try
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            byte b = buf[i];
                            if (b == (byte)'\n') { Emit(line, kind, capture); line.SetLength(0); }
                            else if (b != (byte)'\r') line.WriteByte(b);
                        }
                    }
                    if (line.Length > 0) Emit(line, kind, capture);
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        void Emit(MemoryStream line, LogKind kind, List<string> capture)
        {
            byte[] raw = line.ToArray();
            if (raw.Length == 0) return;
            string text = Decode(raw);
            _log(text, kind);
            if (capture != null) { lock (capture) capture.Add(text); }
        }

        /// <summary>
        /// UTF-16LE 的英文行里有一半字节是 0x00；ANSI 的中文行里几乎没有。
        /// 奇数字节的一定不是 UTF-16LE（半个编码单元）。阈值取 20% 留足余量。
        /// </summary>
        string Decode(byte[] raw)
        {
            int nul = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i] == 0) nul++;
            bool wide = raw.Length >= 4 && raw.Length % 2 == 0 && nul * 5 >= raw.Length;
            return wide ? Encoding.Unicode.GetString(raw) : _ansi.GetString(raw);
        }
    }
}
