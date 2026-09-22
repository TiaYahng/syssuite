using System.Runtime.InteropServices;

namespace SysSuite.Core.Data;

public sealed partial class MsiAppEnumerator
{
    private static class NativeMethods
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiEnumProductsW(int productIndex, char[] productBuffer);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiGetProductInfoExW(
            string productCode,
            string? userSid,
            int context,
            string property,
            char[] value,
            ref int valueLength);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiOpenDatabaseW(
            string databasePath,
            nint persist,
            out uint database);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiDatabaseOpenViewW(
            uint database,
            string query,
            out uint view);

        [DllImport("msi.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int MsiViewExecute(uint view, uint record);

        [DllImport("msi.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int MsiViewFetch(uint view, out uint record);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiRecordGetStringW(
            uint record,
            uint field,
            char[] value,
            ref int valueLength);

        [DllImport("msi.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int MsiViewClose(uint view);

        [DllImport("msi.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int MsiCloseHandle(uint handle);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        public static extern int MsiGetComponentPathExW(
            string productCode,
            string componentId,
            string? userSid,
            int context,
            char[] path,
            ref int pathLength);

        [DllImport("msi.dll", ExactSpelling = true, SetLastError = true)]
        public static extern int MsiRecordReadStream(
            uint record,
            uint field,
            byte[] data,
            ref int dataLength);
    }
}
