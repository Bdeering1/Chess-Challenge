using ChessChallenge.API;
using System;
using System.Diagnostics;
using System.IO;

public class Stockfish : IChessBot
{
    const double FISH_RATIO = 1;
    const int MS_PER_MOVE = 500;

    const string STOCKFISH_BINARY = "stockfish";
    const string MAX_DEPTH = "10";
    const string SKILL_LEVEL = "5";
    const string THREADS = "6";

    public Move Think(Board board, Timer timer)
    {
        Move bestMove = Move.NullMove;

        var rand = new Random();
        if (rand.NextDouble() > FISH_RATIO)
        {
            Move[] all_moves = board.GetLegalMoves();
            return all_moves[rand.Next(0, all_moves.Length)];
        }

        Process stockfish = new Process();
        stockfish.StartInfo.UseShellExecute = false;
        stockfish.StartInfo.RedirectStandardOutput = true;
        stockfish.StartInfo.RedirectStandardInput = true;
        stockfish.StartInfo.FileName = STOCKFISH_BINARY;
        stockfish.OutputDataReceived += (sender, args) =>
        {
            /* Example output:
                Stockfish 16 by the Stockfish developers (see AUTHORS file)
                info string NNUE evaluation using nn-5af11540bbfe.nnue enabled
                info depth 1 seldepth 1 multipv 1 score cp 2 nodes 20 nps 20000 hashfull 0 tbhits 0 time 1 pv g1f3
                info depth 2 seldepth 2 multipv 1 score cp 2 nodes 40 nps 40000 hashfull 0 tbhits 0 time 1 pv g1f3
                info depth 3 seldepth 2 multipv 1 score cp 16 nodes 70 nps 70000 hashfull 0 tbhits 0 time 1 pv c2c3
                info depth 4 seldepth 2 multipv 1 score cp 29 nodes 101 nps 101000 hashfull 0 tbhits 0 time 1 pv e2e4
                info depth 5 seldepth 3 multipv 1 score cp 42 nodes 131 nps 131000 hashfull 0 tbhits 0 time 1 pv e2e4 g8f6
                info depth 6 seldepth 4 multipv 1 score cp 59 nodes 489 nps 244500 hashfull 0 tbhits 0 time 2 pv g1f3 d7d5 d2d4
                info depth 7 seldepth 6 multipv 1 score cp 31 nodes 1560 nps 520000 hashfull 1 tbhits 0 time 3 pv e2e4 d7d5 e4d5 d8d5 g1f3
                info depth 8 seldepth 6 multipv 1 score cp 40 nodes 2105 nps 701666 hashfull 1 tbhits 0 time 3 pv e2e4 d7d5 e4d5 d8d5
                info depth 9 seldepth 8 multipv 1 score cp 48 nodes 4500 nps 900000 hashfull 1 tbhits 0 time 5 pv e2e4 e7e5 g1f3 g8f6 f3e5 f6e4 d2d4 b8c6
                info depth 10 seldepth 10 multipv 1 score cp 50 nodes 7548 nps 943500 hashfull 2 tbhits 0 time 8 pv e2e4 e7e5 g1f3 g8f6 b1c3 d7d6 d2d4
                bestmove e2e4 ponder e7e5
            */
            if (args.Data.StartsWith("bestmove"))
            {
                bestMove = new Move(args.Data.Split(' ')[1], board);
                stockfish.StandardInput.WriteLine("quit");
                stockfish.Close();
            }
        };

        try
        {
            stockfish.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteLine(
                "Unable to find stockfish binaries, expecting a binary file named "
                + STOCKFISH_BINARY + " inside of: \n" + Directory.GetCurrentDirectory()
                + "\n\nDownload your stockfish binaries at https://stockfishchess.org/download/"
            );
            Environment.Exit(0);
        }

        stockfish.BeginOutputReadLine();

        stockfish.StandardInput.WriteLine("setoption name Threads value " + THREADS);
        stockfish.StandardInput.WriteLine("setoption name Skill Level value " + SKILL_LEVEL);
        stockfish.StandardInput.WriteLine("position fen " + board.GetFenString());
        stockfish.StandardInput.WriteLine("go depth " + MAX_DEPTH);

        stockfish.WaitForExit();

        if (timer.MillisecondsElapsedThisTurn < MS_PER_MOVE)
            System.Threading.Thread.Sleep(MS_PER_MOVE - timer.MillisecondsElapsedThisTurn);

        return bestMove;
    }
}