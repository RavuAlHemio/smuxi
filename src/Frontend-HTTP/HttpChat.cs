﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Smuxi.Engine;

namespace Smuxi.Frontend.Http
{
    public class HttpChat
    {
        protected object CollectionsLock { get; }
        public string Name { get; set; }
        protected CircleBuffer<string> HtmlMessages { get; set; }
        public string HtmlTopic { get; protected set; }
        protected SortedDictionary<string, string> ParticipantNamesToHtmlNames { get; set; }
        public int UnseenMessages { get; set; }
        public int UnseenHighlightMessages { get; set; }
        public bool IsSystemChat { get; set; }

        public HttpChat()
        {
            CollectionsLock = new object();
            HtmlMessages = new CircleBuffer<string>(512);
            HtmlTopic = null;
            ParticipantNamesToHtmlNames = null;
            UnseenMessages = 0;
            UnseenHighlightMessages = 0;
            IsSystemChat = false;
        }

        public void AddMessage(MessageModel message)
        {
            lock (CollectionsLock) {
                HtmlMessages.Add(TransformMessage(message));
            }

            if (message.MessageType == MessageType.Normal) {
                if (ParticipantNamesToHtmlNames == null && !IsSystemChat) {
                    // private chat; count this as a highlight message
                    ++UnseenHighlightMessages;
                } else if (message.MessageParts.Any(p => p.IsHighlight)) {
                    // highlighted in this message
                    ++UnseenHighlightMessages;
                } else {
                    // regular message
                    ++UnseenMessages;
                }
            }
        }

        public void AddParticipant(PersonModel person)
        {
            lock (CollectionsLock) {
                if (ParticipantNamesToHtmlNames == null) {
                    ParticipantNamesToHtmlNames = new SortedDictionary<string, string>();
                }

                ParticipantNamesToHtmlNames[person.IdentityName] = TransformNickname(person);
            }
        }

        public void ReplaceAllMessages(IEnumerable<MessageModel> messages)
        {
            lock (CollectionsLock) {
                HtmlMessages.Clear();
                HtmlMessages.AddRange(messages.Select(m => TransformMessage(m)));
            }
        }

        public void ReplaceAllParticipants(IEnumerable<PersonModel> persons)
        {
            lock (CollectionsLock) {
                if (ParticipantNamesToHtmlNames == null) {
                    ParticipantNamesToHtmlNames = new SortedDictionary<string, string>();
                } else {
                    ParticipantNamesToHtmlNames.Clear();
                }

                foreach (PersonModel person in persons) {
                    ParticipantNamesToHtmlNames[person.IdentityName] = TransformNickname(person);
                }
            }
        }

        public void RemoveParticipant(PersonModel person)
        {
            lock (CollectionsLock) {
                if (ParticipantNamesToHtmlNames == null) {
                    ParticipantNamesToHtmlNames = new SortedDictionary<string, string>();
                }

                ParticipantNamesToHtmlNames.Remove(person.IdentityName);
            }
        }

        public void UpdateTopic(MessageModel topic)
        {
            HtmlTopic = TransformMessage(topic, includeTimestamp: false, divWrap: false);
        }

        public List<string> GetHtmlMessages()
        {
            lock (CollectionsLock) {
                return new List<string>(HtmlMessages);
            }
        }

        public List<string> GetHtmlUsers()
        {
            lock (CollectionsLock) {
                if (ParticipantNamesToHtmlNames == null) {
                    return null;
                }
                return new List<string>(ParticipantNamesToHtmlNames.Values);
            }
        }

