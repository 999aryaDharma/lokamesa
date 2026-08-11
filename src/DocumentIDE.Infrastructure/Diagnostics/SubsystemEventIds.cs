using System;

namespace DocumentIDE.Infrastructure.Diagnostics
{
    /// <summary>
    /// Event IDs and category definitions for core subsystems (Indexing, Word, Filesystem, Batch, Share, Security, Startup).
    /// </summary>
    public static class SubsystemEventIds
    {
        public static class Indexing
        {
            public const int Started = 1001;
            public const int Completed = 1002;
            public const int Failed = 1003;
        }

        public static class Word
        {
            public const int AutomationStarted = 2001;
            public const int AutomationCompleted = 2002;
            public const int AutomationFailed = 2003;
        }

        public static class Filesystem
        {
            public const int FileChanged = 3001;
            public const int FileMoved = 3002;
            public const int RecoveryCreated = 3003;
            public const int OperationFailed = 3004;
        }

        public static class Batch
        {
            public const int BatchStarted = 4001;
            public const int BatchProgress = 4002;
            public const int BatchCompleted = 4003;
            public const int BatchFailed = 4004;
        }

        public static class Share
        {
            public const int ShareCreated = 5001;
            public const int ShareAccessed = 5002;
            public const int ShareFailed = 5003;
        }

        public static class Security
        {
            public const int AuthAttempted = 6001;
            public const int AuthFailed = 6002;
            public const int RedactionApplied = 6003;
        }

        public static class Startup
        {
            public const int HostInitializing = 7001;
            public const int HostStarted = 7002;
            public const int HostFailed = 7003;
        }
    }
}
