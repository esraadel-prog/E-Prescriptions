using System;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using ePrescriptions.Core.Helpers;

namespace ePrescriptions.Core.Printing
{
    /// <summary>
    /// أدوات إرسال أوامر RAW مباشرة إلى طابعات Windows عبر winspool.drv أو منافذ COM التسلسلية.
    /// يدعم أوامر فتح درج النقدية ESC/POS وأوامر الملصقات ZPL/TSPL.
    /// </summary>
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType = "RAW";
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        /// <summary>
        /// إرسال مصفوفة بايتات بنوع RAW إلى طابعة Windows المعرفة
        /// </summary>
        public static bool SendBytesToPrinter(string printerName, byte[] bytes)
        {
            return SendBytesToPrinter(printerName, bytes, out _);
        }

        /// <summary>
        /// إرسال مصفوفة بايتات بنوع RAW إلى طابعة Windows مع إرجاع رسالة الخطأ عند الفشل
        /// </summary>
        public static bool SendBytesToPrinter(string printerName, byte[] bytes, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(printerName))
            {
                errorMessage = "لم يتم تحديد اسم الطابعة.";
                return false;
            }
            if (bytes == null || bytes.Length == 0)
            {
                errorMessage = "لا توجد بيانات لإرسالها إلى الطابعة.";
                return false;
            }

            IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"تعذر فتح الطابعة {printerName} (Win32: {err}).";
                    Logger.LogInfo(errorMessage, "RawPrinterHelper");
                    return false;
                }

                bool success = false;
                try
                {
                    var di = new DOCINFOA { pDocName = "ePrescriptions RAW Job", pDataType = "RAW" };
                    if (StartDocPrinter(hPrinter, 1, di))
                    {
                        try
                        {
                            if (StartPagePrinter(hPrinter))
                            {
                                success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out int written) && written == bytes.Length;
                                EndPagePrinter(hPrinter);
                            }
                        }
                        finally
                        {
                            EndDocPrinter(hPrinter);
                        }
                    }
                }
                finally
                {
                    ClosePrinter(hPrinter);
                }

                if (!success) errorMessage = "فشل إرسال البيانات إلى الطابعة.";
                return success;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
        }

        /// <summary>
        /// فتح درج النقدية عبر إرسال نبضة ESC/POS لطابعة الإيصالات المحددة
        /// </summary>
        public static bool OpenCashDrawer(string printerName, string? customCommandCsv = null)
        {
            try
            {
                // نفس محلل الإعدادات (فاضي = ESC p 0 25 250)؛ أمر غير صالح ما يتبعتش
                if (!EscPosCommand.TryParse(customCommandCsv, out byte[] drawerCmd, out string parseError))
                {
                    Logger.LogInfo(parseError, "OpenCashDrawer");
                    return false;
                }

                return SendBytesToPrinter(printerName, drawerCmd);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "OpenCashDrawer");
                return false;
            }
        }

        /// <summary>
        /// إرسال أوامر RAW عبر منفذ تسلسلي COM (مثال: COM1)
        /// </summary>
        public static bool SendToSerialPort(string portName, byte[] bytes, int baudRate = 9600)
        {
            try
            {
                using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                port.Open();
                port.Write(bytes, 0, bytes.Length);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SendToSerialPort");
                return false;
            }
        }
    }
}
