// Smuxi - Smart MUltipleXed Irc
//
// Copyright (c) 2013 Ondřej Hošek <ondra.hosek@gmail.com>
//
// Full GPL License: <http://www.gnu.org/licenses/gpl.txt>
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Smuxi.Common;
using HtmlAgilityPack;

namespace Smuxi.Engine.VBulletinChatbox
{
    [ProtocolManagerInfo(Name = "VBulletin Chatbox", Description = "VBulletin Forum Chatbox Plugin", Alias = "VBulletin Chatbox")]
    public class ChatboxProtocolManager : ProtocolManagerBase
    {
#if LOG4NET
        static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
#endif
        const int HttpTimeout = 10000;

        FrontendManager Frontend { get; set; }
        Uri ForumUri { get; set; }
        GroupChatModel BoxChat { get; set; }
        CookieContainer CookieJar { get; set; }
        ChatboxEventStream EventStream { get; set; }
        string SecurityToken { get; set; }
        string Username { get; set; }
        string Password { get; set; }

        public override ChatModel Chat {
            get {
                return BoxChat;
            }
        }

        public override string NetworkID {
            get {
                return ForumUri.ToString();
            }
        }

        public override string Protocol {
            get {
                return "VBulletinChatbox";
            }
        }

        public ChatboxProtocolManager(Session session) : base(session)
        {
            Trace.Call(session);
        }

        void ShowMessage(object sender, MessageReceivedEventArgs mrea)
        {
            Trace.Call(sender, mrea);

            Session.AddMessageToChat(BoxChat, mrea.Message);
        }

        void ShowError(object sender, ErrorReceivedEventArgs erea)
        {
            Trace.Call(sender, erea);

            var msg = CreateMessageBuilder().AppendErrorText(_("Error reading from stream: {0}"), erea.HttpResponseCode).ToMessage();
            Session.AddMessageToChat(BoxChat, msg);
        }

        void AddUser(object sender, UserAppearedEventArgs uaea)
        {
            Trace.Call(sender, uaea);

            Session.AddPersonToGroupChat(BoxChat, uaea.Person);
        }

        public override void Connect(FrontendManager fm, ServerModel server)
        {
            Trace.Call(fm, server);

            Username = server.Username;
            Password = server.Password;
            Frontend = fm;

            ForumUri = new Uri(server.Hostname);
            BoxChat = new GroupChatModel(ForumUri.ToString(), "VBCB " + ForumUri, this);
            BoxChat.InitMessageBuffer(MessageBufferPersistencyType.Persistent);
            BoxChat.ApplyConfig(Session.UserConfig);
            Session.AddChat(BoxChat);
            Session.SyncChat(BoxChat);

            CookieJar = new CookieContainer();

            LogIn();
            UpdateSecurityToken();

            EventStream = new ChatboxEventStream(BoxChat, ForumUri, Username, CookieJar);
            EventStream.MessageReceived += ShowMessage;
            EventStream.ErrorReceived += ShowError;
            EventStream.UserAppeared += AddUser;
            EventStream.Start();
        }

        void OutputStatusMessage(string message)
        {
            if (Frontend != null) {
                Frontend.SetStatus(message);
            }
            var bld = CreateMessageBuilder().AppendEventPrefix().AppendText(message);
            Session.AddMessageToChat(BoxChat, bld.ToMessage());
        }

        void LogIn()
        {
            Trace.Call();

            OutputStatusMessage(string.Format(_("Logging in to VBulletin Chatbox at {0}..."), ForumUri));

            // create temporary client
            var boxClient = new CookieJarWebClient();
            boxClient.Encoding = Encoding.GetEncoding("ISO-8859-1");
            boxClient.CookieJar = CookieJar;
            boxClient.Timeout = HttpTimeout;

            // login to forum
            var postValues = new System.Collections.Specialized.NameValueCollection();
            postValues.Add("vb_login_username", Username);
            postValues.Add("vb_login_password", Password);
            postValues.Add("cookieuser", "1");
            postValues.Add("s", "");
            postValues.Add("do", "login");
            postValues.Add("vb_login_md5password", "");
            postValues.Add("vb_login_md5password_utf", "");

            lock (CookieJar) {
                boxClient.UploadValues(new Uri(ForumUri, "login.php?do=login"), "POST", postValues);
            }

            OutputStatusMessage(_("Logged in."));
        }

