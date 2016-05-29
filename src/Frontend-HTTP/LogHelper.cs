using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace Smuxi.Frontend.Http
{
    internal class LogHelper
    {
#if LOG4NET
        protected ILog Logger { get; set; }
#endif
        protected string LogSourceName { get; set; }

        public LogHelper(Type logSourceType)
            : this(logSourceType.FullName)
        {
        }

        public LogHelper(string logSourceName)
        {
            if (String.IsNullOrWhiteSpace(logSourceName)) {
                throw new ArgumentNullException(nameof(logSourceName));
            }
            LogSourceName = logSourceName;
#if LOG4NET
            Logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
#endif
        }

        public void Debug(object message, Exception ex = null)
        {
#if LOG4NET
            Logger.Debug(message, ex);
#endif
        }

        public void DebugFormat(string format, params object[] arguments)
        {
#if LOG4NET
            Logger.DebugFormat(CultureInfo.InvariantCulture, format,
                               arguments);
#endif
        }

        public void Error(object message, Exception ex = null)
        {
#if LOG4NET
            Logger.Error(message, ex);
#endif
        }

        public void ErrorFormat(string format, params object[] arguments)
        {
#if LOG4NET
            Logger.ErrorFormat(CultureInfo.InvariantCulture, format,
                               arguments);
#endif
        }

        public void Fatal(object message, Exception ex = null)
        {
#if LOG4NET
            Logger.Fatal(message, ex);
#endif
        }

        public void FatalFormat(string format, params object[] arguments)
        {
#if LOG4NET
            Logger.FatalFormat(CultureInfo.InvariantCulture, format,
                               arguments);
#endif
        }
    }
}
