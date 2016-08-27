using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Smuxi.Frontend.Http
{
    internal static class HttpUtil
    {
        public static Dictionary<string, string> DecodeUrlEncodedForm(string formData)
        {
            // "one=a&two=b&three=c=d&four" => ["one=a", "two=b", "three=c=d", "four"]
            string[] keyValueStrings = formData.Split('&');
            var keysValues = new Dictionary<string, string>();

            foreach (string keyValue in keyValueStrings) {
                // "one=a" => ["one", "a"]; "three=c=d" => ["three", "c=d"]; "four" => ["four"]
                string[] pieces = keyValue.Split(new[] { '=' }, 2);

                // ["one", "a"] => {"one": "a"}; ["four"] => {"four": null}
                string key = WebUtility.UrlDecode(pieces[0]);
                string value = pieces.Length > 1 ? WebUtility.UrlDecode(pieces[1]) : null;

                keysValues[key] = value;
            }

            // {"one": "a", "two": "b", "three": "c=d", "four": null}
            return keysValues;
        }
    }
}
