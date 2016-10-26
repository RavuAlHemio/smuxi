using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using DotLiquid;
using Newtonsoft.Json.Linq;
using RavuAlHemio.HttpDispatcher;
using Smuxi.Common;
using Smuxi.Engine;
using Smuxi.Frontend.Http.Drops;

namespace Smuxi.Frontend.Http
{
    [Responder]
    public class HttpUI : PermanentRemoteObject, IFrontendUI
    {
        private static readonly LogHelper Logger = new LogHelper(MethodBase.GetCurrentMethod().DeclaringType);

        protected HttpAuthenticator Authenticator { get; }
        protected Dictionary<ChatModel, HttpChat> ChatFrontends { get; }
        protected List<ChatModel> Chats { get; }
        protected object CollectionLock { get; }
        public CommandManager CommandManager { get; set; }
        public Dictionary<string, string> ExtensionsToMimeTypes { get; }
        public DistributingHttpListener Listener { get; set; }
        public Templates Templates { get; set; }
        public int Version => 0;

        public HttpUI(string uriPrefix)
        {
            Authenticator = new HttpAuthenticator();
            ChatFrontends = new Dictionary<ChatModel, HttpChat>();
            Chats = new List<ChatModel>();
            CollectionLock = new object();
            ExtensionsToMimeTypes = new Dictionary<string, string>
            {
                [".css"] = "text/css",
                [".js"] = "text/javascript"
            };

            Listener = new DistributingHttpListener(uriPrefix);
            Listener.AddResponder(this);
            Listener.ResponderException += (sender, args) =>
            {
                Logger.Error("exception thrown while handling request", args.Exception);
            };

            Templates = new Templates();
        }

        public void Start()
        {
            Listener.Start();
        }

        public void AddChat(ChatModel chat)
        {
            Trace.Call(chat);
            try {
                lock (CollectionLock) {
                    Chats.Add(chat);
                    ChatFrontends[chat] = new HttpChat
                    {
                        Name = chat.Name,
                        IsSystemChat = (chat.ChatType == ChatType.Session
                                        || chat.ChatType == ChatType.Protocol)
                    };

                    var groupChat = chat as GroupChatModel;
                    if (groupChat != null) {
                        if (groupChat.Persons != null) {
                            ChatFrontends[chat].ReplaceAllParticipants(groupChat.Persons.Values);
                        }
                        ChatFrontends[chat].UpdateTopic(groupChat.Topic);
                    }
                }
            } catch (Exception exc) {
                Logger.Error("exception when adding chat", exc);
            }
        }

        public void EnableChat(ChatModel chat)
        {
            Trace.Call(chat);
        }

        public void DisableChat(ChatModel chat)
        {
            Trace.Call(chat);
        }

        public void AddMessageToChat(ChatModel chat, MessageModel msg)
        {
            Trace.Call(chat, msg);

            try {
                FrontendForChat(chat).AddMessage(msg);
            } catch (Exception exc) {
                Logger.Error("exception when adding message to chat", exc);
            }
        }

        public void RemoveChat(ChatModel chat)
        {
            Trace.Call(chat);

            try {
                lock (CollectionLock) {
                    Chats.Remove(chat);
                    ChatFrontends.Remove(chat);
                }
            } catch (Exception exc) {
                Logger.Error("exception when removing message from chat", exc);
            }
        }

        public void SyncChat(ChatModel chat)
        {
            Trace.Call(chat);

            try {
                HttpChat frontend = FrontendForChat(chat);

                frontend.ReplaceAllMessages(chat.Messages);

                var groupChat = chat as GroupChatModel;
                if (groupChat != null) {
                    frontend.ReplaceAllParticipants(groupChat.Persons.Values);
                }
                Frontend.FrontendManager.AddSyncedChat(chat);
            } catch (Exception exc) {
                Logger.Error("exception when syncing chat", exc);
            }
        }

        public void AddPersonToGroupChat(GroupChatModel groupChat, PersonModel person)
        {
            Trace.Call(groupChat, person);

            try {
                FrontendForChat(groupChat).AddParticipant(person);
            } catch (Exception exc) {
                Logger.Error("exception when adding person to group chat", exc);
            }
        }

        public void UpdatePersonInGroupChat(GroupChatModel groupChat, PersonModel oldPerson,
            PersonModel newPerson)
        {
            Trace.Call(groupChat, oldPerson, newPerson);

            try {
                HttpChat frontend = FrontendForChat(groupChat);
                frontend.RemoveParticipant(oldPerson);
                frontend.AddParticipant(newPerson);
            } catch (Exception exc) {
                Logger.Error("exception when updating person in group chat", exc);
            }
        }

