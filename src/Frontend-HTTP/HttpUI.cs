using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using DotLiquid;
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

        public Dictionary<ChatModel, HttpChat> ChatFrontends { get; }
        public List<ChatModel> Chats { get; }
        public Template ChatPageTemplate { get; }
        public CommandManager CommandManager { get; set; }
        public Dictionary<string, string> ExtensionsToMimeTypes { get; private set; }
        public DistributingHttpListener Listener { get; set; }
        public int Version => 0;

        public HttpUI(string uriPrefix)
        {
            ChatFrontends = new Dictionary<ChatModel, HttpChat>();
            Chats = new List<ChatModel>();
            using (var reader = new StreamReader(
                    Path.Combine("Templates", "chat.html.liquid"), Encoding.UTF8)) {
                ChatPageTemplate = Template.Parse(reader.ReadToEnd());
            }
            ExtensionsToMimeTypes = new Dictionary<string, string>
            {
                [".css"] = "text/css",
                [".js"] = "text/javascript"
            };
            Listener = new DistributingHttpListener(uriPrefix);
            Listener.AddResponder(this);
        }

        public void Start()
        {
            Listener.Start();
        }

        public void AddChat(ChatModel chat)
        {
            Trace.Call(chat);
            Chats.Add(chat);
            ChatFrontends[chat] = new HttpChat
            {
                Name = chat.Name
            };

            var groupModel = chat as GroupChatModel;
            if (groupModel != null) {
                ChatFrontends[chat].ReplaceAllParticipants(groupModel.Persons.Values);
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
        public void LandingPage(HttpListenerContext ctx)
        {
            // TODO
        }

        [Endpoint("/{chatIndex}", Method = "GET")]
        public void ChatPage(HttpListenerContext ctx, int chatIndex)
        {
            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            HttpChat chat = ChatFrontends[Chats[chatIndex]];
            ChatDrop chatDrop = new ChatDrop(chat);

            List<string> chatNames = Chats.Select(c => c.Name).ToList();
            string result = ChatPageTemplate.Render(Hash.FromAnonymousObject(new
            {
                chat = chatDrop,
                chats = chatNames,
                chat_index = chatIndex.ToString(CultureInfo.InvariantCulture)
            }));

            byte[] resultBytes = Encoding.UTF8.GetBytes(result);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = resultBytes.Length;
            ctx.Response.Close(resultBytes, willBlock: true);
        }

        [Endpoint("/{chatIndex}/message", Method = "POST")]
        public void PostMessage(HttpListenerContext ctx, int chatIndex)
        {
            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            ChatModel chat = Chats[chatIndex];

            string query;
            using (var storage = new MemoryStream()) {
                ctx.Request.InputStream.CopyTo(storage);
                query = Encoding.UTF8.GetString(storage.ToArray());
            }

            string[] keyValueStrings = query.Split('&');
            var keysValues = new Dictionary<string, string>();
            foreach (string keyValue in keyValueStrings) {
                string[] pieces = keyValue.Split(new[] {'='}, 2);
                string key = WebUtility.UrlDecode(pieces[0]);
                string value = pieces.Length > 1 ? WebUtility.UrlDecode(pieces[1]) : null;
                keysValues[key] = value;
            }

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
            CommandManager?.Execute(cmd);

            // redirect back
            Redirect(ctx, $"/{chatIndex.ToString(CultureInfo.InvariantCulture)}");
        }

        [Endpoint("/{chatIndex}/messages", Method = "GET")]
        public void MessagesFragment(HttpListenerContext ctx, int chatIndex)
        {
            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            HttpChat chat = ChatFrontends[Chats[chatIndex]];
            ChatDrop chatDrop = new ChatDrop(chat);

            var messages = new StringBuilder();
            foreach (string message in chatDrop.Messages) {
                messages.Append("<div class=\"message\">");
                messages.Append(message);
                messages.Append("</div>\r\n");
            }
            
            byte[] messagesBytes = Encoding.UTF8.GetBytes(messages.ToString());
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = messagesBytes.Length;
            ctx.Response.Close(messagesBytes, willBlock: true);
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

        protected void ReturnNotFound(HttpListenerContext ctx)
        {
            ReturnPlainText(ctx, "Not found.", 404);
        }

        protected void ReturnBadRequest(HttpListenerContext ctx, string errorText = "Bad request.")
        {
            ReturnPlainText(ctx, errorText, 400);
        }

        protected void ReturnPlainText(HttpListenerContext ctx, string message, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.Close(bytes, willBlock: true);
        }

        protected void Redirect(HttpListenerContext ctx, string target, int code = 303)
        {
            // get current URL
            var currentUrlString = new StringBuilder();
            currentUrlString.Append(ctx.Request.IsSecureConnection ? "https" : "http");
            currentUrlString.Append("://");
            currentUrlString.Append(ctx.Request.UserHostName ?? ctx.Request.UserHostAddress);
            currentUrlString.Append(ctx.Request.RawUrl);

            var currentUrl = new Uri(currentUrlString.ToString());
            var targetUrl = new Uri(currentUrl, target);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.RedirectLocation = targetUrl.AbsoluteUri;
            ctx.Response.Close();
        }
    }
}
