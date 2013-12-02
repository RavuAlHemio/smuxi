// Smuxi - Smart MUltipleXed Irc
//
// Copyright (c) 2012 Carlos Martín Nieto <cmn@dwim.me>
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
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HtmlAgilityPack;
using Smuxi.Engine;

namespace Smuxi.Engine.VBulletinChatbox
{
    internal class MessageReceivedEventArgs : EventArgs
    {
        public ChatModel Chat { get; private set; }
        public MessageModel Message { get; private set; }

        public MessageReceivedEventArgs(ChatModel chat, MessageModel message) {
            Chat = chat;
            Message = message;
        }
    }

    internal class ErrorReceivedEventArgs : EventArgs
    {
        public ChatModel Chat { get; private set; }
        public HttpStatusCode HttpResponseCode { get; private set; }
        public string HttpBody { get; private set; }

        public ErrorReceivedEventArgs(ChatModel chat, HttpStatusCode httpCode, string body) {
            Chat = chat;
            HttpResponseCode = httpCode;
            HttpBody = body;
        }
    }

    internal class UserAppearedEventArgs : EventArgs
    {
        public ChatModel Chat { get; private set; }
        public ChatboxPersonModel Person { get; private set; }

        public UserAppearedEventArgs(ChatModel chat, ChatboxPersonModel person) {
            Chat = chat;
            Person = person;
        }
    }

    internal class ChatboxEventStream : IDisposable
    {
        public EventHandler<MessageReceivedEventArgs> MessageReceived;
        public EventHandler<ErrorReceivedEventArgs> ErrorReceived;
        public EventHandler<UserAppearedEventArgs> UserAppeared;

        public static readonly Regex TimestampPattern = new Regex("[[]([0-9][0-9]-[0-9][0-9]-[0-9][0-9], [0-9][0-9]:[0-9][0-9])[]]");
        public const string TimestampSpec = "dd'-'MM'-'yy', 'HH':'mm";
        const int HttpTimeout = 5000;

        ChatModel Chat { get; set; }
        Uri MessagesUri { get; set; }
        CookieContainer CookieJar { get; set; }
        ulong LastMessage { get; set; }
        bool StopNow { get; set; }
        string MyUsername { get; set; }
        Dictionary<ulong, ChatboxPersonModel> Users { get; set; }
        Thread WorkerThread { get; set; }

        public ChatboxEventStream(ChatModel chat, Uri forumUri, string myUsername, CookieContainer cookieJar)
        {
            Chat = chat;
            MessagesUri = new Uri(forumUri, "misc.php?show=ccbmessages");
            CookieJar = cookieJar;
            LastMessage = 0;
            StopNow = false;
            MyUsername = myUsername;
            Users = new Dictionary<ulong, ChatboxPersonModel>();
        }

        public void Start()
        {
            WorkerThread = new Thread(DoWork);
            WorkerThread.Start();
        }

        void DoWork()
        {
            while (!StopNow) {
                try {
                    try {
                        FetchNewMessages();
                    } catch (TimeoutException) {
                        // WGASA
                    } catch (WebException e) {
                        if (e.Status == WebExceptionStatus.ProtocolError) {
                            var resp = (HttpWebResponse) e.Response;
                            if (resp.StatusCode == HttpStatusCode.Unauthorized ||
                                resp.StatusCode == HttpStatusCode.Forbidden) {
                                if (ErrorReceived != null) {
                                    ErrorReceived(this, new ErrorReceivedEventArgs(Chat, resp.StatusCode, resp.StatusDescription));
                                }

                                return;
                            }
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(5));
                } catch (ThreadInterruptedException) {
                    // I remain apathetic
                }
            }
        }

        static ulong? FishOutId(HtmlNode topNode, string specifier)
        {
            ulong ret;

            var link = topNode.SelectSingleNode(".//a[contains(@href,\"" + specifier + "\")]");
            if (link == null) {
                return null;
            }
            var linkHref = link.GetAttributeValue("href", "");

            var specIdx = linkHref.IndexOf(specifier);
            if (specIdx == -1) {
                return null;
            }

            if (!ulong.TryParse(linkHref.Substring(specIdx + specifier.Length), out ret)) {
                return null;
            }

            return ret;
        }

        void FetchNewMessages()
        {
            var req = HttpWebRequest.Create(MessagesUri) as HttpWebRequest;
            req.CookieContainer = CookieJar;
            req.Timeout = HttpTimeout;
            HttpWebResponse res;
            lock (CookieJar) {
                res = req.GetResponse() as HttpWebResponse;
            }
            string gotthis = null;
            ulong newLastMessage = LastMessage;
            var newMessages = new List<MessageModel>();

            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.GetEncoding("ISO-8859-1"))) {
                // read it
                gotthis = sr.ReadToEnd();
                sr.Close();
            }

