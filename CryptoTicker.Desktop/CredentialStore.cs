using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CryptoTicker.Desktop;

public static class CredentialStore
{
    private const int GenericCredential = 1;
    private const int LocalMachinePersistence = 2;

    public static void Save(string target, string secret)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrEmpty(secret))
        {
            return;
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        var pointer = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = pointer,
                Persist = LocalMachinePersistence,
                UserName = target
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    public static string? Read(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !CredRead(target, GenericCredential, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            return credential.CredentialBlobSize == 0 ? null : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
