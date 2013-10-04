// Smuxi - Smart MUltipleXed Irc
//
// Copyright (c) 2013 Ondřej Hošek <ondra.hosek@gmail.com>
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
using System.Runtime.Serialization;
using Smuxi.Common;
using Smuxi.Engine;

namespace Smuxi.Engine.VBulletinChatbox
{
    [Serializable]
    internal class ChatboxPersonModel : PersonModel
	{
		public string ForumUri { get; internal set; }
		public ulong Uid { get; internal set; }
		public string Nickname { get; internal set; }

		internal protected ChatboxPersonModel(string forumUri, ulong uid, string nickname, IProtocolManager pm)
			: base(uid + "@" + forumUri, nickname, forumUri, "VBulletinChatbox", pm)
		{
			ForumUri = forumUri;
			Uid = uid;
			Nickname = nickname;
		}

        internal protected ChatboxPersonModel(SerializationInfo info,
                                              StreamingContext ctx)
            : base(info, ctx)
        {
        }
	}
}