        void UpdateSecurityToken()
        {
            Trace.Call();

            OutputStatusMessage(_("Fetching security token..."));

            var req = HttpWebRequest.Create(new Uri(ForumUri, "faq.php")) as HttpWebRequest;
            req.CookieContainer = CookieJar;
            req.Timeout = HttpTimeout;
            HttpWebResponse res;
            string gotthis;
            try {
                lock (CookieJar) {
                    res = req.GetResponse() as HttpWebResponse;
                }
            } catch (WebException) {
                OutputStatusMessage(_("Security token fetching timed out."));
                return;
            }

            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.GetEncoding("ISO-8859-1"))) {
                // read it
                gotthis = sr.ReadToEnd();
                sr.Close();
            }

            // load into DOM
            var doc = new HtmlDocument();
            doc.LoadHtml(gotthis);

            // find any form's security token field
            var tokin = doc.DocumentNode.SelectSingleNode("//input[@name=\"securitytoken\"]");
            SecurityToken = tokin.GetAttributeValue("value", "");

            OutputStatusMessage(_("Security token fetched."));
        }

        void TrySend(string message)
        {
            Trace.Call(message);

            var thd = new Thread(() => {RealTrySend(message, 0);});
            thd.Start();
        }

        string EncodeOutgoingString(string message)
        {
            Trace.Call(message);

            var serverEncoding = Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var ret = new StringBuilder();
            char? prev = null;
            foreach (char c in message) {
                if (prev >= 0xD800 && prev <= 0xDBFF && (c < 0xDC00 || c > 0xDFFF)) {
                    // lead code point without trail code point; skip
                } else if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-' || c == '_' || c == '.') {
                    // URL-safe character
                    ret.Append(c);
                } else if (c >= 0xD800 && c <= 0xDBFF) {
                    // lead code point; handle this next time around
                } else if (c >= 0xDC00 && c <= 0xDFFF) {
                    // trail code point (and the previous is a lead code point)

                    // decode UTF-16 to real codepoint
                    uint top10 = ((uint) prev - 0xD800) << 10;
                    uint btm10 = ((uint) c - 0xDC00);
                    uint realpoint = (top10 | btm10) + 0x10000;

                    // URL-encode the HTML escape of this
                    ret.AppendFormat("%26%23{0}%3B", realpoint);
                } else {
                    try {
                        // character in the server's encoding
                        foreach (var b in serverEncoding.GetBytes(c.ToString())) {
                            ret.AppendFormat("%{0:X2}", (int) b);
                        }
                    } catch (EncoderFallbackException) {
                        // Unicode BMP character
                        // the Chatbox allows (URL-encoded) HTML escapes for this
                        ret.AppendFormat("%26%23{0}%3B", (int) c);
                    }
                }
                prev = c;
            }

            return ret.ToString();
        }

        void RealTrySend(string message, int attempt)
        {
            Trace.Call(message, attempt);

            HttpWebRequest request = HttpWebRequest.Create(new Uri(ForumUri, "misc.php")) as HttpWebRequest;
            request.KeepAlive = false;
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.CookieContainer = CookieJar;
            request.Timeout = HttpTimeout;

            string requestData = string.Format("do=cb_postnew&securitytoken={0}&vsacb_newmessage={1}", SecurityToken, EncodeOutgoingString(message));
            byte[] requestBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(requestData);

            HttpWebResponse resp;
            request.ContentLength = requestBytes.Length;
            try {
                lock (CookieJar) {
                    request.GetRequestStream().Write(requestBytes, 0, requestBytes.Length);
                    resp = request.GetResponse() as HttpWebResponse;
                }
            } catch (WebException) {
                // ffs, try again
                if (attempt < 5) {
                    RealTrySend(message, attempt + 1);
                } else {
                    var msg = CreateMessageBuilder()
                              .AppendErrorText(_("Failed to send message due to timeout"))
                              .ToMessage();
                    Session.AddMessageToChat(BoxChat, msg);
                }
                return;
            }

            string respBody;
            using (var sr = new StreamReader(resp.GetResponseStream())) {
                respBody = sr.ReadToEnd();
            }

            if ((int)resp.StatusCode != 200 || respBody.Length != 0) {
                // something failed
                switch (attempt) {
                    case 0:
                        // fetch a new token and try again
                        UpdateSecurityToken();
                        RealTrySend(message, 1);
                        break;
                    case 1:
                        // log in anew, fetch a new token and try again
                        LogIn();
                        UpdateSecurityToken();
                        RealTrySend(message, 2);
                        break;
                    default:
                        // guess not
                        var msg = CreateMessageBuilder()
                                  .AppendErrorText(_("Failed to send message; HTTP error code: [{0}] {1}"), (int)resp.StatusCode, resp.StatusCode)
                                  .ToMessage();
                        Session.AddMessageToChat(BoxChat, msg);
                        break;
                }
            }

            // trigger the receiving thread to fetch an update immediately
            EventStream.UpdateNow();
        }

        void CloseTheChat()
        {
            Trace.Call();

            Session.RemoveChat(BoxChat);

            if (EventStream != null) {
                EventStream.Dispose();
            }
        }

        void CommandSend(CommandModel cmd)
        {
            Trace.Call(cmd);

            if (cmd.IsCommand && cmd.Command == "send") {
                TrySend(cmd.Parameter);
            } else {
                TrySend(cmd.Data);
            }
        }

        void CommandRelogin(CommandModel cmd)
        {
            Trace.Call(cmd);

            var thd = new Thread(() => {
                LogIn();
                UpdateSecurityToken();
            });
            thd.Start();
        }

        void CommandRetoken(CommandModel cmd)
        {
            Trace.Call(cmd);

            var thd = new Thread(UpdateSecurityToken);
            thd.Start();
        }

        void CommandQuit(CommandModel cmd)
        {
            Trace.Call(cmd);

            if (cmd.Chat != BoxChat) {
                throw new ArgumentException("Unknown chat to close.");
            }

            CloseTheChat();
        }

        void CommandHelp(CommandModel cmd)
        {
            Trace.Call(cmd);

            var builder = CreateMessageBuilder();
            // TRANSLATOR: the line below is used as a section header
            // for a list of commands
            builder.AppendHeader(_("VBulletin Chatbox Commands"));
            Session.AddMessageToFrontend(cmd, builder.ToMessage());

            string[] help = {
                "help",
                "me",
                "quit",
                "relogin",
                "retoken",
                "say"
            };

            foreach (var line in help) {
                builder = CreateMessageBuilder();
                builder.AppendEventPrefix();
                builder.AppendText(line);
                Session.AddMessageToFrontend(cmd, builder.ToMessage());
            }
        }

        public override bool Command(CommandModel cmd)
        {
            Trace.Call(cmd);

            if (!cmd.IsCommand || cmd.Command == "me" || cmd.Command == "say") {
                CommandSend(cmd);
            } else if (cmd.Command == "help") {
                CommandHelp(cmd);
            } else if (cmd.Command == "relogin") {
                CommandRelogin(cmd);
            } else if (cmd.Command == "retoken") {
                CommandRetoken(cmd);
            } else {
                return false;
            }

            return true;
        }

        public override void Reconnect(FrontendManager fm)
        {
            Trace.Call(fm);
        }

        public override void Disconnect(FrontendManager fm)
        {
            Trace.Call(fm);
        }

        public override IList<GroupChatModel> FindGroupChats(GroupChatModel filter)
        {
            Trace.Call(filter);
            return new List<GroupChatModel>();
        }

        public override void OpenChat(FrontendManager fm, ChatModel chat)
        {
            Trace.Call(fm, chat);
        }

        public override void CloseChat(FrontendManager fm, ChatModel chat)
        {
            Trace.Call(fm, chat);

            if (chat != BoxChat) {
                throw new ArgumentException("Unknown chat to close.");
            }

            CloseTheChat();
        }

        public override void SetPresenceStatus(PresenceStatus status, string message)
        {
            Trace.Call(status, message);
        }

        public override string ToString()
        {
            return ForumUri.ToString();
        }

        public override void Dispose()
        {
            Trace.Call();

            if (EventStream != null) {
                EventStream.Dispose();
            }

            base.Dispose();
        }

        private static string _(string msg)
        {
            return LibraryCatalog.GetString(msg, "smuxi-engine-vbulletinchatbox");
        }
    }
}

