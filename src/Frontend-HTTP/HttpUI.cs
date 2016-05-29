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

        protected HttpAuthenticator Authenticator { get; }
        public Dictionary<ChatModel, HttpChat> ChatFrontends { get; }
        public List<ChatModel> Chats { get; }
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
            ExtensionsToMimeTypes = new Dictionary<string, string>
            {
                [".css"] = "text/css",
                [".js"] = "text/javascript"
            };
            Listener = new DistributingHttpListener(uriPrefix);
            Listener.AddResponder(this);
            Templates = new Templates();
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
            if (!AssertLoggedIn(ctx)) {
                return;
            }

            List<string> chatNames = Chats.Select(c => c.Name).ToList();
            string result = Templates.LandingPage.Render(Hash.FromAnonymousObject(new
            {
                chat_names = chatNames
            }));

            ReturnHtml(ctx, result);
        }

        [Endpoint("/{chatIndex}", Method = "GET")]
        public void ChatPage(HttpListenerContext ctx, int chatIndex)
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

            List<string> chatNames = Chats.Select(c => c.Name).ToList();
            string result = Templates.ChatPage.Render(Hash.FromAnonymousObject(new
            {
                chat = chatDrop,
                chat_names = chatNames,
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

            if (chatIndex < 0 || chatIndex >= Chats.Count) {
                ReturnNotFound(ctx);
                return;
            }

            ChatModel chat = Chats[chatIndex];

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
            CommandManager?.Execute(cmd);

            // redirect back
            Redirect(ctx, $"/{chatIndex.ToString(CultureInfo.InvariantCulture)}");
        }

        [Endpoint("/{chatIndex}/messages", Method = "GET")]
        public void MessagesFragment(HttpListenerContext ctx, int chatIndex)
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

            var messages = new StringBuilder();
            foreach (string message in chatDrop.Messages) {
                messages.Append("<div class=\"message\">");
                messages.Append(message);
                messages.Append("</div>\r\n");
            }
            
            ReturnHtml(ctx, messages.ToString());
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

        protected void ReturnHtml(HttpListenerContext ctx, string html, int code = 200)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Close(bytes, willBlock: true);
        }

        protected void Redirect(HttpListenerContext ctx, string target, int code = 303)
        {
            Uri currentUrl = HttpUtil.GetHttpListenerRequestUri(ctx.Request);
            var targetUrl = new Uri(currentUrl, target);

            ctx.Response.StatusCode = code;
            ctx.Response.ContentLength64 = 0;
            ctx.Response.RedirectLocation = targetUrl.AbsoluteUri;
            ctx.Response.Close();
        }

        protected string ReadRequestBody(Stream requestStream)
        {
            string query;
            using (var storage = new MemoryStream()) {
                requestStream.CopyTo(storage);
                query = Encoding.UTF8.GetString(storage.ToArray());
            }
            return query;
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
    }
}
