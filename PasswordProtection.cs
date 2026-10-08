using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ContinueGame
{
    // Windows DPAPI binds the encrypted password to the current Windows account.
    public static class PasswordProtection
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob { public int Length; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string description,
            IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description,
            IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static string Protect(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(password), true));
        }

        public static string Unprotect(string protectedPassword)
        {
            if (string.IsNullOrEmpty(protectedPassword)) return "";
            byte[] plaintext = Transform(Convert.FromBase64String(protectedPassword), false);
            try { return Encoding.UTF8.GetString(plaintext); }
            finally { Array.Clear(plaintext, 0, plaintext.Length); }
        }

        private static byte[] Transform(byte[] bytes, bool protect)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Password protection requires Windows.");
            var input = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
            var output = new DataBlob();
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                bool success = protect
                    ? CryptProtectData(ref input, "ContinueGame", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero)
                {
                    if (!protect)
                        for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }
}
