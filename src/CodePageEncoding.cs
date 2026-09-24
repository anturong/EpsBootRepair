using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EpsBootRepair
{
    /// <summary>
    /// 用 MultiByteToWideChar / WideCharToMultiByte 包一个老代码页编码器。
    /// .NET 8 默认只带 Unicode/UTF/ASCII/Latin1，936 要靠 NuGet 的
    /// System.Text.Encoding.CodePages —— PE 里要零依赖、目标机也没网，所以自己包一层核。
    /// diskpart 往重定向的 stdout 写老代码页 ANSI(936)，不解码就是乱码。
    /// </summary>
    public sealed class CodePageEncoding : Encoding
    {
        const uint CP_ACP_FLAG = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int MultiByteToWideChar(uint codePage, uint flags, byte[] multiByteStr, int multiByteCount,
                                              [Out] char[] wideCharStr, int wideCharCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int WideCharToMultiByte(uint codePage, uint flags, char[] wideCharStr, int wideCharCount,
                                              [Out] byte[] multiByteStr, int multiByteCount,
                                              IntPtr defaultChar, IntPtr usedDefaultChar);

        readonly uint _cp;

        public CodePageEncoding(int codePage)
        {
            _cp = (uint)codePage;
        }

        /// <summary>优先用框架自带的（如果注册过 CodePages provider），否则退回内核转换。</summary>
        public static Encoding Get(int codePage)
        {
            try { return Encoding.GetEncoding(codePage); }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            return new CodePageEncoding(codePage);
        }

        public override int GetByteCount(char[] chars, int index, int count)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            if (count == 0) return 0;
            char[] sub = Slice(chars, index, count);
            int n = WideCharToMultiByte(_cp, CP_ACP_FLAG, sub, count, null, 0, IntPtr.Zero, IntPtr.Zero);
            return n > 0 ? n : FallbackCount(count);
        }

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            if (chars == null) throw new ArgumentNullException("chars");
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (charCount == 0) return 0;

            char[] sub = Slice(chars, charIndex, charCount);
            int need = WideCharToMultiByte(_cp, CP_ACP_FLAG, sub, charCount, null, 0, IntPtr.Zero, IntPtr.Zero);
            if (need <= 0)
            {
                Encoding fb = Encoding.Latin1;
                return fb.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
            }

            byte[] tmp = new byte[need];
            int n = WideCharToMultiByte(_cp, CP_ACP_FLAG, sub, charCount, tmp, need, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return 0;
            Array.Copy(tmp, 0, bytes, byteIndex, n);
            return n;
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (count == 0) return 0;
            byte[] sub = Slice(bytes, index, count);
            int n = MultiByteToWideChar(_cp, CP_ACP_FLAG, sub, count, null, 0);
            return n > 0 ? n : count;
        }

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (chars == null) throw new ArgumentNullException("chars");
            if (byteCount == 0) return 0;

            byte[] sub = Slice(bytes, byteIndex, byteCount);
            int need = MultiByteToWideChar(_cp, CP_ACP_FLAG, sub, byteCount, null, 0);
            if (need <= 0)
                return Encoding.Latin1.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

            char[] tmp = new char[need];
            int n = MultiByteToWideChar(_cp, CP_ACP_FLAG, sub, byteCount, tmp, need);
            if (n <= 0) return 0;
            Array.Copy(tmp, 0, chars, charIndex, n);
            return n;
        }

        public override int GetMaxByteCount(int charCount) { return charCount * 2 + 8; }

        public override int GetMaxCharCount(int byteCount) { return byteCount + 4; }

        static char[] Slice(char[] src, int index, int count)
        {
            var a = new char[count];
            Array.Copy(src, index, a, 0, count);
            return a;
        }

        static byte[] Slice(byte[] src, int index, int count)
        {
            var a = new byte[count];
            Array.Copy(src, index, a, 0, count);
            return a;
        }

        static int FallbackCount(int charCount) { return charCount; }
    }
}