        public void UpdateTopicInGroupChat(GroupChatModel groupChat, MessageModel topic)
        {
            Trace.Call(groupChat, topic);

            try {
                FrontendForChat(groupChat).UpdateTopic(topic);
            } catch (Exception exc) {
                Logger.Error("exception when updating topic in group chat", exc);
            }
        }

        public void RemovePersonFromGroupChat(GroupChatModel groupChat, PersonModel person)
        {
            Trace.Call(groupChat, person);

            try {
                FrontendForChat(groupChat).RemoveParticipant(person);
            } catch (Exception exc) {
                Logger.Error("exception when removing person from group chat", exc);
            }
        }

        public void SetNetworkStatus(string status)
        {
            Trace.Call(status);
        }

        public void SetStatus(string status)
        {
            Trace.Call(status);
        }

        [Endpoint("/", Method = "GET")]
        public void LandingPage(HttpListenerContext ctx)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            List<ChatTabDrop> chats;
            lock (CollectionLock) {
                chats = Chats
                    .Select((c, i) => new ChatTabDrop(i, ChatFrontends[c]))
                    .ToList();
            }
            string result = Templates.LandingPage.Render(Hash.FromAnonymousObject(new
            {
                chat_tabs = chats
            }));

            ReturnHtml(ctx, result);
        }

        [Endpoint("/{chatIndex}", Method = "GET")]
        public void ChatPage(HttpListenerContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            int chatCount;
            HttpChat httpChat;
            List<ChatTabDrop> chats, highlightedChats;

            lock (CollectionLock) {
                chatCount = Chats.Count;
            }

            if (chatIndex < 0 || chatIndex >= chatCount) {
                ReturnNotFound(ctx);
                return;
            }

            lock (CollectionLock) {
                httpChat = ChatFrontends[Chats[chatIndex]];
                chats = Chats
                    .Select((c, i) => new ChatTabDrop(i, ChatFrontends[c]))
                    .ToList();
                highlightedChats = Chats
                    .Select((c, i) => Tuple.Create(ChatFrontends[c], i))
                    .Where(t => t.Item1.UnseenHighlightMessages > 0)
                    .Select(t => new ChatTabDrop(t.Item2, t.Item1))
                    .ToList();
            }

            ChatDrop chatDrop = new ChatDrop(httpChat);

            string result = Templates.ChatPage.Render(Hash.FromAnonymousObject(new
            {
                chat = chatDrop,
                chat_tabs = chats,
                highlighted_chat_tabs = highlightedChats,
                chat_index = chatIndex.ToString(CultureInfo.InvariantCulture)
            }));

            ReturnHtml(ctx, result);
        }

        [Endpoint("/{chatIndex}/message", Method = "POST")]
        public void PostMessage(HttpListenerContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            int chatCount;
            ChatModel chat;

            lock (CollectionLock) {
                chatCount = Chats.Count;
            }

            if (chatIndex < 0 || chatIndex >= chatCount) {
                ReturnNotFound(ctx);
                return;
            }

            lock (CollectionLock) {
                chat = Chats[chatIndex];
            }

            string query = ReadRequestBody(ctx.Request.InputStream);
            Dictionary<string, string> keysValues = HttpUtil.DecodeUrlEncodedForm(query);

            if (!keysValues.ContainsKey("message") || string.IsNullOrEmpty(keysValues["message"])) {
                ReturnBadRequest(ctx, "Missing message.");
                return;
            }
            
            var cmd = new CommandModel(
                Frontend.FrontendManager,
                chat,
                (string)Frontend.UserConfig["Interface/Entry/CommandCharacter"],
                keysValues["message"]
            );

            // attempt to process locally
            if (!ProcessCommand(cmd)) {
                // send to server
                CommandManager?.Execute(cmd);
            }

            // redirect back
            Redirect(ctx, $"/{chatIndex.ToString(CultureInfo.InvariantCulture)}");
        }

        [Endpoint("/{chatIndex}/messages", Method = "GET")]
        public void MessagesFragment(HttpListenerContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            int chatCount;
            HttpChat httpChat;

            lock (CollectionLock) {
                chatCount = Chats.Count;
            }

            if (chatIndex < 0 || chatIndex >= chatCount) {
                ReturnNotFound(ctx);
                return;
            }

            lock (CollectionLock) {
                httpChat = ChatFrontends[Chats[chatIndex]];
            }

            ChatDrop chatDrop = new ChatDrop(httpChat);

            // mark messages of this chat as seen
            httpChat.UnseenMessages = 0;
            httpChat.UnseenHighlightMessages = 0;

            ReturnHtml(ctx, String.Concat(chatDrop.Messages));
        }

