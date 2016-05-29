using System;
using System.IO;
using System.Text;
using DotLiquid;
using DotLiquid.FileSystems;

namespace Smuxi.Frontend.Http
{
    public class Templates
    {
        public Template ChatPage { get; protected set; }
        public Template LandingPage { get; protected set; }
        public Template LoginPage { get; protected set; }

        public Templates()
        {
            LoadTemplates();
        }

        public virtual void LoadTemplates()
        {
            Template.FileSystem = new LocalFileSystem(Path.Combine(Environment.CurrentDirectory, "Templates"));

            ChatPage = LoadTemplate("chat.html.liquid");
            LandingPage = LoadTemplate("landing.html.liquid");
            LoginPage = LoadTemplate("login.html.liquid");
        }

        protected virtual Template LoadTemplate(string filename)
        {
            using (var reader = new StreamReader(
                    Path.Combine("Templates", filename), Encoding.UTF8))
            {
                return Template.Parse(reader.ReadToEnd());
            }
        }
    }
}
