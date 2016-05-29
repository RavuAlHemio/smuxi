using System;

namespace Smuxi.Frontend.Http
{
    public class MainClass
    {
        public static void Main()
        {
            Frontend.Init();

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
