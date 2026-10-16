using System;
using System.Threading;

namespace ePrescriptions.Core.Helpers
{
    /// <summary>
    /// قفل النسخة الواحدة للبرنامج باستخدام Mutex لمنع تشغيل نسختين في وقت واحد والتعارض على ملف SQLite.
    /// </summary>
    public sealed class SingleInstanceMutex : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _hasHandle;
        private bool _disposed;

        public bool IsSingleInstance => _hasHandle;

        /// <summary>الاسم الأساسي للـ Mutex. لازم يطابق حرفياً الاسم المستخدم في CheckForMutexes داخل installer/e_prescriptions_installer.iss</summary>
        public const string MutexBaseName = "e_prescriptions_SingleInstance_Mutex_9F82A41C";

        /// <summary>الاسم الفعلي (Global\ عشان يشمل كل جلسات ويندوز على الجهاز)</summary>
        public const string DefaultMutexName = "Global\\" + MutexBaseName;

        public SingleInstanceMutex(string appUniqueId = DefaultMutexName)
        {
            _mutex = new Mutex(true, appUniqueId, out _hasHandle);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_hasHandle)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ObjectDisposedException ex) { Logger.LogInfo("الـ Mutex اتحرّر بالفعل: " + ex.Message, "SingleInstanceMutex.Dispose"); }
            }
            _mutex.Dispose();
            _disposed = true;
        }
    }
}
