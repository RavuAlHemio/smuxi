using System;
using System.Reflection;
using System.Threading;
using Smuxi.Common;
using Smuxi.Engine;

namespace Smuxi.Frontend.Http
{
    public class Frontend
    {
        private static readonly LogHelper Logger = new LogHelper(MethodBase.GetCurrentMethod().DeclaringType);

        public static EngineManager EngineManager { get; private set; }
        public static FrontendConfig FrontendConfig { get; private set; }
        public static FrontendManager FrontendManager { get; private set; }
        public static HttpUI HttpUI { get; private set; }
        public static bool IsLocalEngine => LocalSession != null && Session == LocalSession;
        public static Session LocalSession { get; private set; }
        public static Session Session { get; private set; }
        public static string UIName => "HTTP";
        public static UserConfig UserConfig { get; private set; }

        public static void Init()
        {
            Thread.CurrentThread.Name = "Main";
            Trace.Call();

            FrontendConfig = new FrontendConfig(UIName);
            FrontendConfig.Load();

            // set defaults
            if (FrontendConfig[UIName + "/CookieName"] == null) {
                FrontendConfig[UIName + "/CookieName"] = "SmuxiHttpSession";
            }
            if (FrontendConfig[UIName + "/Engine"] == null) {
                FrontendConfig[UIName + "/Engine"] = "local";
            }
            if (FrontendConfig[UIName + "/Password"] == null) {
                FrontendConfig[UIName + "/Password"] = "";
            }
            if (FrontendConfig[UIName + "/Tokens"] == null) {
                FrontendConfig[UIName + "/Tokens"] = "";
            }
            if (FrontendConfig[UIName + "/UriPrefix"] == null) {
                FrontendConfig[UIName + "/UriPrefix"] = "http://+:8080/";
            }
            if (FrontendConfig[UIName + "/Username"] == null) {
                FrontendConfig[UIName + "/Username"] = "";
            }

            FrontendConfig.Save();

            string engine = (string) FrontendConfig[UIName + "/Engine"];
            string uriPrefix = (string) FrontendConfig[UIName + "/UriPrefix"];

            HttpUI = new HttpUI(uriPrefix);

            if (FrontendConfig.IsCleanConfig) {
                Console.Error.WriteLine(
                    _("This frontend doesn't support initial configuration, sorry!"));
                Environment.Exit(1);
            } else if (String.IsNullOrWhiteSpace(engine) || engine == "local") {
                InitLocalEngine();
            } else {
                InitRemoteEngine(engine);
            }
        }

        public static void InitLocalEngine()
        {
            Engine.Engine.Init();
            LocalSession = new Session(Engine.Engine.Config,
                                       Engine.Engine.ProtocolManagerFactory,
                                       "local");
            Session = LocalSession;
            Session.RegisterFrontendUI(HttpUI);
            UserConfig = Session.UserConfig;
            ConnectEngineToUI();
        }

        public static void InitRemoteEngine(string engine)
        {
            EngineManager = new EngineManager(FrontendConfig, HttpUI);

            try {
                Logger.DebugFormat("Connecting to remote engine '{0}'", engine);
                EngineManager.Connect(engine);
                Logger.Debug("Connection established.");
            } catch (Exception ex) {
                Logger.Error(ex);
                Environment.Exit(1);
            }

            try {
                Session = EngineManager.Session;
                UserConfig = EngineManager.UserConfig;
                ConnectEngineToUI();
            } catch (Exception ex) {
                Logger.Error(ex);
                EngineManager.Disconnect();
                throw;
            }
        }

        public static void ConnectEngineToUI()
        {
            FrontendManager = Session.GetFrontendManager(HttpUI);
            FrontendManager.Sync();

            var commandManager = new CommandManager(Session);
            HttpUI.CommandManager = commandManager;

            HttpUI.Start();
        }

        public static void Quit()
        {
            if (FrontendManager != null) {
                FrontendManager.IsFrontendDisconnecting = true;
                if (IsLocalEngine) {
                    try {
                        Session.Shutdown();
                    } catch (Exception ex) {
                        Logger.Error("Quit(): Exception", ex);
                    }
                } else {
                    EngineManager?.Disconnect();
                }
            }

            Environment.Exit(0);
        }

        static string _(string msg)
        {
            return Mono.Unix.Catalog.GetString(msg);
        }
    }
}
