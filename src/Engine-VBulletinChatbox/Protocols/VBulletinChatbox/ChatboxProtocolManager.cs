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

        Uri ForumUri { get; set; }
        GroupChatModel BoxChat { get; set; }
        CookieJarWebClient BoxClient { get; set; }
        CookieContainer CookieCrate { get; set; }
        ChatboxEventStream EventStream { get; set; }
        string SecurityToken { get; set; }

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
            Session.AddMessageToChat(BoxChat, mrea.Message);
        }

        void ShowError(object sender, ErrorReceivedEventArgs erea)
        {
            var msg = CreateMessageBuilder().AppendErrorText(_("Error reading from stream: {0}"), erea.HttpResponseCode).ToMessage();
            Session.AddMessageToChat(BoxChat, msg);
        }

        void AddUser(object sender, UserAppearedEventArgs uaea)
        {
            Session.AddPersonToGroupChat(BoxChat, uaea.Person);
        }

        public override void Connect(FrontendManager fm, ServerModel server)
        {
            Trace.Call(fm, server);

            ForumUri = new Uri(server.Hostname);
            BoxChat = new GroupChatModel(ForumUri.ToString(), "VBCB " + ForumUri, this);
            BoxChat.InitMessageBuffer(MessageBufferPersistencyType.Persistent);
            BoxChat.ApplyConfig(Session.UserConfig);
            Session.AddChat(BoxChat);
            Session.SyncChat(BoxChat);
            var msg = string.Format(_("Connecting to VBulletin Chatbox at {0}..."), ForumUri);
            if (fm != null) {
                fm.SetStatus(msg);
            }
            var bld = CreateMessageBuilder().AppendEventPrefix().AppendText(msg);
            Session.AddMessageToChat(BoxChat, bld.ToMessage());

            // create the client
            BoxClient = new CookieJarWebClient();
            BoxClient.Encoding = Encoding.GetEncoding("ISO-8859-1");
            var postValues = new System.Collections.Specialized.NameValueCollection();

            // login to forum
            postValues.Add("vb_login_username", server.Username);
            postValues.Add("vb_login_password", server.Password);
            postValues.Add("cookieuser", "1");
            postValues.Add("s", "");
            postValues.Add("do", "login");
            postValues.Add("vb_login_md5password", "");
            postValues.Add("vb_login_md5password_utf", "");
            BoxClient.UploadValues(new Uri(ForumUri, "login.php?do=login"), "POST", postValues);

            msg = string.Format(_("Connected to VBulletin Chatbox at {0}"), ForumUri);
            if (fm != null) {
                fm.SetStatus(msg);
            }
            bld = CreateMessageBuilder().AppendEventPrefix().AppendText(msg);
            Session.AddMessageToChat(BoxChat, bld.ToMessage());

            UpdateSecurityToken();

            EventStream = new ChatboxEventStream(BoxChat, ForumUri, BoxClient.CookieJar);
            EventStream.MessageReceived += ShowMessage;
            EventStream.ErrorReceived += ShowError;
            EventStream.UserAppeared += AddUser;
            EventStream.Start();
        }

        void UpdateSecurityToken()
        {
            var req = HttpWebRequest.Create(new Uri(ForumUri, "misc.php?do=cchatbox")) as HttpWebRequest;
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

            // find chatbox form and extract its action
            var cbform = doc.DocumentNode.SelectSingleNode("//form[contains(@action,\"securitytoken=\")]");
            var cbaction = cbform.GetAttributeValue("action", "");
            // find the token
            foreach (var keypair in cbaction.Split('&')) {
                if (keypair.StartsWith("amp;securitytoken=")) {
                    SecurityToken = keypair.Substring("amp;securitytoken=".Length);
                }
            }
        }

        public override bool Command(CommandModel cmd)
        {
            Trace.Call(cmd);

            if (!cmd.IsCommand) {
                HttpWebRequest request = HttpWebRequest.Create(new Uri(ForumUri, "misc.php")) as HttpWebRequest;
                request.KeepAlive = false;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.CookieContainer = BoxClient.CookieJar;

                string requestData = string.Format("do=cb_postnew&securitytoken={0}&vsacb_newmessage=", SecurityToken);
                foreach (char c in cmd.Data) {
                    if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-' || c == '_' || c == '.') {
                        requestData += c;
                    } else {
                        foreach (byte b in Encoding.GetEncoding("ISO-8859-1").GetBytes(c.ToString())) {
                            requestData += string.Format("%{0:X2}", b);
                        }
                    }
                }
                byte[] requestBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(requestData);

                request.ContentLength = requestBytes.Length;
                request.GetRequestStream().Write(requestBytes, 0, requestBytes.Length);
                var resp = request.GetResponse();

                using (var sr = new StreamReader(resp.GetResponseStream())) {
                    sr.ReadToEnd();
                }

                return true;
            } else if (cmd.Command == "help") {
                // no commands, but the frontend and engine have some
                return true;
            }

            return false;
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
            Session.RemoveChat(chat);

            if (EventStream != null) {
                EventStream.Dispose();
            }
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

