using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smuxi.Frontend.Http
{
    public class MainClass
    {
        public static void Main()
        {
            // FIXME
            const string uriPrefix = "http://+:8080/";
            const string engine = "dbowncloud";

            Frontend.Init(engine, uriPrefix);

            Console.WriteLine("Press Enter or Escape to exit.");
            for (;;) {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter) {
                    break;
                }
            }

            Frontend.Quit();
        }
    }
}
