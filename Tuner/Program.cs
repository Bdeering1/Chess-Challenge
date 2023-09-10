

namespace Tuner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("starting cutechess");
            CuteChess cutechess = new();
            Console.WriteLine(cutechess.Test("baseline_sept5", "stockfish", 250, 4));
        }
    }
}