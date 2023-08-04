using ChessChallenge.API;
using System;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    private Board board;
    private Timer timer;
    private List<int> scores = new List<int>();

    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        this.timer = timer;

        var alpha = int.MinValue;
        var beta = int.MaxValue;
        var score = NegaMax(3, alpha, beta);

        var idx = 0;
        foreach (var s in scores)
        {
            if (s == score) return board.GetLegalMoves()[idx];
            idx++;
        }

        return board.GetLegalMoves()[0];
    }

    private int NegaMax(int depth, int alpha, int beta)
    {
        if (depth == 0) { return Eval(); }

        int score;
        foreach(var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            score = -NegaMax(0, alpha, beta);
            board.UndoMove(move);

            if (depth == 0) { scores.Add(score); }

            if (score >= beta) { return beta; }
            if (score > alpha) { alpha = score; }
        }

        return alpha;
    }

    private int Eval()
    {
        return 0;
    }
}