        [Endpoint("/{chatIndex}/messages.json", Method = "GET")]
        [Endpoint("/{chatIndex}/epoch/{epoch}/since/{firstMessageID}/messages.json", Method = "GET")]
        public void MessagesJsonSelection(HttpListenerContext ctx, int chatIndex, long epoch = -1, long firstMessageID = 0)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            int chatCount;
            HttpChat httpChat;

            lock (CollectionLock) {
                chatCount = Chats.Count;
            }

            if (chatIndex < 0 || chatIndex >= chatCount) {
                ReturnNotFound(ctx);
                return;
            }

            lock (CollectionLock) {
                httpChat = ChatFrontends[Chats[chatIndex]];
            }

            List<KeyValuePair<long, string>> htmlMessages;
            if (epoch > httpChat.MessagesEpoch)
            {
                // nothing new
                htmlMessages = new List<KeyValuePair<long, string>>();
            }
            else
            {
                htmlMessages = httpChat.GetHtmlMessages();

                if (epoch == httpChat.MessagesEpoch)
                {
                    // remove messages we already have
                    htmlMessages.RemoveAll(m => m.Key < firstMessageID);
                }
            }

            var ret = new JObject
            {
                {"epoch", new JValue(httpChat.MessagesEpoch)},
                {"nextID", new JValue(httpChat.NextMessageID)},
                {"messages", new JArray(
                    htmlMessages.Select(m => new JObject
                    {
                        {"id", new JValue(m.Key)},
                        {"body", new JValue(m.Value)}
                    })
                )}
            };

            // mark messages of this chat as seen
            httpChat.UnseenMessages = 0;
            httpChat.UnseenHighlightMessages = 0;

