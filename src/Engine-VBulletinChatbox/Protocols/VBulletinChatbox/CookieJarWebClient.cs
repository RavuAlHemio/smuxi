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
using System.Net;

namespace Smuxi.Engine.VBulletinChatbox
{
    public class CookieJarWebClient : WebClient
    {
        public CookieContainer CookieJar = new CookieContainer();
        public int Timeout = 5000;

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = Timeout;
            var httpRequest = request as HttpWebRequest;
            if (httpRequest != null) {
                httpRequest.CookieContainer = CookieJar;
            }
            return request;
        }
    }
}

