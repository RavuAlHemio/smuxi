using System.Collections.Generic;
using System.Linq;
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
                List<KeyValuePair<long, string>> messages = Chat.GetHtmlMessages();
                messages.Reverse();
                return messages.Select(m => m.Value);
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
