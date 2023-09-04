

namespace Tuner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("starting cutechess");
            CuteChess cutechess = new();
            Console.WriteLine(cutechess.Test("engine1", "stockfish", 4));
        }
    }
}