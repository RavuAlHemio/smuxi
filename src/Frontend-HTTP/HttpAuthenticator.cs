using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Smuxi.Frontend.Http
{
    public class HttpAuthenticator
    {
        protected string CookieName =>
            (Frontend.FrontendConfig["Frontend/" + Frontend.UIName + "/CookieName"] as string)
            ?? "SmuxiHttpSession";
        protected string CurrentSessionToken { get; set; }

        public HttpAuthenticator()
        {
            CurrentSessionToken = null;
        }

        public bool CheckAuthenticated(HttpListenerContext ctx)
        {
            // verify session cookie
            Cookie authCookie = ctx.Request.Cookies[CookieName];
            if (authCookie != null) {
                if (authCookie.Value == CurrentSessionToken) {
                    return true;
                }
            }

            // check for token; this makes quick "login by navigating to a bookmark" tricks possible
            // and allowing users to revoke tokens for stolen devices etc.
            Uri url = HttpUtil.GetHttpListenerRequestUri(ctx.Request);
            string queryString = url.Query;
            if (queryString.StartsWith("?")) {
                Dictionary<string, string> query = HttpUtil.DecodeUrlEncodedForm(queryString.Substring(1));
                if (query.ContainsKey("token")) {
                    var tokensString = Frontend.FrontendConfig["Frontend/" + Frontend.UIName + "/Tokens"] as string;
                    if (tokensString != null) {
                        IEnumerable<string> tokensEnumerable = tokensString
                            .Split(',')
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0);
                        var tokens = new HashSet<string>(tokensEnumerable);
                        if (tokens.Contains(query["token"])) {
                            // set the session cookie in response
                            SetSessionCookie(ctx.Response);

                            // authenticated
                            return true;
                        }
                    }
                }
            }

            // assume authentication failed
            return false;
        }

        public bool Login(HttpListenerResponse response, string username, string password)
        {
            var expectedUsername = Frontend.FrontendConfig["Frontend/" + Frontend.UIName + "/Username"] as string;
            var expectedPassword = Frontend.FrontendConfig["Frontend/" + Frontend.UIName + "/Password"] as string;

            if (expectedUsername == null || expectedPassword == null) {
                // no username/password authentication available
                return false;
            }

            if (username != expectedUsername || password != expectedPassword) {
                // wrong credentials
                return false;
            }

            // success!
            // issue a cookie
            SetSessionCookie(response);
            
            return true;
        }

        /// <remarks>
        /// <paramref name="queryString"/> must be supplied without the
        /// leading question mark, if any.
        /// </remarks>
        public bool Login(HttpListenerResponse response, string queryString)
        {
            Dictionary<string, string> query = HttpUtil.DecodeUrlEncodedForm(queryString);
            if (!query.ContainsKey("username") || !query.ContainsKey("password")) {
                return false;
            }

            return Login(response, query["username"], query["password"]);
        }

        public void Logout()
        {
            // invalidate the session token
            CurrentSessionToken = null;
        }

        protected void SetSessionCookie(HttpListenerResponse response)
        {
            string sessionToken = ObtainSessionToken();

            var cookie = new Cookie(CookieName, sessionToken);
            response.SetCookie(cookie);
        }

        protected string ObtainSessionToken()
        {
            // re-use existing session token if one exists (multiple logins)
            if (CurrentSessionToken != null) {
                return CurrentSessionToken;
            }

            const string tokenAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            const int tokenLength = 16;
            var tokenBuilder = new StringBuilder(tokenLength);

            using (var rng = RandomNumberGenerator.Create()) {
                // obtain 32 bits of information at a time
                var buf = new byte[4];
                for (int i = 0; i < tokenLength; i++) {
                    // fill the array
                    rng.GetBytes(buf);

                    // interpret this as an unsigned 32-bit integer
                    uint numerator =
                        ((uint) buf[0] << 24) |
                        ((uint) buf[1] << 16) |
                        ((uint) buf[2] <<  8) |
                        ((uint) buf[3] <<  0);

                    // divide this by the maximum value of an unsigned 32-bit integer
                    // this gives us an equally distributed value in [0.0; 1.0)
                    uint denominator = UInt32.MaxValue;
                    double fraction = numerator/(double) denominator;

                    // use this value to get an index into the array
                    int index = (int) (fraction*tokenAlphabet.Length);

                    // append it to our token
                    tokenBuilder.Append(tokenAlphabet[index]);
                }
            }

            // ... and we have a token!
            CurrentSessionToken = tokenBuilder.ToString();

            return CurrentSessionToken;
        }
    }
}
