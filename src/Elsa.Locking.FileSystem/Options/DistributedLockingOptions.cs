using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Locking.FileSystem.Options
{
    public class DistributedLockingOptions
    {
        /// <summary>
        /// The maximum amount of time to wait before giving up trying to acquire a lock. Defaults to 10 minutes.
        /// </summary>
        public TimeSpan LockAcquisitionTimeout { get; set; } = TimeSpan.FromMinutes(10);
    }
}
