using System.Collections.Generic;
using System.Linq;
using DotLiquid;

namespace Smuxi.Frontend.Http.Drops
{
    public class ChatDrop : Drop
    {
        protected HttpChat Chat { get; }

        public string Name => Chat.Name;

        public ChatDrop(HttpChat chat)
        {
            Chat = chat;
        }

        public IEnumerable<string> Messages
        {
            get
            {
                List<string> messages = Chat.GetHtmlMessages();
                messages.Reverse();
                return messages;
            }
        }

        public IEnumerable<string> Users => Chat.GetHtmlUsers();
    }
}