            ReturnJson(ctx, ret);
        }

        [Endpoint("/static/{fileName}", Method = "GET")]
        public void StaticFile(HttpListenerContext ctx, string fileName)
        {
            if (fileName.Contains('/') || fileName.Contains('\\')) {
                ReturnNotFound(ctx);
                return;
            }

            var target = Path.Combine("Static", fileName);
            if (!File.Exists(target)) {
                ReturnNotFound(ctx);
                return;
            }
            DateTime lastWritten = File.GetLastWriteTimeUtc(target);

            DateTime? ifModifiedSince = null;
            if (ctx.Request.Headers["If-Modified-Since"] != null) {
                DateTime parsed;
                if (DateTime.TryParseExact(ctx.Request.Headers["If-Modified-Since"],
                                           "r", CultureInfo.InvariantCulture,
                                           DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                           out parsed)) {
                    ifModifiedSince = parsed;
                }
            }

            if (ifModifiedSince.HasValue) {
                if (lastWritten <= ifModifiedSince.Value) {
                    // Not Modified
                    ctx.Response.StatusCode = 304;
                    ctx.Response.Close();
                    return;
                }
            }

            ctx.Response.Headers[HttpResponseHeader.LastModified] =
                lastWritten.ToString("r", CultureInfo.InvariantCulture);

            string mimeType;
            if (!ExtensionsToMimeTypes.TryGetValue(Path.GetExtension(fileName), out mimeType)) {
                mimeType = "application/octet-stream";
            }

            ctx.Response.ContentType = mimeType;
            using (var reading = new FileStream(target, FileMode.Open,
                                                FileAccess.Read, FileShare.Read)) {
                if (reading.CanSeek) {
                    ctx.Response.ContentLength64 = reading.Length;
                }
                reading.CopyTo(ctx.Response.OutputStream);
            }
            ctx.Response.Close();
        }

        [Endpoint("/login", Method = "GET")]
        public void LoginForm(HttpListenerContext ctx)
        {
            string result = Templates.LoginPage.Render();
            ReturnHtml(ctx, result);
        }

        [Endpoint("/login", Method = "POST")]
        public void ProcessLogin(HttpListenerContext ctx)
        {
            // read in the request
            string query = ReadRequestBody(ctx.Request.InputStream);
            
            // attempt login
            if (Authenticator.Login(ctx.Response, query)) {
                // redirect to landing page
                Redirect(ctx, "/");
            } else {
                // redirect to login form
                Redirect(ctx, "/login");
            }
        }

        [Endpoint("/logout", Method = "GET")]
        [Endpoint("/logout", Method = "POST")]
        public void Logout(HttpListenerContext ctx)
        {
            Authenticator.Logout();

            // redirect to the login form
            Redirect(ctx, "/login");
        }

        protected static void ReturnNotFound(HttpListenerContext ctx)
        {
            ReturnPlainText(ctx, "Not found.", 404);
        }

        protected static void ReturnBadRequest(HttpListenerContext ctx, string errorText = "Bad request.")
        {
            ReturnPlainText(ctx, errorText, 400);
        }

        protected static void ReturnPlainText(HttpListenerContext ctx, string message, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            CompressWriteCloseResponse(ctx, bytes);
        }

        protected static void ReturnHtml(HttpListenerContext ctx, string html, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            CompressWriteCloseResponse(ctx, bytes);
        }

        protected static void ReturnJson(HttpListenerContext ctx, JToken json, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json.ToString());

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.ContentType = "application/json";
            CompressWriteCloseResponse(ctx, bytes);
        }

        protected static void Redirect(HttpListenerContext ctx, string target, int code = 303)
        {
            Uri currentUrl = HttpUtil.GetHttpListenerRequestUri(ctx.Request);
            var targetUrl = new Uri(currentUrl, target);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.RedirectLocation = targetUrl.AbsoluteUri;
            ctx.Response.Close();
        }

        protected static string ReadRequestBody(Stream requestStream)
        {
            string query;
            using (var storage = new MemoryStream()) {
                requestStream.CopyTo(storage);
                query = Encoding.UTF8.GetString(storage.ToArray());
            }
            return query;
        }

        protected static void CompressWriteCloseResponse(HttpListenerContext ctx, byte[] body)
        {
            // compression?
            var acceptedEncodingsEnumerable = ctx.Request.Headers["Accept-Encoding"]
                ?.Split(',')
                .Select(e => e.Trim().Split(';').First());
            var acceptedEncodings = (acceptedEncodingsEnumerable != null)
                ? new HashSet<string>(acceptedEncodingsEnumerable)
                : new HashSet<string>();

            byte[] bytesToSend;
            if (acceptedEncodings.Contains("gzip")) {
                using (var ms = new MemoryStream()) {
                    using (var zipper = new GZipStream(ms, CompressionMode.Compress)) {
                        zipper.Write(body, 0, body.Length);
                    }
                    ctx.Response.Headers[HttpResponseHeader.ContentEncoding] = "gzip";
                    bytesToSend = ms.ToArray();
                }
            } else if (acceptedEncodings.Contains("deflate")) {
                using (var ms = new MemoryStream()) {
                    using (var deflater = new DeflateStream(ms, CompressionMode.Compress)) {
                        deflater.Write(body, 0, body.Length);
                    }
                    ctx.Response.Headers[HttpResponseHeader.ContentEncoding] = "deflate";
                    bytesToSend = ms.ToArray();
                }
            } else {
                ctx.Response.Headers[HttpResponseHeader.ContentEncoding] = "identity";
                bytesToSend = body;
            }

            ctx.Response.Close(bytesToSend, willBlock: false);
        }

        protected virtual bool AssertLoggedIn(HttpListenerContext ctx)
        {
            if (Authenticator.CheckAuthenticated(ctx)) {
                // OK
                return true;
            }

            // bad; redirect to login and suppress further processing
            Redirect(ctx, "/login");
            return false;
        }

        protected virtual bool ProcessCommand(CommandModel command)
        {
            if (command.IsCommand) {
                switch (command.Command) {
                    case "help":
                        ProcessCommandHelp(command);
                        return false;  // pass to server
                    case "close":
                        ProcessCommandClose(command);
                        return true;
                    case "reloadtemplates":
                        ProcessCommandReloadTemplates(command);
                        return true;
                }
            }

            return false;
        }

        protected virtual void ProcessCommandHelp(CommandModel command)
        {
            HttpChat httpChat;
            lock (CollectionLock) {
                httpChat = ChatFrontends[command.Chat];
            }
            
            var builder = new MessageBuilder();
            // TRANSLATOR: this line is used as a label / category for a
            // list of commands below
            builder.AppendHeader(_("Frontend Commands"));
            httpChat.AddMessage(builder.ToMessage());

            string[] help = {
                "close",
                "reloadtemplates",
            };

            foreach (string line in help) {
                builder = new MessageBuilder();
                builder.AppendEventPrefix();
                builder.AppendText(line);
                httpChat.AddMessage(builder.ToMessage());
            }
        }

        protected virtual void ProcessCommandClose(CommandModel command)
        {
            ChatModel chat = command.Chat;
            IProtocolManager protocolManager = chat.ProtocolManager;

            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    protocolManager.CloseChat(
                        Frontend.FrontendManager,
                        chat
                    );
                } catch (Exception ex) {
                    Logger.Fatal(ex);
                }
            });
        }

        protected virtual void ProcessCommandReloadTemplates(CommandModel command)
        {
            Templates.LoadTemplates();
        }

        protected HttpChat FrontendForChat(ChatModel chat)
        {
            HttpChat frontend;
            lock (CollectionLock) {
                frontend = ChatFrontends[chat];
            }
            return frontend;
        }

        static string _(string msg)
        {
            return Mono.Unix.Catalog.GetString(msg);
        }
    }
}
