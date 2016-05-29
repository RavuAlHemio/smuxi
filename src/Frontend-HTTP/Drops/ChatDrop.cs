using System.Collections.Generic;
using DotLiquid;

namespace Smuxi.Frontend.Http.Drops
{
    public class ChatDrop : Drop
    {
        protected HttpChat Chat { get; }

        public IEnumerable<string> Messages
        {
            get
            {
                List<string> messages = Chat.GetHtmlMessages();
                messages.Reverse();
                return messages;
            }
        }

        public string Name => Chat.Name;

        public string Topic => Chat.HtmlTopic;

        public IEnumerable<string> Users => Chat.GetHtmlUsers();

        public ChatDrop(HttpChat chat)
        {
            Chat = chat;
        }
    }
}