        public static string TransformMessage(MessageModel message, bool includeTimestamp = true, bool divWrap = true)
        {
            if (message == null) {
                return "";
            }

            var messageHtml = new StringBuilder();
            if (divWrap) {
                string messageClass;
                switch (message.MessageType) {
                    case MessageType.Normal:
                        messageClass = "normal-message";
                        break;
                    case MessageType.Event:
                        messageClass = "event-message";
                        break;
                    default:
                        messageClass = "other-message";
                        break;
                }
                messageHtml.AppendFormat("<div class=\"message {0}\">", messageClass);
            }

            if (includeTimestamp) {
                string customTimestampFormat =
                    Frontend.FrontendConfig[Frontend.UIName + "/CustomTimestampFormat"] as string;
                DateTime localTimestamp = message.TimeStamp.ToLocalTime();
                string localTimestampString = (customTimestampFormat == null)
                    ? localTimestamp.ToLongTimeString()
                    : localTimestamp.ToString(customTimestampFormat, CultureInfo.InvariantCulture);
                messageHtml.AppendFormat("<span class=\"timestamp\">{0}</span> ",
                    WebUtility.HtmlEncode(localTimestampString));
            }

            foreach (MessagePartModel part in message.MessageParts) {
                messageHtml.Append(TransformMessagePart(part));
            }

            if (divWrap) {
                messageHtml.Append("</div>\r\n");
            }

            return messageHtml.ToString();
        }

        public static string TransformMessagePart(MessagePartModel part)
        {
            var urlPart = part as UrlMessagePartModel;
            if (urlPart?.Url != null) {
                var style = TextMessagePartStyle(urlPart);
                string textOrUrl = string.IsNullOrEmpty(urlPart.Text)
                    ? urlPart.Url
                    : urlPart.Text;
                return String.Format("<a href=\"{0}\" style=\"{1}\">{2}</a>",
                    WebUtility.HtmlEncode(urlPart.Url),
                    style,
                    WebUtility.HtmlEncode(textOrUrl));
            }

            var textPart = part as TextMessagePartModel;
            if (textPart != null) {
                var style = TextMessagePartStyle(textPart);
                if (style.Length > 0)
                {
                    return $"<span style=\"{style}\">{WebUtility.HtmlEncode(textPart.Text)}</span>";
                }
                return WebUtility.HtmlEncode(textPart.Text);
            }

            var imagePart = part as ImageMessagePartModel;
            if (imagePart != null) {
                // FIXME: alt text only
                return $"<span class=\"image-substitute\">{WebUtility.HtmlEncode(imagePart.AlternativeText)}</span>";
            }

            return "";
        }

        public static string TransformNickname(PersonModel person)
        {
            string sigil = "";

            var ircGroupPerson = person as IrcGroupPersonModel;
            if (ircGroupPerson != null) {
                if (ircGroupPerson.IsOwner) {
                    sigil = "<span class=\"sigil owner\">~</span>";
                } else if (ircGroupPerson.IsChannelAdmin) {
                    sigil = "<span class=\"sigil channel-admin\">&amp;</span>";
                } else if (ircGroupPerson.IsOp) {
                    sigil = "<span class=\"sigil op\">@</span>";
                } else if (ircGroupPerson.IsHalfop) {
                    sigil = "<span class=\"sigil halfop\">%</span>";
                } else if (ircGroupPerson.IsVoice) {
                    sigil = "<span class=\"sigil voice\">+</span>";
                } else {
                    sigil = "<span class=\"sigil none\"></span>";
                }
            }

            TextMessagePartModel nick = person.IdentityNameColored;
            string bracketColor = nick.BackgroundColor.HexCode;
            string nickColor = nick.ForegroundColor.HexCode;
            return $"{sigil}<span class=\"nickname-bracket\" style=\"color:#{bracketColor}\">&lt;</span><span class=\"nickname\" style=\"color:#{nickColor}\">{WebUtility.HtmlEncode(nick.Text)}</span><span class=\"nickname-bracket\" style=\"color:#{bracketColor}\">&gt;</span>";
        }

        public static string TextMessagePartStyle(TextMessagePartModel text)
        {
            var stylings = new Dictionary<string, string>();
            if (text.Bold) {
                stylings["font-weight"] = "bold";
            }
            if (text.Italic) {
                stylings["font-style"] = "italic";
            }
            if (text.Underline) {
                stylings["text-decoration"] = "underline";
            }
            if (text.BackgroundColor != TextColor.None) {
                stylings["background-color"] = "#" + text.BackgroundColor.HexCode;
            }
            if (text.ForegroundColor != TextColor.None) {
                stylings["color"] = "#" + text.ForegroundColor.HexCode;
            }
            return String.Join(";", stylings.Select(
                s => $"{s.Key}:{s.Value}"));
        }
    }
}
