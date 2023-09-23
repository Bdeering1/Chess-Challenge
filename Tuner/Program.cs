

namespace Tuner
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var finished = PgnParser.Parse("../../../../pgns/bigtest.pgn", "../../../../pgns/bigtestfens.txt");
            //Console.WriteLine("starting cutechess");
            //CuteChess cutechess = new();
            //Console.WriteLine(cutechess.Test("engine1", "stockfish", 500, 4));
        }
    }
}