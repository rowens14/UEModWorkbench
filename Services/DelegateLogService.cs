using System;

namespace IrrModWorkbench.Services
{
    public class DelegateLogService : ILogService
    {
        private readonly Action<string> _log;

        public DelegateLogService(Action<string> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Log(string message) => _log(message);
    }
}