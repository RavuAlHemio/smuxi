using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DotLiquid;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using RavuAlHemio.HttpDispatcher;
using RavuAlHemio.HttpDispatcher.Kestrel;
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
        public Dictionary<ChatModel, HttpChat> ChatFrontends { get; }
        public List<ChatModel> Chats { get; }
        public CommandManager CommandManager { get; set; }
        public Dictionary<string, string> ExtensionsToMimeTypes { get; }
        public DistributingKestrelServer Kestrel { get; set; }
        public Templates Templates { get; set; }
        public int Version => 0;

        public HttpUI(string uriPrefix)
        {
            Authenticator = new HttpAuthenticator();
            ChatFrontends = new Dictionary<ChatModel, HttpChat>();
            Chats = new List<ChatModel>();
            ExtensionsToMimeTypes = new Dictionary<string, string>
            {
                [".css"] = "text/css",
                [".js"] = "text/javascript"
            };
            Kestrel = new DistributingKestrelServer(uriPrefix);
            Kestrel.AddResponder(this);
            Templates = new Templates();
        }

        public void Start()
        {
            Kestrel.Start();
        }

        public void AddChat(ChatModel chat)
        {
            Trace.Call(chat);
            Chats.Add(chat);
            ChatFrontends[chat] = new HttpChat
            {
                Name = chat.Name,
                IsSystemChat = (chat.ChatType == ChatType.Session
                                || chat.ChatType == ChatType.Protocol)
            };

            var groupChat = chat as GroupChatModel;
            if (groupChat != null) {
                ChatFrontends[chat].ReplaceAllParticipants(groupChat.Persons.Values);
                ChatFrontends[chat].UpdateTopic(groupChat.Topic);
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
            ChatFrontends[chat].AddMessage(msg);
        }

        public void RemoveChat(ChatModel chat)
        {
            Trace.Call(chat);
            Chats.Remove(chat);
            ChatFrontends.Remove(chat);
        }

        public void SyncChat(ChatModel chat)
        {
            Trace.Call(chat);

            ChatFrontends[chat].ReplaceAllMessages(chat.Messages);

            var groupChat = chat as GroupChatModel;
            if (groupChat != null) {
                ChatFrontends[chat].ReplaceAllParticipants(groupChat.Persons.Values);
            }
            Frontend.FrontendManager.AddSyncedChat(chat);
        }

        public void AddPersonToGroupChat(GroupChatModel groupChat, PersonModel person)
        {
            Trace.Call(groupChat, person);

            ChatFrontends[groupChat].AddParticipant(person);
        }

        public void UpdatePersonInGroupChat(GroupChatModel groupChat, PersonModel oldPerson,
            PersonModel newPerson)
        {
            Trace.Call(groupChat, oldPerson, newPerson);

            HttpChat frontend = ChatFrontends[groupChat];
            frontend.RemoveParticipant(oldPerson);
            frontend.AddParticipant(newPerson);
        }

        public void UpdateTopicInGroupChat(GroupChatModel groupChat, MessageModel topic)
        {
            Trace.Call(groupChat, topic);

            ChatFrontends[groupChat].UpdateTopic(topic);
        }

        public void RemovePersonFromGroupChat(GroupChatModel groupChat, PersonModel person)
        {
            Trace.Call(groupChat, person);

            ChatFrontends[groupChat].RemoveParticipant(person);
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
        public void LandingPage(HttpContext ctx)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            List<ChatTabDrop> chats = Chats
                .Select((c, i) => new ChatTabDrop(i, ChatFrontends[c]))
                .ToList();
            string result = Templates.LandingPage.Render(Hash.FromAnonymousObject(new
            {
                chat_tabs = chats
            }));

            ReturnHtml(ctx, result);
        }

        [Endpoint("/{chatIndex}", Method = "GET")]
        public void ChatPage(HttpContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            HttpChat chat = ChatFrontends[Chats[chatIndex]];
            ChatDrop chatDrop = new ChatDrop(chat);
            List<ChatTabDrop> chats = Chats
                .Select((c, i) => new ChatTabDrop(i, ChatFrontends[c]))
                .ToList();
            List<ChatTabDrop> highlightedChats = Chats
                .Select((c, i) => Tuple.Create(ChatFrontends[c], i))
                .Where(t => t.Item1.UnseenHighlightMessages > 0)
                .Select(t => new ChatTabDrop(t.Item2, t.Item1))
                .ToList();

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
        public void PostMessage(HttpContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            ChatModel chat = Chats[chatIndex];

            string query = ReadRequestBody(ctx.Request.Body);
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
        public void MessagesFragment(HttpContext ctx, int chatIndex)
        {
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            HttpChat chat = ChatFrontends[Chats[chatIndex]];
            ChatDrop chatDrop = new ChatDrop(chat);

            // mark messages of this chat as seen
            chat.UnseenMessages = 0;
            chat.UnseenHighlightMessages = 0;

            ReturnHtml(ctx, String.Concat(chatDrop.Messages));
        }

        [Endpoint("/static/{fileName}", Method = "GET")]
        public void StaticFile(HttpContext ctx, string fileName)
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
            if (ctx.Request.Headers.ContainsKey("If-Modified-Since")) {
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
                    return;
                }
            }

            ctx.Response.Headers["Last-Modified"] =
                lastWritten.ToString("r", CultureInfo.InvariantCulture);

            string mimeType;
            if (!ExtensionsToMimeTypes.TryGetValue(Path.GetExtension(fileName), out mimeType)) {
                mimeType = "application/octet-stream";
            }

            ctx.Response.ContentType = mimeType;
            using (var reading = new FileStream(target, FileMode.Open,
                                                FileAccess.Read, FileShare.Read)) {
                if (reading.CanSeek) {
                    ctx.Response.ContentLength = reading.Length;
                }
                reading.CopyTo(ctx.Response.Body);
            }
        }

        [Endpoint("/login", Method = "GET")]
        public void LoginForm(HttpContext ctx)
        {
            string result = Templates.LoginPage.Render();
            ReturnHtml(ctx, result);
        }

        [Endpoint("/login", Method = "POST")]
        public void ProcessLogin(HttpContext ctx)
        {
            // read in the request
            string query = ReadRequestBody(ctx.Request.Body);
            
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
        public void Logout(HttpContext ctx)
        {
            Authenticator.Logout();

            // redirect to the login form
            Redirect(ctx, "/login");
        }

        protected static void ReturnNotFound(HttpContext ctx)
        {
            ReturnPlainText(ctx, "Not found.", 404);
        }

        protected static void ReturnBadRequest(HttpContext ctx, string errorText = "Bad request.")
        {
            ReturnPlainText(ctx, errorText, 400);
        }

        protected static void ReturnPlainText(HttpContext ctx, string message, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength = bytes.Length;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            CompressWriteCloseResponse(ctx, bytes);
        }

        protected static void ReturnHtml(HttpContext ctx, string html, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength = bytes.Length;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            CompressWriteCloseResponse(ctx, bytes);
        }

        protected static void Redirect(HttpContext ctx, string target, int code = 303)
        {
            var currentUrl = new Uri(ctx.Request.GetEncodedUrl());
            var targetUrl = new Uri(currentUrl, target);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength = 0;
            ctx.Response.Headers["Location"] = targetUrl.AbsoluteUri;
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

        protected static void CompressWriteCloseResponse(HttpContext ctx, byte[] body)
        {
            // compression?
            var acceptedEncodingsEnumerable = ctx.Request.Headers["Accept-Encoding"].SingleOrDefault()
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
                    ctx.Response.Headers["Content-Encoding"] = "gzip";
                    bytesToSend = ms.ToArray();
                }
            } else if (acceptedEncodings.Contains("deflate")) {
                using (var ms = new MemoryStream()) {
                    using (var deflater = new DeflateStream(ms, CompressionMode.Compress)) {
                        deflater.Write(body, 0, body.Length);
                    }
                    ctx.Response.Headers["Content-Encoding"] = "deflate";
                    bytesToSend = ms.ToArray();
                }
            } else {
                ctx.Response.Headers["Content-Encoding"] = "identity";
                bytesToSend = body;
            }

            ctx.Response.Body.Write(bytesToSend, 0, bytesToSend.Length);
        }

        protected virtual bool AssertLoggedIn(HttpContext ctx)
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
            HttpChat httpChat = ChatFrontends[command.Chat];
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

        static string _(string msg)
        {
            return Mono.Unix.Catalog.GetString(msg);
        }
    }
}
