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

        FrontendManager Frontend { get; set; }
        Uri ForumUri { get; set; }
        GroupChatModel BoxChat { get; set; }
        CookieJarWebClient BoxClient { get; set; }
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

            // create the client
            BoxClient = new CookieJarWebClient();
            BoxClient.Encoding = Encoding.GetEncoding("ISO-8859-1");

            LogIn();
            UpdateSecurityToken();

            EventStream = new ChatboxEventStream(BoxChat, ForumUri, Username, BoxClient.CookieJar);
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

            // login to forum
            var postValues = new System.Collections.Specialized.NameValueCollection();
            postValues.Add("vb_login_username", Username);
            postValues.Add("vb_login_password", Password);
            postValues.Add("cookieuser", "1");
            postValues.Add("s", "");
            postValues.Add("do", "login");
            postValues.Add("vb_login_md5password", "");
            postValues.Add("vb_login_md5password_utf", "");
            BoxClient.UploadValues(new Uri(ForumUri, "login.php?do=login"), "POST", postValues);

            OutputStatusMessage(_("Logged in."));
        }

        void UpdateSecurityToken()
        {
            Trace.Call();

            OutputStatusMessage(_("Fetching security token..."));

            var req = HttpWebRequest.Create(new Uri(ForumUri, "faq.php")) as HttpWebRequest;
            req.CookieContainer = BoxClient.CookieJar;
            var res = req.GetResponse() as HttpWebResponse;
            string gotthis;

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

        void TrySend(string message, int attempt)
        {
            Trace.Call(message, attempt);

            HttpWebRequest request = HttpWebRequest.Create(new Uri(ForumUri, "misc.php")) as HttpWebRequest;
            request.KeepAlive = false;
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.CookieContainer = BoxClient.CookieJar;

            string requestData = string.Format("do=cb_postnew&securitytoken={0}&vsacb_newmessage=", SecurityToken);
            foreach (char c in message) {
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-' || c == '_' || c == '.') {
                    requestData += c;
                } else if (c <= (char)0xFF) {
                    foreach (byte b in Encoding.GetEncoding("ISO-8859-1").GetBytes(c.ToString())) {
                        requestData += string.Format("%{0:X2}", b);
                    }
                } else {
                    // the Chatbox allows (URL-encoded) HTML escapes for this
                    requestData += string.Format("%26%23{0}%3B", (int)c);
                }
            }
            byte[] requestBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(requestData);

            request.ContentLength = requestBytes.Length;
            request.GetRequestStream().Write(requestBytes, 0, requestBytes.Length);
            var resp = request.GetResponse() as HttpWebResponse;

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
                        TrySend(message, 1);
                        break;
                    case 1:
                        // log in anew, fetch a new token and try again
                        LogIn();
                        UpdateSecurityToken();
                        TrySend(message, 2);
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
                TrySend(cmd.Parameter, 0);
            } else {
                TrySend(cmd.Data, 0);
            }
        }

        void CommandRelogin(CommandModel cmd)
        {
            Trace.Call(cmd);

            LogIn();
            UpdateSecurityToken();
        }

        void CommandRetoken(CommandModel cmd)
        {
            Trace.Call(cmd);

            UpdateSecurityToken();
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

