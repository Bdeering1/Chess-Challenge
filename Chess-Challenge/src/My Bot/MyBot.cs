using ChessChallenge.API;
using System;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private List<int> scores;

    private int DEPTH = 1;

    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        this.timer = timer;

        scores = new();
        var alpha = int.MinValue;
        var beta = int.MaxValue;
        var score = NegaMax(DEPTH, alpha, beta);

        Console.WriteLine(scores.Count);
        foreach (var s in scores)
        {
            Console.Write($"{s}, ");
        }
        Console.WriteLine();

        var idx = 0;
        foreach (var s in scores)
        {
            if (s == score)
            {
                Console.WriteLine(idx);
                return board.GetLegalMoves()[idx];
            }
            idx++;
        }

        return board.GetLegalMoves()[0];
    }

    private int NegaMax(int depth, int alpha, int beta)
    {
        if (depth == 0) return Eval();

        int score;
        foreach(var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            score = NegaMax(depth - 1, -beta, -alpha);
            board.UndoMove(move);

            if (depth == DEPTH) { scores.Add(score); }

            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }

        return alpha;
    }

    private int Eval()
    {
        var score = 0;
        foreach (var list in board.GetAllPieceLists())
        {
            var value = (int)list.TypeOfPieceInList switch
            {
                1 => 100,
                2 or 3 => 325,
                4 => 550,
                5 => 1000,
                _ => 50000,
            } * list.Count;

            if (!list.IsWhitePieceList) value = -value;
            score += value;
        }

        if (board.IsWhiteToMove) score = -score;
        return score;
    }
}