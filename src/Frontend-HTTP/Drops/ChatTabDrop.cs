using DotLiquid;

namespace Smuxi.Frontend.Http.Drops
{
    public class ChatTabDrop : Drop
    {
        protected HttpChat Chat { get; }

        public int Index { get; }
        public string Name => Chat.Name;
        public int UnseenMsgs => Chat.UnseenMessages;
        public int UnseenHighlightMsgs => Chat.UnseenHighlightMessages;

        public ChatTabDrop(int index, HttpChat chat)
        {
            Index = index;
            Chat = chat;
        }
    }
}