            // work around invalid XML in chatbox
            gotthis = "<messages>" + gotthis + "</messages>";

            // load into DOM
            var doc = new HtmlDocument();
            doc.LoadHtml(gotthis);

            // for each message
            foreach (HtmlNode msg in doc.DocumentNode.SelectNodes("/messages/tr")) {
                ulong msgId;
                ulong userId;

                // pick out the first td (message and user data)
                var metaTd = msg.SelectSingleNode("./td[1]");

                // find the link to the message and to the user
                var msgIdMaybe = FishOutId(metaTd, "misc.php?ccbloc=");
                var userIdMaybe = FishOutId(metaTd, "member.php?u=");
                if (!msgIdMaybe.HasValue || !userIdMaybe.HasValue) {
                    // bah, humbug
                    continue;
                }

                msgId = msgIdMaybe.Value;
                userId = userIdMaybe.Value;

                if (LastMessage >= msgId && LastMessage - msgId < 3000) {
                    // seen this already
                    continue;
                }
                if (newLastMessage < msgId) {
                    newLastMessage = msgId;
                }

                // fetch the timestamp
                var timeStamp = DateTime.Now;
                var stampMatch = TimestampPattern.Match(metaTd.InnerHtml);
                if (stampMatch.Success) {
                    var timeString = stampMatch.Groups[1].Value;
                    try {
                        timeStamp = DateTime.ParseExact(timeString, TimestampSpec, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal);
                    } catch (FormatException fe) {
                        // meh
                    }
                }

                // get the nickname
                var nick = metaTd.SelectSingleNode(".//a[contains(@href,\"member.php?u=\")]//text()").InnerText;

                // feed this to the message
                ChatboxPersonModel person;
                if (Users.ContainsKey(userId)) {
                    person = Users [userId];
                } else {
                    person = new ChatboxPersonModel(Chat.ProtocolManager.NetworkID, userId,
                                                    nick, Chat.ProtocolManager);
                    Users [userId] = person;

                    if (nick == MyUsername) {
                        person.IdentityNameColored.ForegroundColor = new TextColor(0, 0, 255);
                        person.IdentityNameColored.BackgroundColor = TextColor.None;
                        person.IdentityNameColored.Bold = true;
                    }

                    if (UserAppeared != null) {
                        UserAppeared(this, new UserAppearedEventArgs(Chat, person));
                    }
                }
                var outputBuilder = new MessageBuilder();
                outputBuilder.TimeStamp = timeStamp;
                if (nick == MyUsername) {
                    outputBuilder.Me = person;
                }
                outputBuilder.AppendSenderPrefix(person);
                outputBuilder.AppendHtmlMessage(msg.SelectSingleNode("./td[2]").InnerHtml.Trim(), MessagesUri);

                newMessages.Add(outputBuilder.ToMessage());
            }

            // call in reverse!
            if (MessageReceived != null) {
                newMessages.Reverse();
                foreach (var msg in newMessages) {
                    MessageReceived(this, new MessageReceivedEventArgs(Chat, msg));
                }
            }

            LastMessage = newLastMessage;
        }

        public void Dispose()
        {
            StopNow = true;
            WorkerThread.Interrupt();
        }

        public void UpdateNow()
        {
            WorkerThread.Interrupt();
        }
    }
}

